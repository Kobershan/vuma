#pragma warning disable CS1591
using System.Security.Cryptography;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

/// <summary>
/// Turns due schedules into idempotent export requests. The host owns the transaction boundary and
/// invokes <see cref="ReportExportExecutor"/> separately, so a slow renderer never holds this pass.
/// </summary>
public sealed class ReportScheduleRunner(IReportingRepository reports, ITenantContext tenant) : IReportScheduleRunner
{
    public async Task<ReportScheduleRunResult> EnqueueDueAsync(DateTimeOffset asOfUtc, int limit, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScheduledReport> schedules = await reports.ListDueSchedulesAsync(asOfUtc, Math.Clamp(limit, 1, 200), cancellationToken).ConfigureAwait(false);
        List<Guid> exportIds = [];
        foreach (ScheduledReport schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid operationId = OperationId(schedule);
            if (await reports.FindExportByOperationIdAsync(operationId, cancellationToken).ConfigureAwait(false) is null &&
                await reports.FindPublishedDefinitionByCodeAsync(schedule.ReportCode, cancellationToken).ConfigureAwait(false) is not null)
            {
                ReportExport export = ReportExport.Queue(tenant.TenantId, schedule.StoreId, schedule.CompanyId!.Value,
                    operationId, schedule.ReportCode, schedule.NextRunAtUtc);
                reports.Add(export);
                exportIds.Add(export.Id);
            }
            schedule.Advance(asOfUtc);
        }
        return new ReportScheduleRunResult(schedules.Count, exportIds.Count, exportIds);
    }

    private static Guid OperationId(ScheduledReport schedule)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes($"report-schedule:{schedule.Id:D}:{schedule.NextRunAtUtc.UtcDateTime:O}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
