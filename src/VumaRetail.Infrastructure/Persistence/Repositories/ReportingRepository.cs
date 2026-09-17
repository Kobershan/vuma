using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class ReportingRepository(VumaRetailDbContext context) : IReportingRepository
{
    public Task<ReportDefinition?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken = default) => context.ReportDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ReportDefinition?> FindPublishedDefinitionByCodeAsync(string code, CancellationToken cancellationToken = default) => context.ReportDefinitions.FirstOrDefaultAsync(x => x.Code == code.Trim().ToUpperInvariant() && x.Status == ReportDefinitionStatus.Published, cancellationToken);
    public Task<ProjectionCheckpoint?> FindCheckpointAsync(Guid companyId, string source, CancellationToken cancellationToken = default) => context.ProjectionCheckpoints.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Source == source.Trim(), cancellationToken);
    public async Task<IReadOnlyList<ProjectionCheckpoint>> ListCheckpointsAsync(Guid companyId, CancellationToken cancellationToken = default) => await context.ProjectionCheckpoints.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Source).ToListAsync(cancellationToken).ConfigureAwait(false);
    public async Task<IReadOnlyList<DashboardMeasure>> ListMeasuresAsync(Guid companyId, DateOnly businessDate, CancellationToken cancellationToken = default) => await context.DashboardMeasures.AsNoTracking().Where(x => x.CompanyId == companyId && x.BusinessDate == businessDate).OrderBy(x => x.Name).ThenBy(x => x.Currency).ToListAsync(cancellationToken).ConfigureAwait(false);
    public Task<ReportExport?> FindExportByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default) => context.ReportExports.FirstOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
    public Task<ReportExport?> FindExportAsync(Guid id, CancellationToken cancellationToken = default) => context.ReportExports.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ScheduledReport?> FindScheduleAsync(Guid id, CancellationToken cancellationToken = default) => context.ScheduledReports.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<ScheduledReport>> ListDueSchedulesAsync(DateTimeOffset asOfUtc, int limit, CancellationToken cancellationToken = default) =>
        await context.ScheduledReports.Where(x => x.IsEnabled && x.NextRunAtUtc <= asOfUtc.ToUniversalTime()).OrderBy(x => x.NextRunAtUtc).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
    public void Add(ReportDefinition definition) => context.ReportDefinitions.Add(definition);
    public void Add(ProjectionCheckpoint checkpoint) => context.ProjectionCheckpoints.Add(checkpoint);
    public void Add(DashboardMeasure measure) => context.DashboardMeasures.Add(measure);
    public void Add(ReportExport export) => context.ReportExports.Add(export);
    public void Add(ScheduledReport schedule) => context.ScheduledReports.Add(schedule);
}
