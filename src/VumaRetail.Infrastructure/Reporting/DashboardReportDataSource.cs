using System.Globalization;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Infrastructure.Reporting;

/// <summary>Exports the persisted dashboard measure projection as a deterministic tabular report.</summary>
public sealed class DashboardReportDataSource(IReportingRepository reports) : IReportDataSource
{
    public async Task<ReportDataSet> ReadAsync(ReportExport export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        DateOnly businessDate = DateOnly.FromDateTime(export.RequestedAtUtc.UtcDateTime);
        IReadOnlyList<DashboardMeasure> measures = await reports.ListMeasuresAsync(export.CompanyId!.Value, businessDate, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<IReadOnlyList<string>> rows = measures
            .Select(x => (IReadOnlyList<string>)[x.Name, x.Currency, x.Value.ToString(CultureInfo.InvariantCulture)])
            .ToArray();
        return new ReportDataSet(["measure", "currency", "value"], rows);
    }
}
