namespace VumaRetail.Domain.Inventory;

/// <summary>
/// The "exactly one of an item or a variant" rule every inventory row that names a stock-keeping unit
/// shares — <see cref="StockLedgerEntry"/>, <see cref="StockBalance"/>, <see cref="StockTransfer"/> and
/// <see cref="StocktakeLine"/> all validate the same pair the same way.
/// </summary>
/// <remarks>
/// The same shape Stage 06's <c>Barcode</c> established: an item sells "as itself" (no variant) or "as
/// a variant", never both, so exactly one of the two ids is ever set. Centralised here rather than
/// repeated four times so the rule can only be wrong in one place.
/// </remarks>
internal static class StockItemReference
{
    /// <summary>Throws unless exactly one of <paramref name="itemId"/>/<paramref name="itemVariantId"/> is set.</summary>
    /// <param name="itemId">The item, or <c>null</c>.</param>
    /// <param name="itemVariantId">The variant, or <c>null</c>.</param>
    /// <exception cref="InventoryRuleException">Neither, or both, are set.</exception>
    public static void Validate(Guid? itemId, Guid? itemVariantId)
    {
        bool hasItem = itemId is not null && itemId != Guid.Empty;
        bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;

        if (hasItem == hasVariant)
        {
            throw InventoryRuleException.ExactlyOneItemOrVariantRequired();
        }
    }
}
