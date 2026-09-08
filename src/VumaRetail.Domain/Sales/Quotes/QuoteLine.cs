using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Quotes;

/// <summary>
/// One line on a quote: the price resolution frozen at creation (ADR-074) with the pack size
/// snapshot beside it (ADR-112). Immutable once created — a changed offer is a new line.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class QuoteLine : Entity
{
    private QuoteLine(
        Guid tenantId,
        Guid? storeId,
        Guid quoteId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money unitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId,
        string promotionsSummary)
        : base(tenantId, storeId)
    {
        QuoteId = quoteId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        QuantityValue = quantity;
        QuantityUom = uom;
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        TaxAmount = taxAmount;
        PackSizeDescription = packSizeDescription;
        Currency = currency;
        PriceListId = priceListId;
        PromotionsSummary = promotionsSummary;
        Net = unitPrice * quantity - discountAmount;
    }

    private QuoteLine()
    {
    }

    /// <summary>The quote this line belongs to.</summary>
    public Guid QuoteId { get; private set; }

    /// <summary>The item, when it has no variants. Exactly one of this and <see cref="ItemVariantId"/>.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant. Exactly one of this and <see cref="ItemId"/>.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was quoted.</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>The unit the quantity is counted in.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>The snapshotted unit price, at full precision. Never re-resolved.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>The snapshotted whole-line discount the promotions took off.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The snapshotted tax (ADR-075). Never recomputed.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Unit price times quantity less discount.</summary>
    public Money Net { get; private set; }

    /// <summary>The snapshotted pack size as the customer reads it, e.g. <c>2 x Case of 12</c> (ADR-112).</summary>
    public string PackSizeDescription { get; private set; } = string.Empty;

    /// <summary>The ISO 4217 currency. Always the quote's.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The price list the snapshot was resolved against, for explainability.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>Which promotions fired, in words a cashier can read out.</summary>
    public string PromotionsSummary { get; private set; } = string.Empty;

    /// <summary>Snapshots one priced line onto a draft quote.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="quoteId">The quote.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="uom">The unit the quantity is counted in.</param>
    /// <param name="unitPrice">The resolved unit price.</param>
    /// <param name="discountAmount">The resolved whole-line discount.</param>
    /// <param name="taxAmount">The computed tax.</param>
    /// <param name="packSizeDescription">The resolved pack size, e.g. <c>2 x Case of 12</c>.</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    /// <param name="priceListId">The price list resolved against.</param>
    /// <param name="promotionsSummary">Which promotions fired.</param>
    public static QuoteLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid quoteId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money unitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId,
        string promotionsSummary)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A quote line must belong to a tenant.", nameof(tenantId));
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw QuotesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (quantity <= 0m)
        {
            throw QuotesRuleException.QuantityMustBePositive();
        }

        return new QuoteLine(
            tenantId, storeId, quoteId, itemId, itemVariantId,
            quantity, uom, unitPrice, discountAmount, taxAmount,
            packSizeDescription, currency, priceListId, promotionsSummary);
    }
}
