using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Invoices;

/// <summary>
/// One line on an invoice: what was sold, at what snapshotted price and tax, in what packaging
/// (ADR-112). Immutable once created — a reprint a year later shows what was actually sold.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class InvoiceLine : Entity
{
    private InvoiceLine(
        Guid tenantId,
        Guid? storeId,
        Guid invoiceId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money unitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId)
        : base(tenantId, storeId)
    {
        InvoiceId = invoiceId;
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
        Net = unitPrice * quantity - discountAmount;
    }

    private InvoiceLine()
    {
    }

    /// <summary>The invoice this line belongs to.</summary>
    public Guid InvoiceId { get; private set; }

    /// <summary>The item, when it has no variants. Exactly one of this and <see cref="ItemVariantId"/>.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant. Exactly one of this and <see cref="ItemId"/>.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was sold.</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>The unit the quantity is counted in.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>The snapshotted unit price, at full precision. Never re-resolved.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>The snapshotted whole-line discount.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The snapshotted tax (ADR-075). Never recomputed.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Unit price times quantity less discount.</summary>
    public Money Net { get; private set; }

    /// <summary>The snapshotted pack size as the customer reads it, e.g. <c>6 x Case of 10</c> (ADR-112).</summary>
    public string PackSizeDescription { get; private set; } = string.Empty;

    /// <summary>The ISO 4217 currency. Always the invoice's.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The price list the snapshot was resolved against, for explainability.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>Freezes one sold line onto a draft invoice.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="invoiceId">The invoice.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="uom">The unit the quantity is counted in.</param>
    /// <param name="unitPrice">The snapshotted unit price.</param>
    /// <param name="discountAmount">The snapshotted whole-line discount.</param>
    /// <param name="taxAmount">The computed tax.</param>
    /// <param name="packSizeDescription">The resolved pack size. Required — an invoice without pack sizes is not a legal document for wholesale (ADR-112).</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    /// <param name="priceListId">The price list resolved against.</param>
    public static InvoiceLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid invoiceId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money unitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invoice line must belong to a tenant.", nameof(tenantId));
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw InvoicesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (quantity <= 0m)
        {
            throw InvoicesRuleException.QuantityMustBePositive();
        }

        if (string.IsNullOrWhiteSpace(packSizeDescription))
        {
            throw InvoicesRuleException.PackSizeNotResolved();
        }

        return new InvoiceLine(
            tenantId, storeId, invoiceId, itemId, itemVariantId,
            quantity, uom, unitPrice, discountAmount, taxAmount,
            packSizeDescription, currency, priceListId);
    }
}
