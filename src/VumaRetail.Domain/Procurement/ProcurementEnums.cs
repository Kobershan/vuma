namespace VumaRetail.Domain.Procurement;

/// <summary>Where a <see cref="PurchaseRequisition"/> stands.</summary>
/// <remarks>
/// A requisition has no <c>Issued</c> state because it is never sent to anybody. It is internal demand,
/// and the only questions it answers are "was this asked for" and "did somebody senior agree".
/// </remarks>
public enum PurchaseRequisitionStatus
{
    /// <summary>Being written. Lines may be added, changed and removed.</summary>
    Draft = 0,

    /// <summary>Submitted for approval. Frozen while somebody looks at it.</summary>
    Submitted = 1,

    /// <summary>Approved. May now be sourced — converted to an RFQ or straight to an order.</summary>
    Approved = 2,

    /// <summary>Turned down, with a reason. Terminal.</summary>
    Rejected = 3,

    /// <summary>Withdrawn by the person who raised it before anybody acted on it. Terminal.</summary>
    Cancelled = 4,
}

/// <summary>Where an <see cref="Rfq"/> stands.</summary>
public enum RfqStatus
{
    /// <summary>Being written. Lines may be added and changed; no supplier has seen it.</summary>
    Draft = 0,

    /// <summary>Out with suppliers. Responses may be submitted until it closes.</summary>
    Issued = 1,

    /// <summary>A response was awarded. No further responses; the order is the next document.</summary>
    Awarded = 2,

    /// <summary>Closed without an award — nobody quoted, or nothing was acceptable. Terminal.</summary>
    Closed = 3,

    /// <summary>Withdrawn before any award. Terminal.</summary>
    Cancelled = 4,
}

/// <summary>Where one supplier's quote stands.</summary>
public enum RfqResponseStatus
{
    /// <summary>Received and frozen. Business rule 2 — a supplier changing their price submits a new one.</summary>
    Submitted = 0,

    /// <summary>Chosen. Exactly one response per RFQ may reach this state.</summary>
    Awarded = 1,

    /// <summary>Not chosen. Kept, because "who else quoted, and what did they say" is the audit.</summary>
    Declined = 2,
}

/// <summary>Where a <see cref="PurchaseOrder"/> stands.</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Being written. Lines may be added, changed and removed.</summary>
    Draft = 0,

    /// <summary>Approved internally, not yet sent. Lines are frozen (business rule 3).</summary>
    Approved = 1,

    /// <summary>Sent to the supplier. This is the commitment; goods may now be received against it.</summary>
    Issued = 2,

    /// <summary>Some but not all of what was ordered has arrived.</summary>
    PartiallyReceived = 3,

    /// <summary>Everything ordered has been received.</summary>
    Received = 4,

    /// <summary>Received and invoiced, or short-closed deliberately. Terminal.</summary>
    Closed = 5,

    /// <summary>Withdrawn, or superseded by an amendment. Terminal.</summary>
    Cancelled = 6,
}

/// <summary>Where a <see cref="GoodsReceipt"/> stands.</summary>
public enum GoodsReceiptStatus
{
    /// <summary>Being checked in off the truck. Lines may still be added.</summary>
    Draft = 0,

    /// <summary>Completed: stock moved, the order advanced, the document frozen.</summary>
    Completed = 1,

    /// <summary>Abandoned before anything moved. Terminal.</summary>
    Cancelled = 2,
}

/// <summary>What happened when a <see cref="GoodsReceiptLine"/>'s stock was posted.</summary>
/// <remarks>
/// ADR-073's shape again, for goods arriving. The delivery truck has gone; a ledger that refuses the
/// receipt must not make the goods on the dock disappear from the paperwork too.
/// </remarks>
public enum GoodsReceiptPostingStatus
{
    /// <summary>On the document but not yet posted — the receipt has not completed.</summary>
    Pending = 0,

    /// <summary>In stock. <see cref="GoodsReceiptLine.StockLedgerEntryId"/> names the ledger row.</summary>
    Posted = 1,

    /// <summary>The ledger refused it. The goods are physically here; this row is the reconciliation queue.</summary>
    Refused = 2,
}

/// <summary>Why part of a delivery was turned away.</summary>
/// <remarks>
/// A small closed set rather than free text, because the whole point of recording a rejection is that
/// ninety days of them add up to a supplier scorecard, and free text does not add up.
/// </remarks>
public enum GoodsRejectionReason
{
    /// <summary>Nothing was rejected.</summary>
    None = 0,

    /// <summary>Arrived broken.</summary>
    Damaged = 1,

    /// <summary>Past, or too close to, its date.</summary>
    Expired = 2,

    /// <summary>Not what was ordered.</summary>
    WrongItem = 3,

    /// <summary>The right thing, but not to the standard specified.</summary>
    QualityFailure = 4,

    /// <summary>More than was ordered, beyond what the tolerance allows.</summary>
    OverDelivery = 5,
}

/// <summary>The verdict of a three-way match, for a document or for one of its lines.</summary>
public enum ThreeWayMatchStatus
{
    /// <summary>Ordered, received and invoiced agree exactly.</summary>
    Matched = 0,

    /// <summary>They disagree, but inside the tenant's tolerance. Passes, and the variance is recorded.</summary>
    MatchedWithinTolerance = 1,

    /// <summary>They disagree beyond tolerance. Nobody pays this until it is resolved (business rule 13).</summary>
    Blocked = 2,
}

/// <summary>Which of the three comparisons a blocked match failed.</summary>
[Flags]
public enum ThreeWayMatchVarianceKind
{
    /// <summary>No variance.</summary>
    None = 0,

    /// <summary>The supplier invoiced for more than was received (business rule 11).</summary>
    Quantity = 1,

    /// <summary>The supplier charged a different unit cost than was ordered (business rule 12).</summary>
    Price = 2,

    /// <summary>The invoice names a line the order does not have.</summary>
    UnknownLine = 4,
}
