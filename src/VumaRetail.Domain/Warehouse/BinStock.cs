using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// On-hand quantity for one stock-keeping unit in one bin — the projection <see cref="BinStockMovement"/>
/// and bin-tagged <see cref="Inventory.StockLedgerEntry"/> rows sum to, the bin-level mirror of Stage
/// 08's <c>StockBalance</c>.
/// </summary>
/// <remarks>
/// Quantity only — no cost. Valuation stays at the location level in Stage 08's <c>StockBalance</c>;
/// moving stock between bins within the same location never changes what it is worth. Node-local, not
/// replicated, for the same reason ADR-069 gives <c>StockBalance</c>: a running total is not safely
/// mergeable across nodes the way an accumulating ledger is.
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class BinStock : Entity
{
    private BinStock(
        Guid tenantId,
        Guid? storeId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantityOnHand,
        Quantity quantityReserved)
        : base(tenantId, storeId)
    {
        BinId = binId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        QuantityOnHand = quantityOnHand;
        QuantityReserved = quantityReserved;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private BinStock()
    {
    }

    /// <summary>The bin this balance is held at.</summary>
    public Guid BinId { get; private set; }

    /// <summary>The item this balance is for, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this balance is for.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What is currently held in this bin.</summary>
    public Quantity QuantityOnHand { get; private set; }

    /// <summary>
    /// How much of <see cref="QuantityOnHand"/> is spoken for by a released pick wave that has not yet
    /// been confirmed or abandoned. Never reduces on-hand itself (§7 rule 21).
    /// </summary>
    public Quantity QuantityReserved { get; private set; }

    /// <summary>
    /// What the allocator may still promise from this bin — <see cref="QuantityOnHand"/> less
    /// <see cref="QuantityReserved"/>. The figure business rule 21 requires: never negative under any
    /// interleaving of concurrent releases.
    /// </summary>
    public Quantity Available => QuantityOnHand - QuantityReserved;

    /// <summary>Opens a new bin balance at zero, in the unit of measure its first movement establishes.</summary>
    public static BinStock Open(
        Guid tenantId,
        Guid? storeId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A bin balance must belong to a tenant.", nameof(tenantId));
        }

        if (binId == Guid.Empty)
        {
            throw new ArgumentException("A bin balance must name a bin.", nameof(binId));
        }

        WarehouseItemReference.Validate(itemId, itemVariantId);

        return new BinStock(
            tenantId, storeId, binId, itemId, itemVariantId, Quantity.Zero(unitOfMeasure), Quantity.Zero(unitOfMeasure));
    }

    /// <summary>Increases on-hand quantity — a putaway, an internal transfer in, a released pick reservation.</summary>
    /// <exception cref="WarehouseRuleException">The quantity is not positive, or the unit of measure does not match.</exception>
    public void ApplyIn(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);

        QuantityOnHand += quantity;
    }

    /// <summary>Decreases on-hand quantity — a pick reservation, an internal transfer out.</summary>
    /// <exception cref="WarehouseRuleException">
    /// The quantity is not positive, the unit of measure does not match, or it would take the bin below zero.
    /// </exception>
    public void ApplyOut(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);

        if (quantity > QuantityOnHand)
        {
            throw WarehouseRuleException.InsufficientBinStock(QuantityOnHand, quantity);
        }

        QuantityOnHand -= quantity;
    }

    /// <summary>
    /// Sets aside quantity for a released pick allocation. Physical stock does not move — only
    /// <see cref="Available"/> falls — so a second wave released against the same bin sees what is
    /// genuinely still promisable rather than the raw on-hand figure (§7 rule 21, ADR-090 extended).
    /// </summary>
    /// <exception cref="WarehouseRuleException">
    /// The quantity is not positive, the unit of measure does not match, or it would take
    /// <see cref="Available"/> below zero.
    /// </exception>
    public void Reserve(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);

        if (quantity > Available)
        {
            throw WarehouseRuleException.InsufficientBinStock(Available, quantity);
        }

        QuantityReserved += quantity;
    }

    /// <summary>
    /// Gives back a reservation — a pick was confirmed (in full or short), or its task or wave was
    /// cancelled before confirmation. On-hand is untouched; only what the allocator may promise again
    /// changes.
    /// </summary>
    /// <exception cref="WarehouseRuleException">The quantity is not positive or the unit of measure does not match.</exception>
    public void ReleaseReservation(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);

        // Clamped, not thrown: a caller releasing a reservation it holds the record of (an allocated
        // task's own AllocatedQuantity) must never fail because of an earlier rounding artifact — the
        // task record is the source of truth for what it reserved, this balance only mirrors it.
        QuantityReserved = quantity >= QuantityReserved ? Quantity.Zero(quantity.UnitOfMeasure) : QuantityReserved - quantity;
    }

    private void EnsureSameUnitOfMeasure(string unitOfMeasure)
    {
        if (!string.Equals(QuantityOnHand.UnitOfMeasure, unitOfMeasure, StringComparison.Ordinal))
        {
            throw WarehouseRuleException.UnitOfMeasureMismatch(QuantityOnHand.UnitOfMeasure, unitOfMeasure);
        }
    }
}
