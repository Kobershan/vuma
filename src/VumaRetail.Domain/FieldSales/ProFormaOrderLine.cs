using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// One quoted line: the price list, promotion set, pack size and availability snapshot frozen at
/// capture, so the approver sees what the rep quoted beside what it costs today.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProFormaOrderLine : Entity
{
    private ProFormaOrderLine(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ProFormaOrderLine()
    {
    }

    /// <summary>The pro forma.</summary>
    public Guid ProFormaOrderId { get; private set; }

    /// <summary>The item, when the line is not a variant. Exactly one of the two is set.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, when the line is one. Exactly one of the two is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>Quantity value (<c>numeric(18,6)</c> in storage).</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>Unit of measure.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>Quoted unit price snapshot.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Quoted discount snapshot.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>Tax code resolved at capture.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>Quoted tax snapshot, from the tax rules engine.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Quoted net snapshot.</summary>
    public Money Net { get; private set; }

    /// <summary>Line gross — net plus tax.</summary>
    public Money Gross => Net + TaxAmount;

    /// <summary>Pack size snapshot (ADR-112).</summary>
    public string PackSizeDescription { get; private set; } = string.Empty;

    /// <summary>The price list the unit price came off, when one did.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>The promotions applied at capture, as human text.</summary>
    public string PromotionsSummary { get; private set; } = string.Empty;

    /// <summary>Group-wide available shown to the rep at capture. Indicative, never a promise.</summary>
    public Money AvailableAtCapture { get; private set; }

    /// <summary>When that availability was true. Shown on the document beside the figure.</summary>
    public DateTimeOffset AvailabilityAsAt { get; private set; }

    /// <summary>Captures a snapshotted line.</summary>
    public static ProFormaOrderLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid? companyId,
        Guid proFormaOrderId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money taxAmount,
        Money net,
        string packSizeDescription,
        Guid? priceListId,
        string promotionsSummary,
        Money availableAtCapture,
        DateTimeOffset availabilityAsAt,
        string currency)
    {
        if (tenantId == Guid.Empty || proFormaOrderId == Guid.Empty)
        {
            throw new ArgumentException("A line must belong to a tenant and a pro forma.");
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw new ArgumentException("A line names exactly one of item or variant.");
        }

        if (quantityValue <= 0m)
        {
            throw new ArgumentException("A line quantity must be positive.", nameof(quantityValue));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(quantityUom);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(packSizeDescription);

        foreach (Money amount in new[] { unitPrice, discountAmount, taxAmount, net, availableAtCapture })
        {
            if (!string.Equals(amount.Currency, currency, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Line amounts must be in {currency}.");
            }
        }

        if (unitPrice.IsNegative || discountAmount.IsNegative || taxAmount.IsNegative || net.IsNegative)
        {
            throw new ArgumentException("Line amounts cannot be negative.");
        }

        return new ProFormaOrderLine(tenantId, storeId)
        {
            CompanyId = companyId,
            ProFormaOrderId = proFormaOrderId,
            ItemId = itemId,
            ItemVariantId = itemVariantId,
            QuantityValue = quantityValue,
            QuantityUom = quantityUom.Trim(),
            UnitPrice = unitPrice,
            DiscountAmount = discountAmount,
            TaxCode = taxCode.Trim(),
            TaxAmount = taxAmount,
            Net = net,
            PackSizeDescription = packSizeDescription.Trim(),
            PriceListId = priceListId,
            PromotionsSummary = promotionsSummary ?? string.Empty,
            AvailableAtCapture = availableAtCapture,
            AvailabilityAsAt = availabilityAsAt,
        };
    }
}
