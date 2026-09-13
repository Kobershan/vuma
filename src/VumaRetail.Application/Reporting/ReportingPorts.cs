#pragma warning disable CS1591
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public interface IReportingRepository
{
    Task<ReportDefinition?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReportDefinition?> FindPublishedDefinitionByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ProjectionCheckpoint?> FindCheckpointAsync(Guid companyId, string source, CancellationToken cancellationToken = default);
    Task<ReportExport?> FindExportByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<ReportExport?> FindExportAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(ReportDefinition definition);
    void Add(ProjectionCheckpoint checkpoint);
    void Add(ReportExport export);
}
