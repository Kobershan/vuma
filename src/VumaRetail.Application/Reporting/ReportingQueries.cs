#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public sealed record GetReportDefinitionQuery(string Code) : IQuery<ReportDefinitionResult?>;
public sealed record GetReportExportQuery(Guid Id) : IQuery<ReportExportResult?>;
public sealed record ReportDefinitionResult(Guid Id, string Code, string Name, string Status, DateTimeOffset AsAtUtc);
public sealed record ReportExportResult(Guid Id, Guid CompanyId, Guid OperationId, string ReportCode, string Status, DateTimeOffset RequestedAtUtc);

public sealed class GetReportDefinitionQueryHandler(IReportingRepository reports, IClock clock)
    : IQueryHandler<GetReportDefinitionQuery, ReportDefinitionResult?>
{
    public async Task<ReportDefinitionResult?> HandleAsync(GetReportDefinitionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ReportDefinition? definition = await reports.FindPublishedDefinitionByCodeAsync(query.Code, cancellationToken).ConfigureAwait(false);
        return definition is null ? null : new(definition.Id, definition.Code, definition.Name, definition.Status.ToString(), clock.UtcNow);
    }
}

public sealed class GetReportExportQueryHandler(IReportingRepository reports, ICompanyContext company) : IQueryHandler<GetReportExportQuery, ReportExportResult?>
{
    public async Task<ReportExportResult?> HandleAsync(GetReportExportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ReportExport? export = await reports.FindExportAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (export is not null && (company.CompanyId is not { } activeCompany || export.CompanyId != activeCompany))
        {
            return null;
        }
        return export is null ? null : new(export.Id, export.CompanyId!.Value, export.OperationId, export.ReportCode, export.Status.ToString(), export.RequestedAtUtc);
    }
}
