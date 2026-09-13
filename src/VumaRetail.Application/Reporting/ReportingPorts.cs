#pragma warning disable CS1591
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public interface IReportingRepository
{
    Task<ReportDefinition?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportDefinition?> FindDefinitionByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ProjectionCheckpoint?> FindCheckpointAsync(Guid companyId, string source, CancellationToken cancellationToken = default);
    void Add(ReportDefinition definition);
    void Add(ProjectionCheckpoint checkpoint);
}
