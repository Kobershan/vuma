#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Reporting;

[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class ScheduledReport : Entity
{
    private ScheduledReport(Guid tenantId, Guid? storeId, Guid companyId, string reportCode,
        int intervalMinutes, DateTimeOffset nextRunAtUtc) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        ReportCode = reportCode.Trim().ToUpperInvariant();
        IntervalMinutes = intervalMinutes;
        NextRunAtUtc = nextRunAtUtc.ToUniversalTime();
        IsEnabled = true;
    }

    private ScheduledReport() { }
    public string ReportCode { get; private set; } = string.Empty;
    public int IntervalMinutes { get; private set; }
    public DateTimeOffset NextRunAtUtc { get; private set; }
    public bool IsEnabled { get; private set; }

    public static ScheduledReport Create(Guid tenantId, Guid? storeId, Guid companyId, string reportCode,
        int intervalMinutes, DateTimeOffset nextRunAtUtc)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Schedule scope is required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        if (intervalMinutes is < 5 or > 10080)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        }
        return new(tenantId, storeId, companyId, reportCode, intervalMinutes, nextRunAtUtc);
    }

    public bool IsDue(DateTimeOffset nowUtc) => IsEnabled && NextRunAtUtc <= nowUtc.ToUniversalTime();

    public void Advance(DateTimeOffset nowUtc)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("A disabled report schedule cannot advance.");
        }
        DateTimeOffset next = NextRunAtUtc;
        DateTimeOffset now = nowUtc.ToUniversalTime();
        do
        {
            next = next.AddMinutes(IntervalMinutes);
        }
        while (next <= now);
        NextRunAtUtc = next;
    }

    public void Disable() => IsEnabled = false;
    public void Enable(DateTimeOffset nextRunAtUtc) { IsEnabled = true; NextRunAtUtc = nextRunAtUtc.ToUniversalTime(); }
}
