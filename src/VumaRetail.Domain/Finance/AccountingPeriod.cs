using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One accounting period a journal may post into (ADR-016).
/// </summary>
/// <remarks>
/// Tenant-wide, like <see cref="Account"/> — one calendar, not one per store. Closing is gated by
/// <c>IPeriodVarianceChecker</c>: a control account that disagrees with its sub-ledger blocks the
/// close rather than letting the disagreement roll silently into the next period.
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class AccountingPeriod : Entity
{
    private AccountingPeriod(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private AccountingPeriod()
    {
    }

    /// <summary>The first day the period covers, inclusive.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>The last day the period covers, inclusive.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>Whether journals may still post into this period.</summary>
    public PeriodStatus Status { get; private set; } = PeriodStatus.Open;

    /// <summary>When the period was closed, UTC — or <c>null</c> while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who closed it.</summary>
    public string? ClosedBy { get; private set; }

    /// <summary>Opens a new period.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="periodStart">The first day, inclusive.</param>
    /// <param name="periodEnd">The last day, inclusive.</param>
    /// <exception cref="ArgumentException">The end precedes the start.</exception>
    public static AccountingPeriod Open(Guid tenantId, DateOnly periodStart, DateOnly periodEnd)
    {
        if (periodEnd < periodStart)
        {
            throw new ArgumentException("A period cannot end before it starts.", nameof(periodEnd));
        }

        return new AccountingPeriod(tenantId)
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Status = PeriodStatus.Open,
        };
    }

    /// <summary>Whether the given date falls inside this period.</summary>
    /// <param name="date">The date to test.</param>
    public bool Covers(DateOnly date) => date >= PeriodStart && date <= PeriodEnd;

    /// <summary>
    /// Closes the period. The caller is responsible for having already confirmed there is no
    /// unreconciled sub-ledger variance — this method only records the decision.
    /// </summary>
    /// <param name="closedBy">The principal closing it.</param>
    /// <param name="at">When, UTC.</param>
    /// <exception cref="PeriodAlreadyClosedException">The period is already closed.</exception>
    public void Close(string closedBy, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(closedBy);

        if (Status is PeriodStatus.Closed)
        {
            throw new PeriodAlreadyClosedException(Id);
        }

        Status = PeriodStatus.Closed;
        ClosedAt = at;
        ClosedBy = closedBy;
    }

    /// <summary>Reopens a closed period. A deliberate, audited correction path, not routine.</summary>
    public void Reopen()
    {
        Status = PeriodStatus.Open;
        ClosedAt = null;
        ClosedBy = null;
    }
}
