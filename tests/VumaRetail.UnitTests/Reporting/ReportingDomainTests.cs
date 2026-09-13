using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Reporting;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.UnitTests.Reporting;

public sealed class ReportingDomainTests
{
    [Fact]
    public void Checkpoint_replay_does_not_move_cursor_backwards()
    {
        ProjectionCheckpoint checkpoint = ProjectionCheckpoint.Create(Guid.NewGuid(), null, Guid.NewGuid(), "sales", cursor: "0002");
        checkpoint.Advance(1, "0002");
        checkpoint.Cursor.Should().Be("0002");
        FluentActions.Invoking(() => checkpoint.Advance(0, "9999")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dashboard_is_not_live_when_a_contributor_is_stale()
    {
        DashboardSnapshot snapshot = new(Guid.NewGuid(), new DateOnly(2026, 9, 13), DateTimeOffset.UtcNow,
            [new ReportFreshness("store-1", DateTimeOffset.UtcNow.AddHours(-24), true)],
            new Dictionary<string, decimal> { ["sales"] = 100m });
        snapshot.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Report_definition_requires_publish_before_retire()
    {
        ReportDefinition definition = ReportDefinition.Create(Guid.NewGuid(), null, "sales", "Sales");
        FluentActions.Invoking(definition.Retire).Should().Throw<InvalidOperationException>();
        definition.Publish(); definition.Retire(); definition.Status.Should().Be(ReportDefinitionStatus.Retired);
    }

    [Fact]
    public async Task Report_definition_query_does_not_expose_draft_or_retired_reports()
    {
        IReportingRepository repository = Substitute.For<IReportingRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-13T12:00:00Z"));
        GetReportDefinitionQueryHandler handler = new(repository, clock);

        (await handler.HandleAsync(new GetReportDefinitionQuery("sales"))).Should().BeNull();
        await repository.Received(1).FindPublishedDefinitionByCodeAsync("sales", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_request_requires_a_published_report_and_is_idempotent()
    {
        IReportingRepository repository = Substitute.For<IReportingRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        IClock clock = Substitute.For<IClock>();
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        tenant.TenantId.Returns(tenantId);
        tenant.StoreId.Returns((Guid?)null);
        company.CompanyId.Returns(companyId);
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-13T12:00:00Z"));
        ReportDefinition definition = ReportDefinition.Create(tenantId, null, "sales", "Sales");
        definition.Publish();
        repository.FindPublishedDefinitionByCodeAsync("sales", Arg.Any<CancellationToken>()).Returns(definition);
        ReportExport? captured = null;
        repository.When(x => x.Add(Arg.Any<ReportExport>())).Do(call => captured = call.Arg<ReportExport>());
        RequestReportExportCommandHandler handler = new(repository, tenant, company, clock);

        Guid id = await handler.HandleAsync(new RequestReportExportCommand(companyId, operationId, "sales"));
        id.Should().NotBeEmpty();
        captured.Should().NotBeNull();
        ReportExport created = captured!;
        repository.FindExportByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(created);
        (await handler.HandleAsync(new RequestReportExportCommand(companyId, operationId, "SALES"))).Should().Be(id);
        repository.Received(1).Add(Arg.Any<ReportExport>());
    }

    [Fact]
    public async Task Export_status_is_not_visible_to_another_company()
    {
        IReportingRepository repository = Substitute.For<IReportingRepository>();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        Guid exportCompany = Guid.NewGuid();
        company.CompanyId.Returns(Guid.NewGuid());
        ReportExport export = ReportExport.Queue(Guid.NewGuid(), null, exportCompany, Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        repository.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);

        (await new GetReportExportQueryHandler(repository, company).HandleAsync(new GetReportExportQuery(export.Id))).Should().BeNull();
    }

}
