using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// The commitment. The document a supplier will hold the shop to, and the one every later document in
/// the chain is measured against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable once issued</b> (§7 rule 7, business rule 3). Lines may be added, repriced and removed
/// while it is a draft; approval freezes them and issue sends them. A change after that is
/// <see cref="Amend"/>: a new order at <c>Version + 1</c> naming the one it supersedes, with the
/// predecessor cancelled. That is more work than editing a row, and it is the point — a supplier who
/// received PO-000123 for 100 cases has a document, and quietly turning it into 60 cases in our
/// database does not change what they are going to deliver and invoice.
/// </para>
/// <para>
/// <b>The currency is the supplier's and is bound here</b> (business rule 4). Every line, receipt and
/// match against this order is denominated in it. <c>PROGRESS.md</c> §4.13 is what happens when a
/// document lets a currency in through a line instead: one foreign-currency line, even on an abandoned
/// document, and every subsequent read of the aggregate throws with no API path out. Binding it at the
/// document and refusing a mismatched line makes that unreachable.
/// </para>
/// <para>
/// <b>It posts nothing to the GL</b> (ADR-081). An order is a promise, not a transaction — no value has
/// moved and no obligation has crystallised. Value enters the books at receipt, through Stage 08's
/// ledger, and the liability at the three-way match. Neither of those is this document's decision, and
/// §7 rule 12 means neither of them names an account here either.
/// </para>
/// <para>
/// <see cref="PurchaseOrderLine.ReceivedQuantity"/> and
/// <see cref="PurchaseOrderLine.InvoicedQuantity"/> are maintained running totals rather than sums
/// computed on read. They are what business rules 6 and 11 are checked against, inside the command's
/// transaction, and a total that is recomputed from a join every time is a total two concurrent
/// receipts can both read as low.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PurchaseOrder : Entity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(
        Guid tenantId,
        Guid? storeId,
        string orderNumber,
        Guid partnerId,
        string currency,
        Guid locationId,
        DateOnly expectedAt,
        int version,
        Guid? amendsPurchaseOrderId,
        Guid? rfqResponseId,
        string? notes,
        DateTimeOffset raisedAt)
        : base(tenantId, storeId)
    {
        OrderNumber = orderNumber;
        PartnerId = partnerId;
        Currency = currency;
        LocationId = locationId;
        ExpectedAt = expectedAt;
        Version = version;
        AmendsPurchaseOrderId = amendsPurchaseOrderId;
        RfqResponseId = rfqResponseId;
        Notes = notes;
        RaisedAt = raisedAt;
        Status = PurchaseOrderStatus.Draft;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PurchaseOrder()
    {
    }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>PO</c>.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The supplier. A bare id into <c>partners</c>, never a foreign key.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The currency, bound at creation (business rule 4).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Where the goods are to be delivered. A bare id into <c>inventory</c>.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>When the goods are expected. What "on time" is measured against (business rule 16).</summary>
    public DateOnly ExpectedAt { get; private set; }

    /// <summary>Where the order stands.</summary>
    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>1 for an original order, incremented by each amendment.</summary>
    public int Version { get; private set; }

    /// <summary>The order this one supersedes, or <c>null</c> for an original.</summary>
    public Guid? AmendsPurchaseOrderId { get; private set; }

    /// <summary>The quote this order was awarded from, or <c>null</c> if it was raised directly.</summary>
    public Guid? RfqResponseId { get; private set; }

    /// <summary>Delivery instructions, terms, anything the supplier needs to read.</summary>
    public string? Notes { get; private set; }

    /// <summary>The order excluding tax — the sum of its lines.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax on the order — the sum of its lines, each computed once at authoring (ADR-075).</summary>
    public Money Tax { get; private set; }

    /// <summary>What the order commits the shop to — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>When it was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>Who approved it, or <c>null</c>.</summary>
    public Guid? ApprovedByUserId { get; private set; }

    /// <summary>When it was approved, or <c>null</c>.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>When it was sent to the supplier, or <c>null</c>.</summary>
    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>When it was closed, or <c>null</c>.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>When it was cancelled, or <c>null</c>.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Why it was cancelled, when it was.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>What was ordered.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    /// <summary>True while lines may still be changed.</summary>
    public bool IsDraft => Status is PurchaseOrderStatus.Draft;

    /// <summary>True while goods may be received against it (business rule 6).</summary>
    public bool IsOpenForReceipt => Status
        is PurchaseOrderStatus.Issued
        or PurchaseOrderStatus.PartiallyReceived;

    /// <summary>True once every line has had everything it ordered delivered.</summary>
    public bool IsFullyReceived => _lines.Count > 0 && _lines.TrueForAll(line => line.IsFullyReceived);

    /// <summary>Raises a new purchase order.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store buying.</param>
    /// <param name="orderNumber">The document number.</param>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="currency">The currency, bound for the life of the document.</param>
    /// <param name="locationId">Where the goods are to be delivered.</param>
    /// <param name="expectedAt">When they are expected.</param>
    /// <param name="rfqResponseId">The quote this was awarded from, or <c>null</c>.</param>
    /// <param name="notes">Delivery instructions and terms, or <c>null</c>.</param>
    /// <param name="raisedAt">When, UTC.</param>
    public static PurchaseOrder Raise(
        Guid tenantId,
        Guid? storeId,
        string orderNumber,
        Guid partnerId,
        string currency,
        Guid locationId,
        DateOnly expectedAt,
        Guid? rfqResponseId,
        string? notes,
        DateTimeOffset raisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        // Money's constructor validates the ISO code; going through it here means a malformed currency
        // is refused before a document exists rather than at the first line that uses it.
        Money zero = Money.Zero(currency);

        return new PurchaseOrder(
            tenantId,
            storeId,
            orderNumber.Trim(),
            partnerId,
            zero.Currency,
            locationId,
            expectedAt,
            version: 1,
            amendsPurchaseOrderId: null,
            rfqResponseId,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            raisedAt);
    }

    /// <summary>
    /// Opens a replacement for this order at <c>Version + 1</c> and cancels this one (business rule 3).
    /// </summary>
    /// <param name="orderNumber">The replacement's document number.</param>
    /// <param name="reason">Why the original is being superseded.</param>
    /// <param name="amendedAt">When, UTC.</param>
    /// <returns>The new draft order. Its lines are the caller's to write.</returns>
    /// <exception cref="ProcurementRuleException">
    /// This order is already closed or cancelled — there is nothing left to amend.
    /// </exception>
    /// <remarks>
    /// The replacement comes back as a <see cref="PurchaseOrderStatus.Draft"/> with no lines rather than
    /// as a copy. Copying is the tempting shortcut and it is wrong: the reason to amend is that
    /// something changed, so a copy is a document whose every line has to be re-checked anyway, and one
    /// that silently carries a line somebody meant to remove is exactly the bug the amendment was
    /// raised to fix.
    /// </remarks>
    public PurchaseOrder Amend(string orderNumber, string reason, DateTimeOffset amendedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
        {
            throw ProcurementRuleException.UnexpectedOrderStatus(Status);
        }

        PurchaseOrder replacement = new(
            TenantId,
            StoreId,
            orderNumber.Trim(),
            PartnerId,
            Currency,
            LocationId,
            ExpectedAt,
            Version + 1,
            Id,
            RfqResponseId,
            Notes,
            amendedAt);

        Cancel(reason, amendedAt);

        return replacement;
    }

    /// <summary>Puts a line on a draft order.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What is being bought.</param>
    /// <param name="quantity">How much. Positive.</param>
    /// <param name="unitCost">What one costs, in the order's currency.</param>
    /// <param name="taxCode">The tax code the line is bought under.</param>
    /// <param name="net">The line excluding tax, computed once by the caller through <c>ITaxCalculator</c>.</param>
    /// <param name="tax">The tax on the line.</param>
    /// <param name="purchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="ProcurementRuleException">The order is not a draft, or the line is malformed.</exception>
    public PurchaseOrderLine AddLine(
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money unitCost,
        string taxCode,
        Money net,
        Money tax,
        Guid? purchaseRequisitionLineId)
    {
        EnsureDraft();

        ProcurementLineRules.RequireSameCurrency(Currency, unitCost);
        ProcurementLineRules.RequireSameCurrency(Currency, net);
        ProcurementLineRules.RequireSameCurrency(Currency, tax);

        PurchaseOrderLine line = PurchaseOrderLine.For(
            TenantId, StoreId, Id, itemId, itemVariantId, description, quantity, unitCost, taxCode,
            net, tax, purchaseRequisitionLineId);

        _lines.Add(line);
        RecalculateTotals();

        return line;
    }

    /// <summary>Takes a line off a draft order.</summary>
    /// <param name="lineId">The line.</param>
    /// <returns>The line that was removed from the aggregate.</returns>
    /// <exception cref="ProcurementRuleException">The order is not a draft.</exception>
    public PurchaseOrderLine RemoveLine(Guid lineId)
    {
        EnsureDraft();

        PurchaseOrderLine line = RequireLine(lineId);
        _lines.Remove(line);
        RecalculateTotals();

        return line;
    }

    /// <summary>Approves the order internally. Lines freeze here (business rule 3).</summary>
    /// <param name="approvedByUserId">Who approved it.</param>
    /// <param name="approvedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">Not a draft, or it has no lines.</exception>
    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAt)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw ProcurementRuleException.OrderHasNoLines();
        }

        Status = PurchaseOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = approvedAt;
    }

    /// <summary>Sends the order to the supplier. Goods may now be received against it.</summary>
    /// <param name="issuedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It has not been approved.</exception>
    public void Issue(DateTimeOffset issuedAt)
    {
        if (Status is not PurchaseOrderStatus.Approved)
        {
            throw ProcurementRuleException.UnexpectedOrderStatus(Status);
        }

        Status = PurchaseOrderStatus.Issued;
        IssuedAt = issuedAt;
    }

    /// <summary>
    /// Records that a quantity arrived against one line, and moves the order's own status on.
    /// </summary>
    /// <param name="lineId">The order line.</param>
    /// <param name="quantity">The accepted quantity — what actually went into stock.</param>
    /// <param name="overReceiptTolerancePercentage">
    /// How far past the ordered quantity a delivery may go before it is refused, as a percentage
    /// (business rule 6). Zero means no over-receipt at all.
    /// </param>
    /// <exception cref="ProcurementRuleException">
    /// The order is not open for receipt, or the delivery exceeds what was ordered beyond tolerance.
    /// </exception>
    public void RecordReceipt(Guid lineId, Quantity quantity, decimal overReceiptTolerancePercentage)
    {
        if (!IsOpenForReceipt)
        {
            throw ProcurementRuleException.OrderNotOpenForReceipt(Status);
        }

        RequireLine(lineId).RecordReceived(quantity, overReceiptTolerancePercentage);

        // Partially received rather than received the moment anything arrives, and fully received only
        // when every line is satisfied. A two-line order with one line complete is not a delivered order,
        // and treating it as one is how the second half never gets chased.
        Status = IsFullyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    /// <summary>
    /// Records that a quantity arrived against one line and was turned away (business rule 7).
    /// </summary>
    /// <param name="lineId">The order line.</param>
    /// <param name="quantity">The rejected quantity. Never entered stock, never counts as received.</param>
    /// <exception cref="ProcurementRuleException">The order is not open for receipt.</exception>
    public void RecordRejection(Guid lineId, Quantity quantity)
    {
        // Deliberately not folded into RecordReceipt. A delivery can be entirely rejected — 100 cases
        // arrive and all 100 are off — and there is no accepted quantity to hang it on. Making it its own
        // transition means that case has a home rather than being a receipt of zero, which
        // RecordReceived refuses.
        if (Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed)
        {
            throw ProcurementRuleException.OrderNotOpenForReceipt(Status);
        }

        RequireLine(lineId).RecordRejected(quantity);
    }

    /// <summary>
    /// Records that a quantity was invoiced against one line, once a match has been released
    /// (business rule 14).
    /// </summary>
    /// <param name="lineId">The order line.</param>
    /// <param name="quantity">The invoiced quantity.</param>
    public void RecordInvoiced(Guid lineId, Quantity quantity)
        => RequireLine(lineId).RecordInvoiced(quantity);

    /// <summary>Closes the order — everything received and invoiced, or deliberately short-closed.</summary>
    /// <param name="closedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It was never issued, or is already terminal.</exception>
    public void Close(DateTimeOffset closedAt)
    {
        if (Status is not (PurchaseOrderStatus.Issued
            or PurchaseOrderStatus.PartiallyReceived
            or PurchaseOrderStatus.Received))
        {
            throw ProcurementRuleException.UnexpectedOrderStatus(Status);
        }

        Status = PurchaseOrderStatus.Closed;
        ClosedAt = closedAt;
    }

    /// <summary>Cancels the order, with a reason.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="cancelledAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is already closed or cancelled.</exception>
    public void Cancel(string reason, DateTimeOffset cancelledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
        {
            throw ProcurementRuleException.UnexpectedOrderStatus(Status);
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancelledAt = cancelledAt;
        CancellationReason = reason.Trim();
    }

    /// <summary>Finds a line on this order.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="ProcurementNotFoundException">No such line on this order.</exception>
    public PurchaseOrderLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new ProcurementNotFoundException("purchase order line", lineId);

    /// <summary>Refuses to proceed unless the order is still a draft.</summary>
    /// <exception cref="ProcurementRuleException">It is not a draft.</exception>
    public void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw ProcurementRuleException.UnexpectedOrderStatus(Status);
        }
    }

    private void RecalculateTotals()
    {
        Money net = Money.Zero(Currency);
        Money tax = Money.Zero(Currency);

        foreach (PurchaseOrderLine line in _lines)
        {
            net += line.Net;
            tax += line.Tax;
        }

        Net = net;
        Tax = tax;
        Gross = net + tax;
    }
}
