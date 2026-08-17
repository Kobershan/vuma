using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// One counted bin/stock-keeping-unit pair within a <see cref="CycleCount"/>. <see cref="SystemQuantity"/>
/// is snapshotted when the line is opened, not read live at finalize — the same reasoning Stage 08's
/// <c>StocktakeLine</c> gives.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class CycleCountLine : Entity
{
    private CycleCountLine(
        Guid tenantId,
        Guid? storeId,
        Guid cycleCountId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity systemQuantity,
        Quantity countedQuantity)
        : base(tenantId, storeId)
    {
        CycleCountId = cycleCountId;
        BinId = binId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private CycleCountLine()
    {
    }

    /// <summary>The count this line belongs to.</summary>
    public Guid CycleCountId { get; private set; }

    /// <summary>The bin counted.</summary>
    public Guid BinId { get; private set; }

    /// <summary>The item counted, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant counted.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What <see cref="BinStock.QuantityOnHand"/> reported at the moment this line was recorded.</summary>
    public Quantity SystemQuantity { get; private set; }

    /// <summary>What was physically counted.</summary>
    public Quantity CountedQuantity { get; private set; }

    /// <summary>The signed difference — counted minus system. What finalizing posts.</summary>
    public Quantity Variance => CountedQuantity - SystemQuantity;

    /// <summary>Records a fresh count for one bin/stock-keeping-unit pair.</summary>
    public static CycleCountLine Record(
        Guid tenantId,
        Guid? storeId,
        Guid cycleCountId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity systemQuantity,
        Quantity countedQuantity)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A cycle count line must belong to a tenant.", nameof(tenantId));
        }

        if (cycleCountId == Guid.Empty)
        {
            throw new ArgumentException("A cycle count line must belong to a count.", nameof(cycleCountId));
        }

        if (binId == Guid.Empty)
        {
            throw new ArgumentException("A cycle count line must name a bin.", nameof(binId));
        }

        WarehouseItemReference.Validate(itemId, itemVariantId);

        if (countedQuantity.IsNegative)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        return new CycleCountLine(tenantId, storeId, cycleCountId, binId, itemId, itemVariantId, systemQuantity, countedQuantity);
    }

    /// <summary>Replaces this line's count with a fresh one — a recount before the count is finalized.</summary>
    public void Recount(Quantity systemQuantity, Quantity countedQuantity)
    {
        if (countedQuantity.IsNegative)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
    }
}
