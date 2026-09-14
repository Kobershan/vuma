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
    public async Task Export_completion_requires_the_active_company_and_records_artifact()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var export = ReportExport.Queue(tenantId, null, companyId, Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        var reports = Substitute.For<IReportingRepository>();
        reports.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var clock = Substitute.For<IClock>();
        var completedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(completedAt);

        await new CompleteReportExportCommandHandler(reports, tenant, company, clock)
            .HandleAsync(new CompleteReportExportCommand(companyId, export.Id, "blob/report.csv"));

        export.Status.Should().Be(ReportExportStatus.Completed);
        export.ArtifactReference.Should().Be("blob/report.csv");
    }

    [Fact]
    public async Task Export_failure_from_another_company_is_refused()
    {
        var export = ReportExport.Queue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        var reports = Substitute.For<IReportingRepository>();
        reports.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());

        await FluentActions.Invoking(() => new FailReportExportCommandHandler(reports, tenant, company)
            .HandleAsync(new FailReportExportCommand(company.CompanyId!.Value, export.Id, "provider unavailable")))
            .Should().ThrowAsync<InvalidOperationException>();
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
        ITenantContext tenant = Substitute.For<ITenantContext>();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        Guid exportCompany = Guid.NewGuid();
        tenant.TenantId.Returns(Guid.NewGuid());
        company.CompanyId.Returns(Guid.NewGuid());
        ReportExport export = ReportExport.Queue(Guid.NewGuid(), null, exportCompany, Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        repository.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);

        (await new GetReportExportQueryHandler(repository, tenant, company).HandleAsync(new GetReportExportQuery(export.Id))).Should().BeNull();
    }

    [Fact]
    public async Task Export_download_grant_requires_completed_export_and_active_company()
    {
        var companyId = Guid.NewGuid();
        var export = ReportExport.Queue(Guid.NewGuid(), null, companyId, Guid.NewGuid(), "sales", DateTimeOffset.UtcNow);
        export.Complete(DateTimeOffset.UtcNow, "blob/report.csv");
        var repository = Substitute.For<IReportingRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(export.TenantId);
        repository.FindExportAsync(export.Id, Arg.Any<CancellationToken>()).Returns(export);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var authorizer = Substitute.For<IReportExportDownloadAuthorizer>();
        authorizer.Create(export, Arg.Any<DateTimeOffset>()).Returns("opaque-report-grant");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

        ReportExportDownloadResult? result = await new AuthorizeReportExportDownloadQueryHandler(repository, authorizer, tenant, company, clock)
            .HandleAsync(new AuthorizeReportExportDownloadQuery(export.Id));

        result.Should().NotBeNull();
        result!.Token.Should().Be("opaque-report-grant");
        result.ExpiresAtUtc.Should().Be(clock.UtcNow.AddMinutes(15));
    }

}
