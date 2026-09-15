#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public sealed record GetDashboardOverviewQuery(Guid CompanyId, DateOnly BusinessDate) : IQuery<DashboardSnapshot>;

public sealed class GetDashboardOverviewQueryHandler(IReportingRepository reports, ICompanyContext company, IClock clock)
    : IQueryHandler<GetDashboardOverviewQuery, DashboardSnapshot>
{
    public async Task<DashboardSnapshot> HandleAsync(GetDashboardOverviewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (company.CompanyId is not { } active || active != query.CompanyId)
        {
            throw new InvalidOperationException("The dashboard company is not the active company.");
        }
        DateTimeOffset now = clock.UtcNow;
        IReadOnlyList<ProjectionCheckpoint> checkpoints = await reports.ListCheckpointsAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        ReportFreshness[] contributors = checkpoints.Select(x => new ReportFreshness(x.Source, now,
            now - x.UpdatedAt > TimeSpan.FromHours(24))).ToArray();
        return new DashboardSnapshot(query.CompanyId, query.BusinessDate, now, contributors,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase));
    }
}
