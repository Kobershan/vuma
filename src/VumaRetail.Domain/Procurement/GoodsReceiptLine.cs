using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>What arrived against one purchase order line — how much was kept, how much was sent back.</summary>
/// <remarks>
/// <para>
/// <see cref="OrderedQuantity"/> and <see cref="UnitCost"/> are snapshots off the order line rather
/// than lookups. Two reasons, and both bite later: the order can be amended after the delivery, and a
/// constraint can then re-assert business rule 8's arithmetic on the row itself without joining to
/// another table.
/// </para>
/// <para>
/// <see cref="RejectedQuantity"/> never enters stock and never counts as received. It is kept because
/// it is the raw material of a supplier scorecard — a rejection that is only recorded as "we received
/// less" is a rejection nobody can hold the supplier to.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class GoodsReceiptLine : Entity
{
    private GoodsReceiptLine(
        Guid tenantId,
        Guid? storeId,
        Guid goodsReceiptId,
        Guid purchaseOrderLineId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity orderedQuantity,
        Quantity acceptedQuantity,
        Quantity rejectedQuantity,
        GoodsRejectionReason rejectionReason,
        Money unitCost,
        Money acceptedValue,
        string? note)
        : base(tenantId, storeId)
    {
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        OrderedQuantity = orderedQuantity;
        AcceptedQuantity = acceptedQuantity;
        RejectedQuantity = rejectedQuantity;
        RejectionReason = rejectionReason;
        UnitCost = unitCost;
        AcceptedValue = acceptedValue;
        Note = note;
        StockPosting = GoodsReceiptPostingStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private GoodsReceiptLine()
    {
    }

    /// <summary>The receipt this line belongs to.</summary>
    public Guid GoodsReceiptId { get; private set; }

    /// <summary>The order line the goods came against.</summary>
    public Guid PurchaseOrderLineId { get; private set; }

    /// <summary>The item, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What the order called this. Copied, so the receipt reads without a join.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much the order line ordered, snapshotted when the receipt was written.</summary>
    public Quantity OrderedQuantity { get; private set; }

    /// <summary>How much was accepted into stock. Always positive.</summary>
    public Quantity AcceptedQuantity { get; private set; }

    /// <summary>How much arrived and was turned away. Zero on a clean delivery.</summary>
    public Quantity RejectedQuantity { get; private set; }

    /// <summary>Why anything was turned away.</summary>
    public GoodsRejectionReason RejectionReason { get; private set; }

    /// <summary>
    /// What one unit costs excluding tax — the order line's <c>NetUnitCost</c>, and what the stock is
    /// valued at (business rule 8, ADR-086).
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> the order line's as-ordered <c>UnitCost</c>, which on a South African
    /// order is VAT-inclusive. See <c>PurchaseOrderLine.NetUnitCost</c> for why capitalising recoverable
    /// input VAT into inventory is wrong twice over. The three-way match still compares the supplier's
    /// invoiced unit cost against the as-ordered one — that comparison is like for like and unaffected.
    /// </remarks>
    public Money UnitCost { get; private set; }

    /// <summary>The accepted quantity at <see cref="UnitCost"/>, rounded once.</summary>
    public Money AcceptedValue { get; private set; }

    /// <summary>Anything the receiver wanted on the record.</summary>
    public string? Note { get; private set; }

    /// <summary>What happened when this line's stock was posted.</summary>
    public GoodsReceiptPostingStatus StockPosting { get; private set; }

    /// <summary>The <c>inventory.stock_ledger_entries</c> row this line produced, when it produced one.</summary>
    public Guid? StockLedgerEntryId { get; private set; }

    /// <summary>Why the stock posting was refused, when it was.</summary>
    public string? StockPostingNote { get; private set; }

    /// <summary>How much arrived in total — accepted plus rejected.</summary>
    public Quantity DeliveredQuantity => AcceptedQuantity + RejectedQuantity;

    /// <summary>
    /// Builds a receipt line off the order line it delivers against. Prefer
    /// <see cref="GoodsReceipt.AddLine"/>, which enforces the document's rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store receiving.</param>
    /// <param name="goodsReceiptId">The receipt.</param>
    /// <param name="orderLine">The order line the goods came against.</param>
    /// <param name="acceptedQuantity">How much was accepted. Positive.</param>
    /// <param name="rejectedQuantity">How much was turned away, or <c>null</c>.</param>
    /// <param name="rejectionReason">Why, when anything was rejected.</param>
    /// <param name="note">Anything the receiver wants recorded, or <c>null</c>.</param>
    /// <exception cref="ProcurementRuleException">
    /// The accepted quantity is not positive, or a rejection was recorded without a reason (or a reason
    /// without a quantity).
    /// </exception>
    public static GoodsReceiptLine For(
        Guid tenantId,
        Guid? storeId,
        Guid goodsReceiptId,
        PurchaseOrderLine orderLine,
        Quantity acceptedQuantity,
        Quantity? rejectedQuantity,
        GoodsRejectionReason rejectionReason,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(orderLine);

        ProcurementLineRules.RequirePositive(acceptedQuantity);

        Quantity rejected = rejectedQuantity ?? Quantity.Zero(acceptedQuantity.UnitOfMeasure);

        if (rejected.IsNegative)
        {
            throw ProcurementRuleException.QuantityMustBePositive();
        }

        // Both halves of the pairing, because either one alone is a record nobody can act on: a rejected
        // quantity with no reason cannot be raised with the supplier, and a reason with nothing behind it
        // is a scorecard entry for something that did not happen.
        if (rejected.IsZero != (rejectionReason is GoodsRejectionReason.None))
        {
            throw ProcurementRuleException.RejectionReasonRequired();
        }

        // The net cost, not the as-ordered one (ADR-086), extended and rounded once — the §4.14 rule.
        Money netUnitCost = orderLine.NetUnitCost;
        Money value = acceptedQuantity.Extend(netUnitCost).RoundToCurrencyScale();

        return new GoodsReceiptLine(
            tenantId,
            storeId,
            goodsReceiptId,
            orderLine.Id,
            orderLine.ItemId,
            orderLine.ItemVariantId,
            orderLine.Description,
            orderLine.Quantity,
            acceptedQuantity,
            rejected,
            rejectionReason,
            netUnitCost,
            value,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }

    /// <summary>Records that this line's stock went into inventory.</summary>
    /// <param name="stockLedgerEntryId">The ledger entry that was posted.</param>
    public void RecordStockPosted(Guid stockLedgerEntryId)
    {
        StockPosting = GoodsReceiptPostingStatus.Posted;
        StockLedgerEntryId = stockLedgerEntryId;
        StockPostingNote = null;
    }

    /// <summary>
    /// Records that the ledger refused this line's stock, and why. The receipt still stands — the goods
    /// are physically here (ADR-073, business rule 9).
    /// </summary>
    /// <param name="reason">The refusal, in words a manager reconciling it can act on.</param>
    public void RecordStockPostingRefused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        StockPosting = GoodsReceiptPostingStatus.Refused;
        StockLedgerEntryId = null;
        StockPostingNote = reason.Length > 500 ? reason[..500] : reason;
    }
}
