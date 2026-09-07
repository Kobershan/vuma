namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// A single line of demand in a sourcing request: item/variant, quantity, unit price + currency.
/// Price is carried as a snapshot for split document reconciliation; no pricing happens here (Stage 10 owns that).
/// </summary>
public sealed class SourcingDemandLine
{
    private SourcingDemandLine() { }

    public SourcingDemandLine(
        StockItemReference itemReference,
        decimal quantity,
        decimal unitPrice,
        string currencyCode)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        if (currencyCode.Length != 3) throw new ArgumentException("Currency code must be 3 characters.", nameof(currencyCode));

        ItemReference = itemReference;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    /// <summary>
    /// Item and variant reference.
    /// </summary>
    public StockItemReference ItemReference { get; private set; } = null!;

    /// <summary>
    /// Quantity demanded (in the item's unit of measure).
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// Unit price at the time of demand (snapshot, not authoritative).
    /// Carried for split document line reconciliation; pricing logic belongs in Stage 10.
    /// </summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// Currency code (3 chars, ISO 4217).
    /// </summary>
    public string CurrencyCode { get; private set; } = null!;

    /// <summary>
    /// Extended amount: Quantity × UnitPrice.
    /// </summary>
    public decimal ExtendedAmount => Quantity * UnitPrice;
}
