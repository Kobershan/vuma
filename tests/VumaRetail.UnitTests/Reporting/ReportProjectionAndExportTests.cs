using System.Text;
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.UnitTests.Reporting;

public sealed class ReportProjectionAndExportTests
{
    [Fact]
    public void Projection_deduplicates_replay_and_keeps_currency_totals_separate()
    {
        Guid companyId = Guid.NewGuid();
        var projection = new ReportingProjection(companyId, "sales");
        ReportingProjectionEvent sale = Event(companyId, "0001", "ZAR", ("revenue", 100m));
        ReportingProjectionEvent foreignSale = Event(companyId, "0002", "USD", ("revenue", 10m));

        projection.Apply(sale).Should().BeTrue();
        projection.Apply(sale).Should().BeFalse();
        projection.Apply(foreignSale).Should().BeTrue();

        projection.SnapshotMeasures().Should().BeEquivalentTo(new Dictionary<string, decimal>
        {
            ["revenue|ZAR"] = 100m,
            ["revenue|USD"] = 10m
        });
        projection.AppliedEventCount.Should().Be(2);
    }

    [Fact]
    public void Rebuild_produces_the_same_totals_as_incremental_replay()
    {
        Guid companyId = Guid.NewGuid();
        ReportingProjectionEvent[] events =
        [
            Event(companyId, "0002", "ZAR", ("revenue", 75m)),
            Event(companyId, "0001", "ZAR", ("revenue", 25m)),
            Event(companyId, "0003", "ZAR", ("orders", 1m))
        ];
        var incremental = new ReportingProjection(companyId, "sales");
        foreach (ReportingProjectionEvent item in events.OrderBy(x => x.Cursor, StringComparer.Ordinal))
        {
            incremental.Apply(item);
        }
        var rebuilt = new ReportingProjection(companyId, "sales");
        rebuilt.Rebuild(events.Append(events[0]));

        rebuilt.SnapshotMeasures().Should().BeEquivalentTo(incremental.SnapshotMeasures());
        rebuilt.AppliedEventCount.Should().Be(incremental.AppliedEventCount);
    }

    [Fact]
    public async Task Csv_exporter_escapes_values_and_is_deterministic()
    {
        ReportExport export = ReportExport.Queue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        RenderedReport result = await new CsvReportExporter().RenderAsync(export,
            new ReportDataSet(["name", "amount"], [["A, B", "10"], ["quoted \"item\"", "20"]]), CancellationToken.None);

        result.ContentType.Should().Be("text/csv; charset=utf-8");
        result.FileExtension.Should().Be(".csv");
        Encoding.UTF8.GetString(result.Content).Should().Be("name,amount\r\n\"A, B\",10\r\n\"quoted \"\"item\"\"\",20\r\n");
    }

    [Fact]
    public async Task Export_executor_renders_stores_and_completes_the_queued_request()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        ReportExport export = ReportExport.Queue(tenantId, null, companyId, Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        var reports = Substitute.For<IReportingRepository>();
        reports.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);
        var data = Substitute.For<IReportDataSource>();
        data.ReadAsync(export, Arg.Any<CancellationToken>()).Returns(new ReportDataSet(["amount"], [["100"]]));
        var renderer = Substitute.For<IReportExporter>();
        renderer.RenderAsync(export, Arg.Any<ReportDataSet>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedReport(Encoding.UTF8.GetBytes("amount\r\n100\r\n"), "text/csv", ".csv"));
        var artifacts = Substitute.For<IReportArtifactStore>();
        artifacts.PutAsync(tenantId, companyId, export.Id, ".csv", Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns("artifact.csv");
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

        bool completed = await new ReportExportExecutor(reports, data, renderer, artifacts, tenant, company, clock)
            .ExecuteAsync(export.Id);

        completed.Should().BeTrue();
        export.Status.Should().Be(ReportExportStatus.Completed);
        export.ArtifactReference.Should().Be("artifact.csv");
    }

    [Fact]
    public async Task Filesystem_artifact_store_round_trips_and_rejects_path_escape()
    {
        string root = Path.Combine(Path.GetTempPath(), $"vuma-report-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemReportArtifactStore(new ReportArtifactStoreOptions { RootDirectory = root });
            Guid tenantId = Guid.NewGuid();
            Guid companyId = Guid.NewGuid();
            Guid exportId = Guid.NewGuid();
            await using (MemoryStream content = new(Encoding.UTF8.GetBytes("report")))
            {
                string reference = await store.PutAsync(tenantId, companyId, exportId, ".csv", content);
                await using Stream roundTrip = await store.GetAsync(reference);
                using StreamReader reader = new(roundTrip);
                (await reader.ReadToEndAsync()).Should().Be("report");
            }

            await FluentActions.Invoking(() => store.GetAsync("../../outside.csv"))
                .Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Scheduled_report_advances_past_all_missed_intervals_and_can_be_disabled()
    {
        DateTimeOffset first = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        ScheduledReport schedule = ScheduledReport.Create(Guid.NewGuid(), null, Guid.NewGuid(), "sales", 60, first);

        schedule.IsDue(first.AddHours(3)).Should().BeTrue();
        schedule.Advance(first.AddHours(3));
        schedule.NextRunAtUtc.Should().Be(first.AddHours(4));
        schedule.Disable();
        schedule.IsDue(first.AddHours(5)).Should().BeFalse();
    }

    [Fact]
    public async Task Schedule_command_requires_published_report_and_active_company()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        var reports = Substitute.For<IReportingRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        tenant.StoreId.Returns((Guid?)null);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ReportDefinition definition = ReportDefinition.Create(tenantId, null, "sales", "Sales");
        definition.Publish();
        reports.FindPublishedDefinitionByCodeAsync("sales", Arg.Any<CancellationToken>()).Returns(definition);

        await new ScheduleReportCommandHandler(reports, tenant, company)
            .HandleAsync(new ScheduleReportCommand(companyId, "sales", 60, DateTimeOffset.UtcNow.AddHours(1)));

        reports.Received(1).Add(Arg.Is<ScheduledReport>(x => x.CompanyId == companyId && x.ReportCode == "SALES"));
    }

    [Fact]
    public async Task Schedule_runner_enqueues_due_work_once_and_advances_the_schedule()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        DateTimeOffset due = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        ScheduledReport schedule = ScheduledReport.Create(tenantId, null, companyId, "sales", 60, due);
        ReportDefinition definition = ReportDefinition.Create(tenantId, null, "sales", "Sales");
        definition.Publish();
        var reports = Substitute.For<IReportingRepository>();
        reports.ListDueSchedulesAsync(due.AddMinutes(1), 20, Arg.Any<CancellationToken>()).Returns([schedule]);
        reports.FindPublishedDefinitionByCodeAsync("SALES", Arg.Any<CancellationToken>()).Returns(definition);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        ReportScheduleRunResult result = await new ReportScheduleRunner(reports, tenant).EnqueueDueAsync(due.AddMinutes(1), 20);

        result.Examined.Should().Be(1);
        result.Enqueued.Should().Be(1);
        result.ExportIds.Should().ContainSingle();
        schedule.NextRunAtUtc.Should().Be(due.AddHours(1));
        reports.Received(1).Add(Arg.Is<ReportExport>(x => x.OperationId != Guid.Empty && x.ReportCode == "SALES"));
    }

    private static ReportingProjectionEvent Event(Guid companyId, string cursor, string currency, params (string Name, decimal Value)[] measures) =>
        new(Guid.NewGuid(), companyId, "sales", 1, cursor, currency, measures.ToDictionary(x => x.Name, x => x.Value));
}
