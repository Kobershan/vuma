#pragma warning disable CS1591
using System.Text;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

public sealed record ReportDataSet(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public ReportDataSet Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Columns.FirstOrDefault());
        if (Columns.Count != Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new ArgumentException("Report columns must be unique.", nameof(Columns));
        }
        if (Rows.Any(row => row.Count != Columns.Count))
        {
            throw new ArgumentException("Every report row must contain one value per column.", nameof(Rows));
        }
        return this;
    }
}

public sealed record RenderedReport(byte[] Content, string ContentType, string FileExtension);

public interface IReportDataSource
{
    Task<ReportDataSet> ReadAsync(ReportExport export, CancellationToken cancellationToken = default);
}

public interface IReportExporter
{
    Task<RenderedReport> RenderAsync(ReportExport export, ReportDataSet data, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic UTF-8 CSV renderer. Values are escaped according to RFC 4180 rules.</summary>
public sealed class CsvReportExporter : IReportExporter
{
    public Task<RenderedReport> RenderAsync(ReportExport export, ReportDataSet data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();
        ReportDataSet normalized = data.Normalize();
        StringBuilder csv = new();
        AppendRow(csv, normalized.Columns);
        foreach (IReadOnlyList<string> row in normalized.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendRow(csv, row);
        }
        return Task.FromResult(new RenderedReport(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", ".csv"));
    }

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string> row)
    {
        for (int index = 0; index < row.Count; index++)
        {
            if (index > 0)
            {
                csv.Append(',');
            }
            string value = row[index] ?? string.Empty;
            bool quoted = value.Contains(',', StringComparison.Ordinal) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
            if (quoted)
            {
                csv.Append('"');
            }
            csv.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            if (quoted)
            {
                csv.Append('"');
            }
        }
        csv.Append("\r\n");
    }
}

public interface IReportArtifactStore
{
    Task<string> PutAsync(Guid tenantId, Guid companyId, Guid exportId, string extension, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> GetAsync(string artifactReference, CancellationToken cancellationToken = default);
}

/// <summary>Executes one queued export outside the trading request transaction.</summary>
public sealed class ReportExportExecutor(
    IReportingRepository reports,
    IReportDataSource dataSource,
    IReportExporter exporter,
    IReportArtifactStore artifacts,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
{
    public async Task<bool> ExecuteAsync(Guid exportId, CancellationToken cancellationToken = default)
    {
        ReportExport? export = await reports.FindExportAsync(exportId, cancellationToken).ConfigureAwait(false);
        if (export is null || export.Status != ReportExportStatus.Queued)
        {
            return false;
        }
        if (tenant.TenantId != export.TenantId || company.CompanyId is not { } active || active != export.CompanyId)
        {
            throw new InvalidOperationException("The report export is outside the active tenant/company scope.");
        }

        try
        {
            ReportDataSet data = await dataSource.ReadAsync(export, cancellationToken).ConfigureAwait(false);
            RenderedReport rendered = await exporter.RenderAsync(export, data, cancellationToken).ConfigureAwait(false);
            await using MemoryStream content = new(rendered.Content, writable: false);
            string reference = await artifacts.PutAsync(export.TenantId, export.CompanyId!.Value, export.Id,
                rendered.FileExtension, content, cancellationToken).ConfigureAwait(false);
            export.Complete(clock.UtcNow, reference);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            export.Fail(exception.Message.Length > 500 ? exception.Message[..500] : exception.Message);
            throw;
        }
    }
}
