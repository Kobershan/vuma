namespace VumaRetail.Contracts.Inventory.Sourcing;

public sealed class SourcingDemandDto
{
    public SourcingDemandDto(
        string itemCode,
        string? variantCode,
        decimal quantity,
        decimal unitPrice,
        string currencyCode)
    {
        ItemCode = itemCode ?? throw new ArgumentNullException(nameof(itemCode));
        VariantCode = variantCode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CurrencyCode = currencyCode ?? throw new ArgumentNullException(nameof(currencyCode));
    }

    public string ItemCode { get; }
    public string? VariantCode { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public string CurrencyCode { get; }
}
