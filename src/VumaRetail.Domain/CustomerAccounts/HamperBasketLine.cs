using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// One line on a hamper basket: what is in it, how much, and what to substitute when the shelf
/// is empty at settle time. Frozen at basket creation; the substituted line is priced at the
/// basket's group price, never re-resolved.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class HamperBasketLine : Entity
{
    private HamperBasketLine(
        Guid tenantId,
        Guid? storeId,
        Guid basketId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantityValue,
        string quantityUom,
        Guid? substitutionItemId,
        Guid? substitutionItemVariantId)
        : base(tenantId, storeId)
    {
        BasketId = basketId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        QuantityValue = quantityValue;
        QuantityUom = quantityUom;
        SubstitutionItemId = substitutionItemId;
        SubstitutionItemVariantId = substitutionItemVariantId;
    }

    private HamperBasketLine()
    {
    }

    /// <summary>The basket.</summary>
    public Guid BasketId { get; private set; }

    /// <summary>The item, when it has no variants. Exactly one of this and <see cref="ItemVariantId"/>.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much.</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>The unit the quantity is counted in.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>The substitute item, used only when the line item's available is zero at settle time.</summary>
    public Guid? SubstitutionItemId { get; private set; }

    /// <summary>The substitute variant.</summary>
    public Guid? SubstitutionItemVariantId { get; private set; }

    /// <summary>Freezes one basket line.</summary>
    public static HamperBasketLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid basketId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Guid? substitutionItemId = null,
        Guid? substitutionItemVariantId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A basket line must belong to a tenant.", nameof(tenantId));
        }

        if (basketId == Guid.Empty)
        {
            throw new ArgumentException("A basket line must belong to a basket.", nameof(basketId));
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw new StokvelExceptions(
                "STOKVEL_HAMPER_EXACTLY_ONE_ITEM_OR_VARIANT",
                "A basket line identifies exactly one of an item or a variant.");
        }

        if (quantity <= 0m)
        {
            throw new StokvelExceptions(
                "STOKVEL_HAMPER_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uom);

        if (substitutionItemId.HasValue && substitutionItemVariantId.HasValue)
        {
            throw new StokvelExceptions(
                "STOKVEL_HAMPER_SUBSTITUTION_EXACTLY_ONE",
                "A substitution identifies exactly one of an item or a variant.");
        }

        return new HamperBasketLine(
            tenantId, storeId, basketId, itemId, itemVariantId,
            quantity, uom.Trim(), substitutionItemId, substitutionItemVariantId);
    }
}
