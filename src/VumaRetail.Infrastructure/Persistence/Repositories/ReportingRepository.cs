using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class ReportingRepository(VumaRetailDbContext context) : IReportingRepository
{
    public Task<ReportDefinition?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken = default) => context.ReportDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ReportDefinition?> FindDefinitionByCodeAsync(string code, CancellationToken cancellationToken = default) => context.ReportDefinitions.FirstOrDefaultAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);
    public Task<ProjectionCheckpoint?> FindCheckpointAsync(Guid companyId, string source, CancellationToken cancellationToken = default) => context.ProjectionCheckpoints.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Source == source.Trim(), cancellationToken);
    public void Add(ReportDefinition definition) => context.ReportDefinitions.Add(definition);
    public void Add(ProjectionCheckpoint checkpoint) => context.ProjectionCheckpoints.Add(checkpoint);
}
