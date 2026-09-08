using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// The tenant's customer-money policy: interest, settlement discount, lay-by fees and terms.
/// One row per tenant. Rates live here as data so a change is an update, never a release
/// (mistake #9: hard-coding what is configuration).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class CustomerFinanceTerms : Entity
{
    private CustomerFinanceTerms(
        Guid tenantId,
        decimal interestMonthlyRate,
        decimal settlementDiscountRate,
        int settlementDiscountDays,
        Money layByAdminFee,
        int layByMaxTermMonths,
        int staleBalanceMinutes)
        : base(tenantId, null)
    {
        InterestMonthlyRate = interestMonthlyRate;
        SettlementDiscountRate = settlementDiscountRate;
        SettlementDiscountDays = settlementDiscountDays;
        LayByAdminFee = layByAdminFee;
        LayByMaxTermMonths = layByMaxTermMonths;
        StaleBalanceMinutes = staleBalanceMinutes;
    }

    private CustomerFinanceTerms()
    {
    }

    /// <summary>Monthly fraction charged on overdue balances. 0.02 is two percent.</summary>
    public decimal InterestMonthlyRate { get; private set; }

    /// <summary>Fraction taken off for payment inside the discount window.</summary>
    public decimal SettlementDiscountRate { get; private set; }

    /// <summary>Days from invoice date inside which the settlement discount applies.</summary>
    public int SettlementDiscountDays { get; private set; }

    /// <summary>Kept from a cancelled lay-by. Snapshotted onto each agreement at opening.</summary>
    public Money LayByAdminFee { get; private set; }

    /// <summary>The longest lay-by term the shop offers, in months.</summary>
    public int LayByMaxTermMonths { get; private set; }

    /// <summary>Minutes after which a cached balance is too stale to pay out against offline.</summary>
    public int StaleBalanceMinutes { get; private set; }

    /// <summary>Seeds the tenant's policy row.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="currency">The ISO 4217 currency for the admin fee.</param>
    public static CustomerFinanceTerms Seed(Guid tenantId, string currency)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Terms must belong to a tenant.", nameof(tenantId));
        }

        return new CustomerFinanceTerms(
            tenantId, 0.02m, 0.02m, 10, new Money(100m, currency), 6, 15);
    }
}
