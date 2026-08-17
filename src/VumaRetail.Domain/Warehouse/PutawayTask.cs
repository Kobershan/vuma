using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// One line of unbinned received stock waiting to be shelved. Confirming it moves stock from unbinned
/// (dock) storage into a bin — a bin-level movement only; it never touches Stage 08's location-level
/// ledger, because the receipt that put the quantity on hand already did (business rule 2).
/// </summary>
/// <remarks>
/// May be confirmed in more than one call — a picker can split a task across two bins, or shelve part
/// of it now and the rest later. <see cref="ConfirmedQuantity"/> accumulates across confirmations;
/// <see cref="Status"/> becomes <see cref="PutawayStatus.Confirmed"/> only once the whole quantity is
/// accounted for.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class PutawayTask : Entity
{
    private PutawayTask(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        PutawaySourceReferenceType sourceReferenceType,
        Guid? sourceReferenceId,
        Guid? suggestedBinId)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        SourceReferenceType = sourceReferenceType;
        SourceReferenceId = sourceReferenceId;
        SuggestedBinId = suggestedBinId;
        Status = PutawayStatus.Pending;
        ConfirmedQuantity = Quantity.Zero(quantity.UnitOfMeasure);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PutawayTask()
    {
    }

    /// <summary>The location the stock is unbinned at.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item to shelve, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant to shelve.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The total quantity this task must account for.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What kind of document put this stock on the dock.</summary>
    public PutawaySourceReferenceType SourceReferenceType { get; private set; }

    /// <summary>The id of the receiving document, if any.</summary>
    public Guid? SourceReferenceId { get; private set; }

    /// <summary>The bin the allocator proposes. Advisory — the confirmed bin need not match.</summary>
    public Guid? SuggestedBinId { get; private set; }

    /// <summary>Where this task stands.</summary>
    public PutawayStatus Status { get; private set; }

    /// <summary>The bin the last confirmation shelved stock into.</summary>
    public Guid? ConfirmedBinId { get; private set; }

    /// <summary>How much of <see cref="Quantity"/> has been shelved so far, across every confirmation.</summary>
    public Quantity ConfirmedQuantity { get; private set; }

    /// <summary>When this task was last confirmed, or <c>null</c> if it never has been.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>What remains to be shelved.</summary>
    public Quantity Remaining => Quantity - ConfirmedQuantity;

    /// <summary>Creates a new putaway task.</summary>
    public static PutawayTask Create(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        PutawaySourceReferenceType sourceReferenceType,
        Guid? sourceReferenceId,
        Guid? suggestedBinId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A putaway task must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A putaway task must name a location.", nameof(locationId));
        }

        WarehouseItemReference.Validate(itemId, itemVariantId);

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        return new PutawayTask(
            tenantId, storeId, locationId, itemId, itemVariantId, quantity, sourceReferenceType,
            sourceReferenceId, suggestedBinId);
    }

    /// <summary>Records the allocator's suggested bin.</summary>
    public void SuggestBin(Guid binId)
    {
        if (binId == Guid.Empty)
        {
            throw new ArgumentException("A suggested bin must be a real bin.", nameof(binId));
        }

        SuggestedBinId = binId;
    }

    /// <summary>
    /// Confirms that some or all of this task's remaining quantity was shelved into a bin.
    /// </summary>
    /// <param name="binId">The bin the stock was actually shelved into.</param>
    /// <param name="quantity">How much was shelved. Must be positive and no more than <see cref="Remaining"/>.</param>
    /// <param name="at">The instant of confirmation, UTC.</param>
    /// <exception cref="WarehouseRuleException">The task is not pending, or the quantity exceeds what remains.</exception>
    public void Confirm(Guid binId, Quantity quantity, DateTimeOffset at)
    {
        if (binId == Guid.Empty)
        {
            throw new ArgumentException("A confirmed bin must be a real bin.", nameof(binId));
        }

        if (Status != PutawayStatus.Pending)
        {
            throw WarehouseRuleException.PutawayNotPending();
        }

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        if (quantity > Remaining)
        {
            throw WarehouseRuleException.PutawayExceedsRemaining(Remaining, quantity);
        }

        ConfirmedBinId = binId;
        ConfirmedQuantity += quantity;
        ConfirmedAt = at;

        if (ConfirmedQuantity == Quantity)
        {
            Status = PutawayStatus.Confirmed;
        }
    }

    /// <summary>Abandons the task's remaining quantity.</summary>
    /// <exception cref="WarehouseRuleException">The task is already fully confirmed or cancelled.</exception>
    public void Cancel()
    {
        if (Status != PutawayStatus.Pending)
        {
            throw WarehouseRuleException.PutawayNotPending();
        }

        Status = PutawayStatus.Cancelled;
    }
}
