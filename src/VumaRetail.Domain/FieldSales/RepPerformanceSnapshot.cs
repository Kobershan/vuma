using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// What a rep did in one closed month, per company (plus the group roll-up): computed once,
/// stored, never recomputed silently (ADR-110 — the same shape as the supplier scorecard).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class RepPerformanceSnapshot : Entity
{
    private RepPerformanceSnapshot(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RepPerformanceSnapshot()
    {
    }

    /// <summary>The rep.</summary>
    public Guid RepId { get; private set; }


    /// <summary>First day of the closed month.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Pro formas captured in the month.</summary>
    public int CapturedCount { get; private set; }

    /// <summary>Captured value.</summary>
    public Money CapturedValue { get; private set; }

    /// <summary>Value converted into orders.</summary>
    public Money ConvertedValue { get; private set; }

    /// <summary>Value rejected.</summary>
    public Money RejectedValue { get; private set; }

    /// <summary>Value expired unapproved.</summary>
    public Money ExpiredValue { get; private set; }

    /// <summary>Invoiced value in the month.</summary>
    public Money InvoicedValue { get; private set; }

    /// <summary>Credited value in the month.</summary>
    public Money CreditedValue { get; private set; }

    /// <summary>Net value: invoiced less credited.</summary>
    public Money NetValue { get; private set; }

    /// <summary>Margin value, where the snapshot was taken with cost visibility. Stored plain
    /// (ADR-067); the currency is always the row's.</summary>
    public decimal? MarginAmount { get; private set; }

    /// <summary>Margin, where the snapshot was taken with cost visibility.</summary>
    public Money? MarginValue => MarginAmount is { } margin
        ? new Money(margin, NetValue.Currency)
        : null;

    /// <summary>Whether margin is meaningful on this row.</summary>
    public bool HasMargin => MarginValue is not null;

    /// <summary>Distinct customers ordered in the month.</summary>
    public int ActiveCustomers { get; private set; }

    /// <summary>Monotonic version within (rep, company, period).</summary>
    public int Version { get; private set; }

    /// <summary>Why this version exists (scheduled close, recomputation, correction).</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When snapshotted, UTC.</summary>
    public DateTimeOffset SnapshottedAt { get; private set; }

    /// <summary>Snapshots one closed month. The period must be closed (ends before today).</summary>
    public static RepPerformanceSnapshot Snapshot(
        Guid tenantId,
        Guid? storeId,
        Guid repId,
        Guid? companyId,
        DateOnly periodStart,
        RepPerformanceFigures figures,
        int version,
        string reason,
        DateTimeOffset snapshottedAt,
        string currency)
    {
        if (tenantId == Guid.Empty || repId == Guid.Empty)
        {
            throw new ArgumentException("A snapshot names its tenant and rep.");
        }

        if (periodStart.Day != 1)
        {
            throw new ArgumentException("A snapshot period starts on the first of a month.", nameof(periodStart));
        }

        if (periodStart.AddMonths(1) > DateOnly.FromDateTime(snapshottedAt.UtcDateTime))
        {
            throw new ArgumentException("A snapshot covers a closed month only.", nameof(periodStart));
        }

        ArgumentNullException.ThrowIfNull(figures);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new RepPerformanceSnapshot(tenantId, storeId)
        {
            RepId = repId,
            CompanyId = companyId == Guid.Empty ? null : companyId,
            PeriodStart = periodStart,
            CapturedCount = figures.CapturedCount,
            CapturedValue = new Money(figures.CapturedValue, currency),
            ConvertedValue = new Money(figures.ConvertedValue, currency),
            RejectedValue = new Money(figures.RejectedValue, currency),
            ExpiredValue = new Money(figures.ExpiredValue, currency),
            InvoicedValue = new Money(figures.InvoicedValue, currency),
            CreditedValue = new Money(figures.CreditedValue, currency),
            NetValue = new Money(figures.InvoicedValue - figures.CreditedValue, currency),
            MarginAmount = figures.MarginValue,
            ActiveCustomers = figures.ActiveCustomers,
            Version = version,
            Reason = reason.Trim(),
            SnapshottedAt = snapshottedAt,
        };
    }
}

/// <summary>Plain figures a calculator folds out of pro formas and invoices.</summary>
/// <param name="CapturedCount">Pro formas captured.</param>
/// <param name="CapturedValue">Captured gross value.</param>
/// <param name="ConvertedValue">Approved-and-converted gross value.</param>
/// <param name="RejectedValue">Rejected gross value.</param>
/// <param name="ExpiredValue">Expired gross value.</param>
/// <param name="InvoicedValue">Invoiced value.</param>
/// <param name="CreditedValue">Credited value.</param>
/// <param name="MarginValue">Margin, or null without cost visibility.</param>
/// <param name="ActiveCustomers">Distinct customers ordered.</param>
public sealed record RepPerformanceFigures(
    int CapturedCount,
    decimal CapturedValue,
    decimal ConvertedValue,
    decimal RejectedValue,
    decimal ExpiredValue,
    decimal InvoicedValue,
    decimal CreditedValue,
    decimal? MarginValue,
    int ActiveCustomers);
