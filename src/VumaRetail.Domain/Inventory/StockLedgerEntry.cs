using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// One immutable, append-only record of stock moving in, out, or between locations.
/// </summary>
/// <remarks>
/// <para>
/// ADR-005 (LOCKED) and <c>CLAUDE.md</c> §7 rule 6: stock levels are never a mutable column. Every
/// event that changes what is on hand — a receipt, a sale issue, an adjustment, either side of a
/// transfer, a stocktake variance — writes a new row here and nothing already written is ever updated
/// or deleted. <see cref="StockBalance"/> is the projection this table's rows sum to; it is maintained
/// transactionally alongside every insert here, never edited independently of one.
/// </para>
/// <para>
/// Marked <see cref="IImmutableRecord"/>: the persistence layer refuses this entity in the
/// <c>Modified</c> or <c>Deleted</c> state (§7 rule 7), so the append-only guarantee holds structurally
/// rather than by convention. Correcting a mistaken entry means posting a new one that reverses it, the
/// same way a financial document is amended by a credit note rather than an edit.
/// </para>
/// <para>
/// <see cref="ItemId"/>/<see cref="ItemVariantId"/> follow <c>Barcode</c>'s precedent from Stage 06:
/// exactly one is set, resolving to whichever of an item or its variant is the sellable unit. Both are
/// plain <see cref="Guid"/> references into <c>catalog</c>, validated by the command handler that
/// constructs this row — never a cross-schema foreign key (<c>CONVENTIONS.md</c> §2).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class StockLedgerEntry : Entity, IImmutableRecord
{
    private StockLedgerEntry(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        StockMovementType movementType,
        Quantity quantity,
        Money unitCost,
        StockReferenceType referenceType,
        Guid? referenceId,
        AdjustmentReasonCode? reasonCode,
        string? note)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        MovementType = movementType;
        Quantity = quantity;
        UnitCost = unitCost;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        ReasonCode = reasonCode;
        Note = note;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StockLedgerEntry()
    {
    }

    /// <summary>The location this movement affects.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item this movement affects directly, or <c>null</c> when it is a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this movement affects, or <c>null</c> when it is an item directly.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What kind of movement this is.</summary>
    public StockMovementType MovementType { get; private set; }

    /// <summary>
    /// The signed change to on-hand quantity: positive for a receipt, a transfer-in or a positive
    /// adjustment/variance; negative for a sale issue, a transfer-out or a negative one.
    /// </summary>
    public Quantity Quantity { get; private set; }

    /// <summary>
    /// The value of one unit at the moment of this movement — the receipt's cost for an inbound
    /// movement, or the weighted-average cost being relieved for an outbound one.
    /// </summary>
    public Money UnitCost { get; private set; }

    /// <summary>What kind of document, if any, this movement correlates to.</summary>
    public StockReferenceType ReferenceType { get; private set; }

    /// <summary>
    /// The id of the document this movement belongs to — a transfer's shared id, a stocktake session, a
    /// sale — or <c>null</c> for a plain manual receipt or adjustment with no document of its own.
    /// </summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>Why an adjustment was made. Set only when <see cref="MovementType"/> is <see cref="StockMovementType.Adjustment"/>.</summary>
    public AdjustmentReasonCode? ReasonCode { get; private set; }

    /// <summary>An optional free-text note.</summary>
    public string? Note { get; private set; }

    /// <summary>The total value this movement carries — <see cref="Quantity"/> extended by <see cref="UnitCost"/>.</summary>
    public Money Value => Quantity.Extend(UnitCost);

    /// <summary>
    /// Posts a new ledger entry. The one way any row in this table comes to exist — there is no setter,
    /// and nothing here can be changed once created.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store — the location's own, or <c>null</c> for a tenant-wide location.</param>
    /// <param name="locationId">The location this movement affects.</param>
    /// <param name="itemId">The item, when it has no variants. Exactly one of this and <paramref name="itemVariantId"/> must be set.</param>
    /// <param name="itemVariantId">The variant. Exactly one of this and <paramref name="itemId"/> must be set.</param>
    /// <param name="movementType">What kind of movement this is.</param>
    /// <param name="quantity">The signed quantity delta. Must not be zero.</param>
    /// <param name="unitCost">The value of one unit at the moment of this movement.</param>
    /// <param name="referenceType">What kind of document this correlates to, if any.</param>
    /// <param name="referenceId">The correlating document's id. Required unless <paramref name="referenceType"/> is <see cref="StockReferenceType.Manual"/>.</param>
    /// <param name="reasonCode">Why an adjustment was made. Required when <paramref name="movementType"/> is <see cref="StockMovementType.Adjustment"/>.</param>
    /// <param name="note">An optional free-text note.</param>
    /// <exception cref="InventoryRuleException">A structural invariant was broken.</exception>
    public static StockLedgerEntry Post(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        StockMovementType movementType,
        Quantity quantity,
        Money unitCost,
        StockReferenceType referenceType,
        Guid? referenceId,
        AdjustmentReasonCode? reasonCode,
        string? note)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stock movement must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A stock movement must name a location.", nameof(locationId));
        }

        StockItemReference.Validate(itemId, itemVariantId);

        if (quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBeNonZero();
        }

        if (movementType == StockMovementType.Adjustment && reasonCode is null)
        {
            throw InventoryRuleException.AdjustmentRequiresReasonCode();
        }

        if (referenceType != StockReferenceType.Manual && referenceId is null)
        {
            throw InventoryRuleException.ReferenceIdRequired();
        }

        return new StockLedgerEntry(
            tenantId,
            storeId,
            locationId,
            itemId,
            itemVariantId,
            movementType,
            quantity,
            unitCost,
            referenceType,
            referenceId,
            reasonCode,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }
}

/// <summary>What kind of event a <see cref="StockLedgerEntry"/> records.</summary>
public enum StockMovementType
{
    /// <summary>Stock arriving — a supplier delivery, a found-stock correction.</summary>
    Receipt = 0,

    /// <summary>Stock leaving through a sale. Stage 09 (POS) is this movement's real trigger; Stage 08 exposes the command it will call.</summary>
    SaleIssue = 1,

    /// <summary>A documented correction to on-hand quantity — damage, loss, a manual correction.</summary>
    Adjustment = 2,

    /// <summary>Stock leaving one location as the source side of a transfer.</summary>
    TransferOut = 3,

    /// <summary>Stock arriving at another location as the destination side of a transfer.</summary>
    TransferIn = 4,

    /// <summary>The difference between a physical count and the system quantity, posted on finalizing a stocktake.</summary>
    StocktakeVariance = 5,

    /// <summary>
    /// Stock coming back over the counter on a sales return. Stage 10's mirror of
    /// <see cref="SaleIssue"/>, valued at what the original issue cost rather than at today's average.
    /// </summary>
    SalesReturn = 6,
}

/// <summary>What kind of document, if any, a <see cref="StockLedgerEntry"/> correlates to.</summary>
public enum StockReferenceType
{
    /// <summary>No document — a plain receipt or adjustment recorded directly against the ledger.</summary>
    Manual = 0,

    /// <summary>Correlates the two sides of a <see cref="StockTransfer"/>.</summary>
    Transfer = 1,

    /// <summary>Correlates to a <see cref="StocktakeSession"/>.</summary>
    Stocktake = 2,

    /// <summary>Correlates to a sale. Stage 09 supplies the real sale id; Stage 08 only records the correlation.</summary>
    Sale = 3,

    /// <summary>Correlates to a <c>sales.sales_returns</c> document. Stage 10.</summary>
    SalesReturn = 4,

    /// <summary>
    /// Correlates to an <c>imports.import_batches</c> row — an opening or counted stock figure that
    /// arrived in a spreadsheet. Stage 11.
    /// </summary>
    /// <remarks>
    /// Its own reference type rather than <see cref="Manual"/> for the reason Stage 10's
    /// <see cref="SalesReturn"/> exists: an adjustment nobody can trace back to the document that
    /// caused it looks exactly like a shrinkage write-off, and the two must not be confusable in an
    /// investigation. It is also what a rollback matches on to find the entries it must reverse.
    /// </remarks>
    Import = 5,

    /// <summary>
    /// Correlates to a <c>procurement.goods_receipts</c> document — a supplier delivery. Stage 12.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own reference type while sharing <see cref="StockMovementType.Receipt"/>, rather than a new
    /// movement type. The <em>movement</em> is genuinely the one Stage 08 already had — stock arriving,
    /// valued at what it cost, blended into the weighted average — and the existing
    /// <c>inventory.receipt.posted</c> rule already debits inventory and credits
    /// goods-received-not-invoiced, which is exactly what a supplier delivery does. A new movement type
    /// would have needed a new posting rule to say the same thing.
    /// </para>
    /// <para>
    /// What it does need is the reference, for the reason <see cref="Import"/> and
    /// <see cref="SalesReturn"/> both have one: a receipt nobody can trace back to the delivery that
    /// caused it is indistinguishable from a manual correction, and Stage 12's three-way match reads
    /// these entries to answer "did this actually arrive".
    /// </para>
    /// </remarks>
    GoodsReceipt = 6,
}

/// <summary>Why a <see cref="StockMovementType.Adjustment"/> was made.</summary>
public enum AdjustmentReasonCode
{
    /// <summary>Stock was found damaged and written off.</summary>
    Damage = 0,

    /// <summary>Stock is missing with no other explanation — shrinkage, theft, a miscount never reconciled.</summary>
    Loss = 1,

    /// <summary>More stock was found on hand than the system expected.</summary>
    Found = 2,

    /// <summary>A data-entry or process correction unrelated to a physical count.</summary>
    Correction = 3,

    /// <summary>Anything not covered above. <see cref="StockLedgerEntry.Note"/> should say what.</summary>
    Other = 4,
}
