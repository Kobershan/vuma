using FluentAssertions;
using NSubstitute;
using VumaRetail.Application;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.UnitTests.Reporting;

public sealed class DashboardQueryTests
{
    [Fact]
    public async Task Overview_is_company_scoped_and_marks_old_checkpoints_stale()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        var reports = Substitute.For<IReportingRepository>();
        reports.ListCheckpointsAsync(companyId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            ProjectionCheckpoint.Create(tenantId, null, companyId, "sales")
        });
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

        DashboardSnapshot result = await new GetDashboardOverviewQueryHandler(reports, company, clock)
            .HandleAsync(new GetDashboardOverviewQuery(companyId, new DateOnly(2026, 9, 15)));

        result.CompanyId.Should().Be(companyId);
        result.Contributors.Should().ContainSingle(x => x.Contributor == "sales" && x.IsStale);
        result.IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task Overview_rejects_an_inactive_company_before_reading_checkpoints()
    {
        Guid active = Guid.NewGuid();
        var reports = Substitute.For<IReportingRepository>();
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(active);
        var clock = Substitute.For<IClock>();

        await FluentActions.Invoking(() => new GetDashboardOverviewQueryHandler(reports, company, clock)
                .HandleAsync(new GetDashboardOverviewQuery(Guid.NewGuid(), new DateOnly(2026, 9, 15))))
            .Should().ThrowAsync<InvalidOperationException>();
        await reports.DidNotReceive().ListCheckpointsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
