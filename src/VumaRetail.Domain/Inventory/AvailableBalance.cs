using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// The reserved / staging / incoming position for one stock-keeping unit at one location — the
/// half of available-to-promise the ledger does not already answer.
/// </summary>
/// <remarks>
/// <para>
/// ADR-005 names <c>inventory.stock_balance</c> the projection the ledger sums to; this is its
/// sibling for the reservation half. <c>OnHand</c> is deliberately NOT stored here: it already
/// has a projection (<see cref="StockBalance"/>, maintained by <c>IStockLedgerPoster</c>), and
/// a second copy would eventually disagree with it. <c>Available</c> is therefore always
/// computed at read time as <c>StockBalance − this row's Reserved − this row's InStaging</c>,
/// so there is no column anywhere that can go stale between a receipt and a hold.
/// </para>
/// <para>
/// Mutable, like <see cref="StockBalance"/> and unlike the ledgers it summarises: it is
/// maintained transactionally alongside every reservation row, never edited independently of
/// one. <see cref="ReplicationScope.NodeLocal"/> for the same reason as
/// <see cref="StockBalance"/> — a running total is not safely mergeable.
/// </para>
/// <para>
/// Rebuilt from reservations (plus staging/incoming feeds) by summing every live <c>Held</c>
/// <see cref="StockReservation"/> row per location and stock-keeping unit; the rebuild must
/// equal the incremental projection row for row (stage acceptance criterion 7).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class AvailableBalance : Entity
{
    private AvailableBalance(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity reserved,
        Quantity inStaging,
        Quantity incoming)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Reserved = reserved;
        InStaging = inStaging;
        Incoming = incoming;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private AvailableBalance()
    {
    }

    /// <summary>The location this position belongs to.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item, or <c>null</c> when this row is for a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> when this row is for an item directly.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What live holds currently speak for. Never negative.</summary>
    public Quantity Reserved { get; private set; } = Quantity.Zero("EA");

    /// <summary>What sits in staging bins — on hand, not available. Never negative.</summary>
    public Quantity InStaging { get; private set; } = Quantity.Zero("EA");

    /// <summary>Open inbound supply, informational only. Never negative.</summary>
    public Quantity Incoming { get; private set; } = Quantity.Zero("EA");

    /// <summary>Opens a zero position for a location and stock-keeping unit.</summary>
    public static AvailableBalance Open(
        Guid tenantId,
        Guid? storeId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An availability position must belong to a tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("An availability position must belong to a company.", nameof(companyId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("An availability position must name a location.", nameof(locationId));
        }

        StockItemReference.Validate(itemId, itemVariantId);

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw new ArgumentException("A unit of measure is required.", nameof(unitOfMeasure));
        }

        var balance = new AvailableBalance(
            tenantId,
            storeId,
            locationId,
            itemId,
            itemVariantId,
            Quantity.Zero(unitOfMeasure),
            Quantity.Zero(unitOfMeasure),
            Quantity.Zero(unitOfMeasure));
        balance.AssignCompany(companyId);
        return balance;
    }

    /// <summary>
    /// Applies a hold that has just been written: reserved grows, nothing else moves. The caller
    /// has already proven available covers it inside a locked transaction — this method asserts
    /// the arithmetic rather than re-deciding it.
    /// </summary>
    /// <param name="quantity">How much was held. Must be positive and in this row's unit of measure.</param>
    public void ApplyHold(Quantity quantity)
    {
        EnsureUsable(quantity);
        Reserved += quantity;
    }

    /// <summary>
    /// Applies a terminal row (consume, release, expiry): reserved shrinks by the full held
    /// quantity. A clamp is a defect here, not mercy — the terminal row always carries the hold's
    /// own quantity, so anything else means the projection has drifted from the ledger.
    /// </summary>
    /// <param name="quantity">How much was closed. Must be positive and in this row's unit of measure.</param>
    /// <exception cref="InventoryRuleException">Closing more than is reserved.</exception>
    public void ApplyClose(Quantity quantity)
    {
        EnsureUsable(quantity);

        if (quantity > Reserved)
        {
            throw InventoryRuleException.ReservationCloseExceedsHeld(Reserved, quantity);
        }

        Reserved -= quantity;
    }

    /// <summary>Refreshes the staging figure from the warehouse feed.</summary>
    /// <param name="inStaging">What currently sits in staging bins. Must not be negative.</param>
    public void RefreshStaging(Quantity inStaging)
    {
        EnsureSameUnit(inStaging);

        if (inStaging.IsNegative)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        InStaging = inStaging;
    }

    private void EnsureUsable(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        EnsureSameUnit(quantity);
    }

    private void EnsureSameUnit(Quantity quantity)
    {
        if (!string.Equals(Reserved.UnitOfMeasure, quantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(Reserved.UnitOfMeasure, quantity.UnitOfMeasure);
        }
    }
}
