using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One day's record that a control account disagreed with its sub-ledger — the evidence the
/// "automated daily job" ADR-016 calls for leaves behind.
/// </summary>
/// <remarks>
/// Written by <c>FinanceReconciliationHostedService</c>, once per open period per control account,
/// whether or not there is a variance to report — a clean run is itself the fact that matters when
/// somebody asks "was this checked yesterday". Append-only like <see cref="Domain.Platform.AuditEntry"/>:
/// a flag is never edited, only superseded by the next day's row.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ReconciliationVarianceFlag : Entity, IImmutableRecord
{
    private ReconciliationVarianceFlag(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ReconciliationVarianceFlag()
    {
    }

    /// <summary>The period this check ran against.</summary>
    public Guid AccountingPeriodId { get; private set; }

    /// <summary>The control account checked.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Which sub-ledger it was checked against.</summary>
    public ControlAccountType ControlAccountType { get; private set; }

    /// <summary>The GL balance at the time of the check.</summary>
    public decimal GlBalance { get; private set; }

    /// <summary>The sub-ledger's own balance at the time of the check.</summary>
    public decimal SubLedgerBalance { get; private set; }

    /// <summary>The difference. Zero means reconciled that day.</summary>
    public decimal Variance { get; private set; }

    /// <summary>When the check ran, UTC.</summary>
    public DateTimeOffset CheckedAt { get; private set; }

    /// <summary>Records one day's check.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="accountingPeriodId">The period checked.</param>
    /// <param name="accountId">The control account checked.</param>
    /// <param name="controlAccountType">Which sub-ledger it was checked against.</param>
    /// <param name="glBalance">The GL balance at the time of the check.</param>
    /// <param name="subLedgerBalance">The sub-ledger's own balance at the time of the check.</param>
    /// <param name="checkedAt">When the check ran, UTC.</param>
    public static ReconciliationVarianceFlag Record(
        Guid tenantId,
        Guid accountingPeriodId,
        Guid accountId,
        ControlAccountType controlAccountType,
        decimal glBalance,
        decimal subLedgerBalance,
        DateTimeOffset checkedAt)
        => new(tenantId)
        {
            AccountingPeriodId = accountingPeriodId,
            AccountId = accountId,
            ControlAccountType = controlAccountType,
            GlBalance = glBalance,
            SubLedgerBalance = subLedgerBalance,
            Variance = glBalance - subLedgerBalance,
            CheckedAt = checkedAt,
        };
}
