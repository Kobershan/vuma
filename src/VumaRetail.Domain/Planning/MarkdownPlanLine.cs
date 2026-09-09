using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// One SKU on a markdown plan, with the decision inputs snapshotted at authoring time.
/// </summary>
/// <remarks>
/// Live price and average cost are resolved at authoring/activation and stored here, so a plan
/// reprinted later shows what was actually decided (ADR-112, ADR-113).
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class MarkdownPlanLine : Entity
{
    private MarkdownPlanLine()
    {
    }

    private MarkdownPlanLine(
        Guid tenantId,
        Guid companyId,
        Guid markdownPlanId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal currentPrice,
        decimal proposedDiscountPercent,
        string currency,
        string? abcXyz,
        decimal sellThroughPercent,
        decimal daysOfSupply)
        : base(tenantId)
    {
        AssignCompany(companyId);
        MarkdownPlanId = markdownPlanId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        CurrentPrice = currentPrice;
        ProposedDiscountPercent = proposedDiscountPercent;
        Currency = currency;
        AbcXyz = abcXyz;
        SellThroughPercent = sellThroughPercent;
        DaysOfSupply = daysOfSupply;
    }

    /// <summary>The plan this line belongs to. A bare id — never a cross-schema foreign key.</summary>
    public Guid MarkdownPlanId { get; private set; }

    /// <summary>The item, or <c>null</c> for a variant.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> for an item.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The live resolved price when the line was authored.</summary>
    public decimal CurrentPrice { get; private set; }

    /// <summary>The proposed percentage off, 0–100.</summary>
    public decimal ProposedDiscountPercent { get; private set; }

    /// <summary>The ISO 4217 currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The ABC/XYZ snapshot (e.g. <c>C/Z</c>) behind the proposal, if classified.</summary>
    public string? AbcXyz { get; private set; }

    /// <summary>Sell-through percent over the review window.</summary>
    public decimal SellThroughPercent { get; private set; }

    /// <summary>Days of supply at current demand.</summary>
    public decimal DaysOfSupply { get; private set; }

    /// <summary>The Stage 10 promotion this line activated as, if it has been activated.</summary>
    public Guid? PromotionId { get; private set; }

    /// <summary>Records the promotion an activation created. One line activates once.</summary>
    /// <param name="promotionId">The Stage 10 promotion.</param>
    public void MarkPromoted(Guid promotionId)
    {
        if (PromotionId is not null)
        {
            throw PlanningRuleException.BadTransition("Markdown plan line", "promoted", "promote again");
        }

        PromotionId = promotionId;
    }

    internal static MarkdownPlanLine Create(
        Guid tenantId,
        Guid companyId,
        Guid markdownPlanId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal currentPrice,
        decimal proposedDiscountPercent,
        string currency,
        string? abcXyz,
        decimal sellThroughPercent,
        decimal daysOfSupply)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        if (currentPrice < 0m)
        {
            throw PlanningRuleException.BadInput("Current price cannot be negative.");
        }

        if (proposedDiscountPercent <= 0m || proposedDiscountPercent > 100m)
        {
            throw PlanningRuleException.BadInput("Proposed discount must be 0–100 (exclusive of 0).");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new MarkdownPlanLine(
            tenantId,
            companyId,
            markdownPlanId,
            itemId,
            itemVariantId,
            currentPrice,
            proposedDiscountPercent,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(abcXyz) ? null : abcXyz.Trim(),
            sellThroughPercent,
            daysOfSupply);
    }
}
