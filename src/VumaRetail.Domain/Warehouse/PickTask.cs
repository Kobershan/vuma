using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// One line of demand within a <see cref="PickWave"/>: an item/variant, a requested quantity, the
/// outbound reference that raised the demand, and the bin it is allocated to and picked from.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class PickTask : Entity
{
    private PickTask(
        Guid tenantId,
        Guid? storeId,
        Guid pickWaveId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity requestedQuantity,
        string outboundReference)
        : base(tenantId, storeId)
    {
        PickWaveId = pickWaveId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        RequestedQuantity = requestedQuantity;
        OutboundReference = outboundReference;
        Status = PickTaskStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PickTask()
    {
    }

    /// <summary>The wave this line belongs to.</summary>
    public Guid PickWaveId { get; private set; }

    /// <summary>The item requested, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant requested.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was requested.</summary>
    public Quantity RequestedQuantity { get; private set; }

    /// <summary>
    /// Free text identifying what raised this demand — an order number, a transfer request. Order
    /// Management (Stage 14) does not exist yet; this becomes a real sales order id once it does,
    /// with no shape change.
    /// </summary>
    public string OutboundReference { get; private set; } = string.Empty;

    /// <summary>The bin this line is allocated to, once allocated.</summary>
    public Guid? AllocatedBinId { get; private set; }

    /// <summary>
    /// <see cref="AllocatedQuantity"/>'s numeric value, mapped directly as a plain nullable column for
    /// the reason <c>Bin.CapacityValue</c> gives (ADR-067, EF Core 9 cannot configure a complex property
    /// as optional).
    /// </summary>
    public decimal? AllocatedQuantityValue { get; private set; }

    /// <summary>How much was allocated, once allocated.</summary>
    public Quantity? AllocatedQuantity
        => AllocatedQuantityValue is { } value ? new Quantity(value, RequestedQuantity.UnitOfMeasure) : null;

    /// <summary><see cref="PickedQuantity"/>'s numeric value. See <see cref="AllocatedQuantityValue"/>.</summary>
    public decimal? PickedQuantityValue { get; private set; }

    /// <summary>How much was actually confirmed picked, once picked.</summary>
    public Quantity? PickedQuantity
        => PickedQuantityValue is { } value ? new Quantity(value, RequestedQuantity.UnitOfMeasure) : null;

    /// <summary>Where this line stands.</summary>
    public PickTaskStatus Status { get; private set; }

    /// <summary>Creates a new demand line.</summary>
    public static PickTask Create(
        Guid tenantId,
        Guid? storeId,
        Guid pickWaveId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity requestedQuantity,
        string outboundReference)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A pick task must belong to a tenant.", nameof(tenantId));
        }

        if (pickWaveId == Guid.Empty)
        {
            throw new ArgumentException("A pick task must belong to a wave.", nameof(pickWaveId));
        }

        WarehouseItemReference.Validate(itemId, itemVariantId);

        if (requestedQuantity.IsNegative || requestedQuantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        if (string.IsNullOrWhiteSpace(outboundReference))
        {
            throw new ArgumentException("A pick task must name what raised its demand.", nameof(outboundReference));
        }

        return new PickTask(tenantId, storeId, pickWaveId, itemId, itemVariantId, requestedQuantity, outboundReference.Trim());
    }

    /// <summary>Allocates this line to a bin.</summary>
    /// <exception cref="WarehouseRuleException">The line is not <see cref="PickTaskStatus.Pending"/>.</exception>
    public void Allocate(Guid binId, Quantity quantity)
    {
        if (Status != PickTaskStatus.Pending)
        {
            throw WarehouseRuleException.PickTaskWrongStatus(PickTaskStatus.Pending, Status);
        }

        if (binId == Guid.Empty)
        {
            throw new ArgumentException("An allocation must name a real bin.", nameof(binId));
        }

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        AllocatedBinId = binId;
        AllocatedQuantityValue = quantity.Value;
        Status = PickTaskStatus.Allocated;
    }

    /// <summary>
    /// Confirms a pick against this line's allocation. A quantity below what was allocated is a short
    /// pick — recorded, not refused (business rule 6).
    /// </summary>
    /// <exception cref="WarehouseRuleException">The line is not <see cref="PickTaskStatus.Allocated"/>, or the quantity exceeds the allocation.</exception>
    public void ConfirmPick(Quantity quantity)
    {
        if (Status != PickTaskStatus.Allocated || AllocatedQuantity is not { } allocated)
        {
            throw WarehouseRuleException.PickTaskWrongStatus(PickTaskStatus.Allocated, Status);
        }

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        if (quantity > allocated)
        {
            throw WarehouseRuleException.PickExceedsAllocated(allocated, quantity);
        }

        PickedQuantityValue = quantity.Value;
        Status = quantity == allocated ? PickTaskStatus.Picked : PickTaskStatus.ShortPicked;
    }

    /// <summary>Abandons this line before it is picked.</summary>
    public void Cancel()
    {
        if (Status is PickTaskStatus.Picked or PickTaskStatus.ShortPicked or PickTaskStatus.Cancelled)
        {
            throw WarehouseRuleException.PickTaskWrongStatus(PickTaskStatus.Pending, Status);
        }

        Status = PickTaskStatus.Cancelled;
    }
}
