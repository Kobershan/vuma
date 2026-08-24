using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// The claim that the goods physically arrived. The only document in the buying chain that moves
/// stock, and the one that puts value into the books.
/// </summary>
/// <remarks>
/// <para>
/// <b>Received and rejected are separate figures, and only the accepted half moves</b> (business rule
/// 7). A delivery of 100 cases of which 6 are broken is not a delivery of 94 — the shop received 100
/// and turned 6 away, the supplier will be asked about the 6, and ninety days of those 6s are what a
/// scorecard is made of. Collapsing the two loses the only record that anything went wrong.
/// </para>
/// <para>
/// <b>Stock is valued at the order's unit cost</b> (business rule 8), not at today's weighted average.
/// That is what a purchase <em>is</em>: the entry that moves the average, rather than one priced by it
/// (ADR-068).
/// </para>
/// <para>
/// <b>A refused stock movement does not fail the receipt</b> (ADR-073, business rule 9). POS learned
/// this with a customer standing at the counter; the same thing is true with a truck at the dock. The
/// goods are here either way, so the line records <see cref="GoodsReceiptPostingStatus.Refused"/>, the
/// document counts them, and a reconciliation endpoint lists them. A receipt that threw would leave the
/// paperwork claiming nothing arrived while the pallets sat in the yard.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class GoodsReceipt : Entity
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt(
        Guid id,
        Guid tenantId,
        Guid? storeId,
        Guid purchaseOrderId,
        Guid partnerId,
        string receiptNumber,
        Guid locationId,
        string currency,
        string? deliveryNoteNumber,
        Guid receivedByUserId,
        DateTimeOffset receivedAt)
        : base(id, tenantId, storeId)
    {
        PurchaseOrderId = purchaseOrderId;
        PartnerId = partnerId;
        ReceiptNumber = receiptNumber;
        LocationId = locationId;
        Currency = currency;
        DeliveryNoteNumber = deliveryNoteNumber;
        ReceivedByUserId = receivedByUserId;
        ReceivedAt = receivedAt;
        Status = GoodsReceiptStatus.Draft;
        ReceivedValue = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private GoodsReceipt()
    {
    }

    /// <summary>The order the goods came against.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>The supplier, copied off the order so a receipt reads on its own.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>GRN</c>.</summary>
    public string ReceiptNumber { get; private set; } = string.Empty;

    /// <summary>Where the goods landed. The order's delivery location.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The currency, bound from the order (business rule 4).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The supplier's own delivery-note number, as written on the paper that came with the truck.</summary>
    public string? DeliveryNoteNumber { get; private set; }

    /// <summary>Who checked the delivery in.</summary>
    public Guid ReceivedByUserId { get; private set; }

    /// <summary>When the goods arrived, UTC. What "on time" is measured on (business rule 16).</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Where the receipt stands.</summary>
    public GoodsReceiptStatus Status { get; private set; }

    /// <summary>The value of what was accepted, at the order's costs. Sums only the accepted quantity.</summary>
    public Money ReceivedValue { get; private set; }

    /// <summary>When it completed, or <c>null</c>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When it was cancelled, or <c>null</c>.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>What arrived.</summary>
    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    /// <summary>True while lines may still be added.</summary>
    public bool IsDraft => Status is GoodsReceiptStatus.Draft;

    /// <summary>
    /// How many lines completed without the stock movement being posted (ADR-073). Zero on a normal
    /// receipt; anything else is a reconciliation the store owes itself.
    /// </summary>
    public int StockPostingsRefused
        => _lines.Count(line => line.StockPosting is GoodsReceiptPostingStatus.Refused);

    /// <summary>Opens a receipt against an issued order.</summary>
    /// <param name="order">The order the goods came against. Read; its lines are advanced on completion.</param>
    /// <param name="receiptNumber">The document number.</param>
    /// <param name="deliveryNoteNumber">The supplier's delivery-note number, or <c>null</c>.</param>
    /// <param name="receivedByUserId">Who checked it in.</param>
    /// <param name="receivedAt">When the goods arrived, UTC.</param>
    /// <param name="id">
    /// The receipt's identity, or <c>null</c> to mint one here. A caller that already sent this create
    /// once supplies the id it used the first time, which makes a dropped-connection retry idempotent.
    /// </param>
    /// <exception cref="ProcurementRuleException">The order is not open for receipt (business rule 6).</exception>
    public static GoodsReceipt Open(
        PurchaseOrder order,
        string receiptNumber,
        string? deliveryNoteNumber,
        Guid receivedByUserId,
        DateTimeOffset receivedAt,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptNumber);

        if (!order.IsOpenForReceipt)
        {
            throw ProcurementRuleException.OrderNotOpenForReceipt(order.Status);
        }

        return new GoodsReceipt(
            id ?? UuidV7.NewGuid(),
            order.TenantId,
            order.StoreId,
            order.Id,
            order.PartnerId,
            receiptNumber.Trim(),
            order.LocationId,
            order.Currency,
            string.IsNullOrWhiteSpace(deliveryNoteNumber) ? null : deliveryNoteNumber.Trim(),
            receivedByUserId,
            receivedAt);
    }

    /// <summary>Records what arrived against one of the order's lines.</summary>
    /// <param name="orderLine">The order line. Must belong to this receipt's order.</param>
    /// <param name="acceptedQuantity">How much was accepted into stock. Positive.</param>
    /// <param name="rejectedQuantity">How much was turned away, or <c>null</c> if none was.</param>
    /// <param name="rejectionReason">Why, when anything was rejected.</param>
    /// <param name="note">Anything the receiver wants on the record.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="ProcurementRuleException">
    /// The receipt is not a draft, the order line belongs to another order, the same line is already on
    /// this receipt, or the rejection is malformed.
    /// </exception>
    public GoodsReceiptLine AddLine(
        PurchaseOrderLine orderLine,
        Quantity acceptedQuantity,
        Quantity? rejectedQuantity,
        GoodsRejectionReason rejectionReason,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(orderLine);
        EnsureDraft();

        if (orderLine.PurchaseOrderId != PurchaseOrderId)
        {
            throw new ProcurementNotFoundException("purchase order line on this order", orderLine.Id);
        }

        if (_lines.Exists(line => line.PurchaseOrderLineId == orderLine.Id))
        {
            // Two rows for one order line is exactly how an over-receipt slips past the per-line
            // tolerance check: each row passes on its own and the pair does not.
            throw ProcurementRuleException.DuplicateOrderLineReference("receipt");
        }

        GoodsReceiptLine created = GoodsReceiptLine.For(
            TenantId, StoreId, Id, orderLine, acceptedQuantity, rejectedQuantity, rejectionReason, note);

        _lines.Add(created);
        RecalculateValue();

        return created;
    }

    /// <summary>
    /// Freezes the receipt. Stock movement and the order's advance are the completion service's, which
    /// calls this last.
    /// </summary>
    /// <param name="completedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">Not a draft, or it has no lines.</exception>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw ProcurementRuleException.ReceiptHasNoLines();
        }

        Status = GoodsReceiptStatus.Completed;
        CompletedAt = completedAt;
    }

    /// <summary>Abandons a draft receipt. Nothing moved.</summary>
    /// <param name="cancelledAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is not a draft.</exception>
    public void Cancel(DateTimeOffset cancelledAt)
    {
        EnsureDraft();

        Status = GoodsReceiptStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    /// <summary>Finds a line on this receipt.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="ProcurementNotFoundException">No such line on this receipt.</exception>
    public GoodsReceiptLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new ProcurementNotFoundException("goods receipt line", lineId);

    /// <summary>Refuses to proceed unless the receipt is still a draft.</summary>
    /// <exception cref="ProcurementRuleException">It is not a draft.</exception>
    public void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw ProcurementRuleException.UnexpectedReceiptStatus(Status);
        }
    }

    private void RecalculateValue()
    {
        Money value = Money.Zero(Currency);

        foreach (GoodsReceiptLine line in _lines)
        {
            value += line.AcceptedValue;
        }

        ReceivedValue = value;
    }
}
