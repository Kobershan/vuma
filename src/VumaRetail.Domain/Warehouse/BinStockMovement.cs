using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// One immutable, append-only record of stock moving into, out of, or between bins — the bin-level
/// mirror of Stage 08's <see cref="Inventory.StockLedgerEntry"/>, at one level of granularity down.
/// </summary>
/// <remarks>
/// <para>
/// Posted for internal bin reassignments that do not change what a location holds — a putaway shelving
/// unbinned stock, a pick reserving stock out of a bin, an internal bin-to-bin move. A movement that
/// <em>does</em> change what a location holds (a shipment leaving, a cycle count variance) is posted
/// through Stage 08's <c>IStockLedgerPoster</c> instead, with a <c>BinId</c> attached — see
/// <c>docs/stages/STAGE-13-warehouse-management.md</c> business rules 2–4.
/// </para>
/// <para>
/// Marked <see cref="IImmutableRecord"/> for the same reason <c>StockLedgerEntry</c> is: the append-only
/// guarantee is structural, not conventional. A mistaken movement is corrected by a compensating one.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class BinStockMovement : Entity, IImmutableRecord
{
    private BinStockMovement(
        Guid tenantId,
        Guid? storeId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        BinStockMovementType movementType,
        Quantity quantity,
        BinStockReferenceType referenceType,
        Guid referenceId,
        string? note)
        : base(tenantId, storeId)
    {
        BinId = binId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        MovementType = movementType;
        Quantity = quantity;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Note = note;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private BinStockMovement()
    {
    }

    /// <summary>The bin this movement affects.</summary>
    public Guid BinId { get; private set; }

    /// <summary>The item this movement affects, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this movement affects.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What kind of movement this is.</summary>
    public BinStockMovementType MovementType { get; private set; }

    /// <summary>The signed change to the bin's on-hand quantity.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What kind of document this movement correlates to.</summary>
    public BinStockReferenceType ReferenceType { get; private set; }

    /// <summary>The id of the task or transfer this movement belongs to.</summary>
    public Guid ReferenceId { get; private set; }

    /// <summary>An optional free-text note.</summary>
    public string? Note { get; private set; }

    /// <summary>Posts a new bin-stock movement.</summary>
    /// <exception cref="WarehouseRuleException">A structural invariant was broken.</exception>
    public static BinStockMovement Post(
        Guid tenantId,
        Guid? storeId,
        Guid binId,
        Guid? itemId,
        Guid? itemVariantId,
        BinStockMovementType movementType,
        Quantity quantity,
        BinStockReferenceType referenceType,
        Guid referenceId,
        string? note = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A bin-stock movement must belong to a tenant.", nameof(tenantId));
        }

        if (binId == Guid.Empty)
        {
            throw new ArgumentException("A bin-stock movement must name a bin.", nameof(binId));
        }

        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("A bin-stock movement must correlate to a task or transfer.", nameof(referenceId));
        }

        WarehouseItemReference.Validate(itemId, itemVariantId);

        if (quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        return new BinStockMovement(
            tenantId, storeId, binId, itemId, itemVariantId, movementType, quantity, referenceType, referenceId,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }
}
