using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// On-hand quantity and weighted-average cost for one stock-keeping unit at one location.
/// </summary>
/// <remarks>
/// <para>
/// ADR-005 (LOCKED): stock levels are never a mutable column in the sense of being <em>corrected</em>
/// directly — but the same ADR names <c>inventory.stock_balance</c> as a materialised projection,
/// "rebuilt from the ledger and maintained transactionally". This is that table. It is never written
/// to independently of a <see cref="StockLedgerEntry"/>: every method here is called from the same
/// application-layer poster that inserts the ledger row it corresponds to, in the same database
/// transaction, so the two can never disagree about what happened. It genuinely is a projection — sum
/// every <see cref="StockLedgerEntry"/> for the same location and stock-keeping unit and the on-hand
/// quantity this row reports is what you get.
/// </para>
/// <para>
/// Valuation is weighted-average moving cost (ADR-063): every receipt blends its cost into the running
/// average, and every issue relieves quantity at that average without changing it. This is the
/// simplest costing method to build and maintain correctly for a first cut (<c>CLAUDE.md</c> §1),
/// avoiding a costing-layer stack (FIFO lots, specific identification) a later stage can add if a
/// tenant's accounting method requires it.
/// </para>
/// <para>
/// Deliberately <em>not</em> replicated between nodes as a value — see the type's
/// <see cref="ReplicatedAttribute"/> and ADR-064. It is <see cref="ReplicationScope.NodeLocal"/>:
/// each node rebuilds and maintains its own copy from the ledger entries it has applied, whether
/// locally originated or received from a peer. A running total is not safely mergeable the way an
/// accumulating ledger is — two nodes each applying their own delta to a synced total would double an
/// overlapping movement or silently diverge, which is exactly the class of bug ADR-007's conflict
/// policies exist to avoid inventing.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class StockBalance : Entity
{
    private StockBalance(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantityOnHand,
        Money averageCost)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        QuantityOnHand = quantityOnHand;
        AverageCost = averageCost;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StockBalance()
    {
    }

    /// <summary>The location this balance is held at.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item this balance is for, or <c>null</c> when it is a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this balance is for, or <c>null</c> when it is an item directly.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What is currently on hand.</summary>
    public Quantity QuantityOnHand { get; private set; }

    /// <summary>The weighted-average cost of one unit currently on hand.</summary>
    public Money AverageCost { get; private set; }

    /// <summary>The value of everything on hand — <see cref="QuantityOnHand"/> extended by <see cref="AverageCost"/>.</summary>
    public Money TotalValue => QuantityOnHand.Extend(AverageCost);

    /// <summary>Opens a new balance row at zero, in the unit of measure and currency its first movement establishes.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store — the location's own.</param>
    /// <param name="locationId">The location.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="unitOfMeasure">The unit of measure every future movement against this row must share.</param>
    /// <param name="currency">The currency every future movement against this row must share.</param>
    public static StockBalance Open(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        string currency)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stock balance must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A stock balance must name a location.", nameof(locationId));
        }

        StockItemReference.Validate(itemId, itemVariantId);

        return new StockBalance(
            tenantId,
            storeId,
            locationId,
            itemId,
            itemVariantId,
            Quantity.Zero(unitOfMeasure),
            Money.Zero(currency));
    }

    /// <summary>
    /// Blends a receipt into the running weighted average and increases on-hand quantity.
    /// </summary>
    /// <param name="quantity">How much arrived. Must be positive.</param>
    /// <param name="unitCost">What it cost per unit.</param>
    /// <returns>The value received — <paramref name="quantity"/> extended by <paramref name="unitCost"/>.</returns>
    /// <exception cref="InventoryRuleException">
    /// The quantity is not positive, or <paramref name="quantity"/>/<paramref name="unitCost"/> is in a
    /// different unit of measure or currency than this balance is already held in.
    /// </exception>
    public Money ApplyReceipt(Quantity quantity, Money unitCost)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);
        EnsureSameCurrency(unitCost.Currency);

        Quantity newQuantityOnHand = QuantityOnHand + quantity;
        Money existingValue = QuantityOnHand.Extend(AverageCost);
        Money incomingValue = quantity.Extend(unitCost);

        // Weighted average: blend the value already on hand with the value just received, over the new
        // total quantity. A brand-new balance (QuantityOnHand is zero) collapses to the receipt's own
        // unit cost, which is exactly what a first receipt should do.
        AverageCost = newQuantityOnHand.IsZero
            ? unitCost
            : (existingValue + incomingValue) / newQuantityOnHand.Value;

        QuantityOnHand = newQuantityOnHand;

        return incomingValue;
    }

    /// <summary>
    /// Relieves on-hand quantity at the current weighted-average cost. The average itself does not
    /// change — an issue values out at cost, it does not revalue what remains.
    /// </summary>
    /// <param name="quantity">How much to relieve. Must be positive.</param>
    /// <returns>The value relieved — <paramref name="quantity"/> extended by the average cost before this call.</returns>
    /// <exception cref="InventoryRuleException">
    /// The quantity is not positive, is in a different unit of measure than this balance, or would take
    /// on-hand quantity below zero.
    /// </exception>
    public Money ApplyIssue(Quantity quantity)
    {
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        EnsureSameUnitOfMeasure(quantity.UnitOfMeasure);

        if (quantity > QuantityOnHand)
        {
            throw InventoryRuleException.InsufficientStock(QuantityOnHand, quantity);
        }

        Money relieved = quantity.Extend(AverageCost);
        QuantityOnHand -= quantity;

        return relieved;
    }

    private void EnsureSameUnitOfMeasure(string unitOfMeasure)
    {
        if (!string.Equals(QuantityOnHand.UnitOfMeasure, unitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(QuantityOnHand.UnitOfMeasure, unitOfMeasure);
        }
    }

    private void EnsureSameCurrency(string currency)
    {
        if (!string.Equals(AverageCost.Currency, currency, StringComparison.Ordinal))
        {
            throw InventoryRuleException.CurrencyMismatch(AverageCost.Currency, currency);
        }
    }
}
