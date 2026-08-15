using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// One counted item/variant within a <see cref="StocktakeSession"/>: the system quantity at the moment
/// it was counted, against what was physically found.
/// </summary>
/// <remarks>
/// <see cref="SystemQuantity"/> is a snapshot taken when the count is recorded, not read live when the
/// session is finalized — stock keeps moving while a count is in progress, and comparing a count taken
/// at 09:00 against a system quantity read at 17:00 would blame the stocktake for every sale in
/// between. The variance this line reports is always "what we counted minus what the system said at
/// the moment we counted it".
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StocktakeLine : Entity
{
    private StocktakeLine(
        Guid tenantId,
        Guid? storeId,
        Guid stocktakeSessionId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity systemQuantity,
        Quantity countedQuantity)
        : base(tenantId, storeId)
    {
        StocktakeSessionId = stocktakeSessionId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StocktakeLine()
    {
    }

    /// <summary>The session this line belongs to.</summary>
    public Guid StocktakeSessionId { get; private set; }

    /// <summary>The item counted, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant counted.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What <see cref="StockBalance.QuantityOnHand"/> reported at the moment this line was recorded.</summary>
    public Quantity SystemQuantity { get; private set; }

    /// <summary>What was physically counted.</summary>
    public Quantity CountedQuantity { get; private set; }

    /// <summary>The signed difference — counted minus system. What finalizing posts to the ledger.</summary>
    public Quantity Variance => CountedQuantity - SystemQuantity;

    /// <summary>Records a fresh count for one item/variant within a session.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store — the session's own.</param>
    /// <param name="stocktakeSessionId">The session this line belongs to.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="systemQuantity">The system quantity at the moment of counting.</param>
    /// <param name="countedQuantity">What was physically found.</param>
    public static StocktakeLine Record(
        Guid tenantId,
        Guid? storeId,
        Guid stocktakeSessionId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity systemQuantity,
        Quantity countedQuantity)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stocktake line must belong to a tenant.", nameof(tenantId));
        }

        if (stocktakeSessionId == Guid.Empty)
        {
            throw new ArgumentException("A stocktake line must belong to a session.", nameof(stocktakeSessionId));
        }

        StockItemReference.Validate(itemId, itemVariantId);

        if (countedQuantity.IsNegative)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        return new StocktakeLine(tenantId, storeId, stocktakeSessionId, itemId, itemVariantId, systemQuantity, countedQuantity);
    }

    /// <summary>Replaces this line's count with a fresh one — a recount before the session is finalized.</summary>
    /// <param name="systemQuantity">The system quantity at the moment of this recount.</param>
    /// <param name="countedQuantity">What was physically found this time.</param>
    public void Recount(Quantity systemQuantity, Quantity countedQuantity)
    {
        if (countedQuantity.IsNegative)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
    }
}
