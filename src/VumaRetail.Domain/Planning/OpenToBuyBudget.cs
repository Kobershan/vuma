using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// An open-to-buy budget: what may be committed for a month, per category or for everything.
/// </summary>
/// <remarks>
/// <para>
/// <c>Remaining = Planned − Committed</c>, where Committed is read live from procurement
/// (submitted/approved requisitions plus open orders) every time the budget is read. The budget
/// row stores only the plan — never the commitments — so a cancelled requisition stops counting
/// the moment it is cancelled.
/// </para>
/// <para>
/// Open-to-buy is a warning, never a blocker: a replenishment suggestion raised past the budget
/// is flagged, not refused.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class OpenToBuyBudget : Entity
{
    private OpenToBuyBudget()
    {
    }

    private OpenToBuyBudget(
        Guid tenantId,
        Guid companyId,
        int year,
        int month,
        string? categoryCode,
        decimal plannedAmount,
        string currency)
        : base(tenantId)
    {
        AssignCompany(companyId);
        Year = year;
        Month = month;
        CategoryCode = categoryCode;
        PlannedAmount = plannedAmount;
        Currency = currency;
    }

    /// <summary>The budget year.</summary>
    public int Year { get; private set; }

    /// <summary>The budget month, 1–12.</summary>
    public int Month { get; private set; }

    /// <summary>The category budgeted, or <c>null</c> for the whole company.</summary>
    public string? CategoryCode { get; private set; }

    /// <summary>What may be committed this month.</summary>
    public decimal PlannedAmount { get; private set; }

    /// <summary>The ISO 4217 currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Re-plans the month. Budgets are management intent, freely revised.</summary>
    public void Replan(decimal plannedAmount)
    {
        if (plannedAmount < 0m)
        {
            throw PlanningRuleException.BadInput("Planned open-to-buy cannot be negative.");
        }

        PlannedAmount = plannedAmount;
    }

    /// <summary>Creates a monthly open-to-buy budget.</summary>
    /// <exception cref="PlanningRuleException">The month or amount is invalid.</exception>
    public static OpenToBuyBudget Create(
        Guid tenantId,
        Guid companyId,
        int year,
        int month,
        string? categoryCode,
        decimal plannedAmount,
        string currency)
    {
        if (month < 1 || month > 12)
        {
            throw PlanningRuleException.BadInput("Month must be 1–12.");
        }

        if (plannedAmount < 0m)
        {
            throw PlanningRuleException.BadInput("Planned open-to-buy cannot be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new OpenToBuyBudget(
            tenantId,
            companyId,
            year,
            month,
            string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim(),
            plannedAmount,
            currency.Trim().ToUpperInvariant());
    }
}
