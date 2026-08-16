using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// One thing a <see cref="Promotion"/> applies to — an item, a variant, or a whole category.
/// </summary>
/// <remarks>
/// A promotion with no lines at all applies to everything, which is how a store-wide clearance is
/// expressed. A promotion with lines applies only to what they name, and one matching line is enough.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PromotionLine : Entity
{
    private PromotionLine(
        Guid tenantId,
        Guid? storeId,
        Guid promotionId,
        Guid? itemId,
        Guid? itemVariantId,
        string? categoryCode)
        : base(tenantId, storeId)
    {
        PromotionId = promotionId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        CategoryCode = categoryCode;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PromotionLine()
    {
    }

    /// <summary>The promotion this line belongs to.</summary>
    public Guid PromotionId { get; private set; }

    /// <summary>The item targeted, when the promotion names one.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant targeted, when the promotion names one.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The category targeted, upper-cased, when the promotion covers a whole shelf.</summary>
    public string? CategoryCode { get; private set; }

    /// <summary>Creates a promotion line. Prefer <see cref="Promotion.AddLine"/>.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, inherited from the promotion.</param>
    /// <param name="promotionId">The promotion this line belongs to.</param>
    /// <param name="itemId">The item, when the promotion targets one.</param>
    /// <param name="itemVariantId">The variant, when it targets one.</param>
    /// <param name="categoryCode">The category, when it targets a whole shelf.</param>
    /// <exception cref="SalesRuleException">The line targets none of the three, or more than one.</exception>
    public static PromotionLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid promotionId,
        Guid? itemId,
        Guid? itemVariantId,
        string? categoryCode)
    {
        bool hasCategory = !string.IsNullOrWhiteSpace(categoryCode);
        int targets = (itemId.HasValue ? 1 : 0) + (itemVariantId.HasValue ? 1 : 0) + (hasCategory ? 1 : 0);

        if (targets != 1)
        {
            throw SalesRuleException.ExactlyOneItemOrVariantRequired();
        }

        return new PromotionLine(
            tenantId,
            storeId,
            promotionId,
            itemId,
            itemVariantId,
            hasCategory ? categoryCode!.Trim().ToUpperInvariant() : null);
    }

    /// <summary>True when this line targets the thing being sold.</summary>
    /// <param name="itemId">The item being sold, when it has no variants.</param>
    /// <param name="itemVariantId">The variant being sold.</param>
    /// <param name="categoryCode">The category the item sits in, when known.</param>
    public bool Matches(Guid? itemId, Guid? itemVariantId, string? categoryCode)
    {
        if (ItemId is { } targetItem)
        {
            return itemId == targetItem;
        }

        if (ItemVariantId is { } targetVariant)
        {
            return itemVariantId == targetVariant;
        }

        return categoryCode is not null
            && string.Equals(CategoryCode, categoryCode.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
