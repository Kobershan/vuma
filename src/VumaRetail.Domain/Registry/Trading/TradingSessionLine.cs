using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry.Trading;

/// <summary>
/// One scanned line on a trading-session segment. Priced snapshots, stored verbatim (ADR-138):
/// a document reprinted later shows what was actually captured, whatever the shelf price did.
/// </summary>
public sealed class TradingSessionLine
{
    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    public TradingSessionLine()
    {
    }

    /// <summary>The line's identity, minted by the caller (offline replay finds it, §4.11).</summary>
    public Guid Id { get; private set; }

    /// <summary>The owning tenant.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The session.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>The segment (one per company).</summary>
    public Guid SegmentId { get; private set; }

    /// <summary>The resolved owning company, denormalised for the completion legs.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>The scanned barcode.</summary>
    public string Barcode { get; private set; } = string.Empty;

    /// <summary>The item, when the line is not a variant. Exactly one of the two is set.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, when the line is one. Exactly one of the two is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>Description snapshot taken at scan.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Quantity value (<c>numeric(18,6)</c> in storage).</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>Unit of measure.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>Snapshotted unit price.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Snapshotted discount.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>Tax code resolved at scan.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>Snapshotted tax, from the tax rules engine.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Snapshotted net.</summary>
    public Money Net { get; private set; }

    /// <summary>Line gross — net plus tax, rounded once at capture.</summary>
    public Money Gross => Net + TaxAmount;

    /// <summary>Pack size snapshot (ADR-112).</summary>
    public string PackSizeDescription { get; private set; } = string.Empty;

    /// <summary>The price list the unit price came from, when one did.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>True once voided. Voided lines contribute nothing.</summary>
    public bool IsVoided { get; private set; }

    /// <summary>When the line was added, UTC.</summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>When the line was voided, UTC. Null unless voided.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>Captures a scanned line with its priced snapshots.</summary>
    public static TradingSessionLine Create(
        Guid lineId,
        Guid tenantId,
        Guid sessionId,
        Guid segmentId,
        Guid companyId,
        string barcode,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money taxAmount,
        Money net,
        string packSizeDescription,
        Guid? priceListId,
        string currency,
        DateTimeOffset addedAt)
    {
        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("A line must have an identity minted by its caller.", nameof(lineId));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A line must belong to a tenant.", nameof(tenantId));
        }

        if (sessionId == Guid.Empty || segmentId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("A line must belong to a session, a segment and a company.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantityUom);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(packSizeDescription);

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw new ArgumentException("A line names exactly one of item or variant.");
        }

        if (quantityValue <= 0m)
        {
            throw new ArgumentException("A line quantity must be positive.", nameof(quantityValue));
        }

        if (unitPrice.IsNegative || discountAmount.IsNegative || taxAmount.IsNegative || net.IsNegative)
        {
            throw new ArgumentException("Line amounts cannot be negative.");
        }

        foreach (Money amount in new[] { unitPrice, discountAmount, taxAmount, net })
        {
            if (!string.Equals(amount.Currency, currency, StringComparison.Ordinal))
            {
                throw TradingSessionException.CurrencyMismatch(currency, amount.Currency);
            }
        }

        if (unitPrice * quantityValue < discountAmount)
        {
            throw new ArgumentException("A discount cannot exceed the extended price.");
        }

        return new TradingSessionLine
        {
            Id = lineId,
            TenantId = tenantId,
            SessionId = sessionId,
            SegmentId = segmentId,
            CompanyId = companyId,
            Barcode = barcode.Trim(),
            ItemId = itemId,
            ItemVariantId = itemVariantId,
            Description = description.Trim(),
            QuantityValue = quantityValue,
            QuantityUom = quantityUom.Trim(),
            UnitPrice = unitPrice,
            DiscountAmount = discountAmount,
            TaxCode = taxCode.Trim(),
            TaxAmount = taxAmount,
            Net = net,
            PackSizeDescription = packSizeDescription.Trim(),
            PriceListId = priceListId,
            AddedAt = addedAt,
        };
    }

    /// <summary>Voids the line. It stays for audit; it contributes nothing.</summary>
    public void Void(DateTimeOffset voidedAt)
    {
        if (IsVoided)
        {
            throw new TradingSessionException(
                "TRADING_LINE_ALREADY_VOIDED", $"Line {Id} is already voided.");
        }

        IsVoided = true;
        VoidedAt = voidedAt;
    }
}
