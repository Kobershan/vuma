namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// The "exactly one of an item or a variant" rule every warehouse row that names a stock-keeping unit
/// shares — the same shape Stage 08's <c>StockItemReference</c> enforces for the location-level ledger,
/// kept as its own copy here so a warehouse violation raises a <see cref="WarehouseRuleException"/>
/// rather than an inventory one.
/// </summary>
internal static class WarehouseItemReference
{
    /// <summary>Throws unless exactly one of <paramref name="itemId"/>/<paramref name="itemVariantId"/> is set.</summary>
    /// <exception cref="WarehouseRuleException">Neither, or both, are set.</exception>
    public static void Validate(Guid? itemId, Guid? itemVariantId)
    {
        bool hasItem = itemId is not null && itemId != Guid.Empty;
        bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;

        if (hasItem == hasVariant)
        {
            throw WarehouseRuleException.ExactlyOneItemOrVariantRequired();
        }
    }
}
