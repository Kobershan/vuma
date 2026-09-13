#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public sealed record GetReportDefinitionQuery(string Code) : IQuery<ReportDefinitionResult?>;
public sealed record ReportDefinitionResult(Guid Id, string Code, string Name, string Status, DateTimeOffset AsAtUtc);

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
