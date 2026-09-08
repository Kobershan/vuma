using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// One line on a lay-by agreement: the price resolution frozen at creation (ADR-074) with the pack size
/// snapshot beside it (ADR-112). Immutable once created — a changed offer is a new line.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class LayByAgreementLine : Entity
{
    private LayByAgreementLine(
        Guid tenantId,
        Guid? storeId,
        Guid agreementId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money agreedUnitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId)
        : base(tenantId, storeId)
    {
        AgreementId = agreementId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        QuantityValue = quantity;
        QuantityUom = uom;
        AgreedUnitPrice = agreedUnitPrice;
        DiscountAmount = discountAmount;
        TaxAmount = taxAmount;
        PackSizeDescription = packSizeDescription;
        Currency = currency;
        PriceListId = priceListId;
        Net = agreedUnitPrice * quantity - discountAmount;
    }

    private LayByAgreementLine()
    {
    }

    /// <summary>The agreement this line belongs to.</summary>
    public Guid AgreementId { get; private set; }

    /// <summary>The item, when it has no variants. Exactly one of this and <see cref="ItemVariantId"/>.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant. Exactly one of this and <see cref="ItemId"/>.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was laid by.</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>The unit the quantity is counted in.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>The frozen unit price. The shelf may move; this does not.</summary>
    public Money AgreedUnitPrice { get; private set; }

    /// <summary>The frozen whole-line discount.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The frozen tax (ADR-075). Never recomputed.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Unit price times quantity less discount.</summary>
    public Money Net { get; private set; }

    /// <summary>The frozen pack size as the customer reads it (ADR-112).</summary>
    public string PackSizeDescription { get; private set; } = string.Empty;

    /// <summary>The ISO 4217 currency. Always the agreement's.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The price list resolved against, for explainability.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>Snapshots one priced line onto a draft agreement.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="agreementId">The agreement.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="uom">The unit the quantity is counted in.</param>
    /// <param name="agreedUnitPrice">The resolved unit price.</param>
    /// <param name="discountAmount">The resolved whole-line discount.</param>
    /// <param name="taxAmount">The computed tax.</param>
    /// <param name="packSizeDescription">The resolved pack size.</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    /// <param name="priceListId">The price list resolved against.</param>
    public static LayByAgreementLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid agreementId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        Money agreedUnitPrice,
        Money discountAmount,
        Money taxAmount,
        string packSizeDescription,
        string currency,
        Guid? priceListId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A lay-by line must belong to a tenant.", nameof(tenantId));
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw LayByExceptions.ExactlyOneItemOrVariantRequired();
        }

        if (quantity <= 0m)
        {
            throw LayByExceptions.QuantityMustBePositive();
        }

        if (string.IsNullOrWhiteSpace(packSizeDescription))
        {
            throw LayByExceptions.PackSizeNotResolved();
        }

        return new LayByAgreementLine(
            tenantId, storeId, agreementId, itemId, itemVariantId,
            quantity, uom, agreedUnitPrice, discountAmount, taxAmount,
            packSizeDescription, currency, priceListId);
    }
}
