using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>Something procurement-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>purchase order</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class ProcurementNotFoundException(string what, Guid id)
    : DomainException("PROCUREMENT_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something procurement-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class ProcurementConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>One supplier quoted twice against the same RFQ.</summary>
    /// <param name="partnerId">The supplier.</param>
    public static ProcurementConflictException DuplicateRfqResponse(Guid partnerId)
        => new(
            "PROCUREMENT_DUPLICATE_RFQ_RESPONSE",
            $"Supplier {partnerId} has already quoted against this RFQ. A revised price is a new "
            + "response on a new RFQ, not an edit — business rule 2 freezes a submitted quote.");

    /// <summary>The same supplier invoice number was matched against this order twice.</summary>
    /// <param name="invoiceNumber">The supplier's invoice number.</param>
    public static ProcurementConflictException DuplicateSupplierInvoice(string invoiceNumber)
        => new(
            "PROCUREMENT_DUPLICATE_SUPPLIER_INVOICE",
            $"Supplier invoice '{invoiceNumber}' has already been matched against this order. Matching "
            + "it twice is how one delivery gets paid for twice.");

    /// <summary>A scorecard already exists for this supplier and period.</summary>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="periodStart">The period's first day.</param>
    public static ProcurementConflictException DuplicateScorecard(Guid partnerId, DateOnly periodStart)
        => new(
            "PROCUREMENT_DUPLICATE_SCORECARD",
            $"Supplier {partnerId} already has a scorecard for the period starting {periodStart:yyyy-MM-dd}. "
            + "A snapshot is taken once (ADR-084); recomputing it would restate history.");
}

/// <summary>A procurement business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class ProcurementRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A line named neither an item nor a variant, or named both.</summary>
    public static ProcurementRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "PROCUREMENT_EXACTLY_ONE_ITEM_OR_VARIANT",
            "A procurement line identifies exactly one of an item (when it has no variants) or a "
            + "variant — never both, never neither.");

    /// <summary>A quantity that must be positive was zero or negative.</summary>
    public static ProcurementRuleException QuantityMustBePositive()
        => new("PROCUREMENT_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>A cost was negative. A supplier does not pay the shop to take goods.</summary>
    public static ProcurementRuleException CostMustNotBeNegative()
        => new("PROCUREMENT_COST_MUST_NOT_BE_NEGATIVE", "A unit cost cannot be negative.");

    /// <summary>Something was attempted that the requisition's status does not allow.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static ProcurementRuleException UnexpectedRequisitionStatus(PurchaseRequisitionStatus status)
        => new(
            "PROCUREMENT_UNEXPECTED_REQUISITION_STATUS",
            $"That is not allowed while the requisition is {status}.");

    /// <summary>A requisition was submitted with nothing on it.</summary>
    public static ProcurementRuleException RequisitionHasNoLines()
        => new(
            "PROCUREMENT_REQUISITION_HAS_NO_LINES",
            "A requisition must have at least one line before it is submitted.");

    /// <summary>Something was attempted that the RFQ's status does not allow.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static ProcurementRuleException UnexpectedRfqStatus(RfqStatus status)
        => new("PROCUREMENT_UNEXPECTED_RFQ_STATUS", $"That is not allowed while the RFQ is {status}.");

    /// <summary>An RFQ was issued with nothing on it.</summary>
    public static ProcurementRuleException RfqHasNoLines()
        => new("PROCUREMENT_RFQ_HAS_NO_LINES", "An RFQ must have at least one line before it is issued.");

    /// <summary>A quote arrived after the RFQ closed.</summary>
    /// <param name="closesAt">When it closed.</param>
    /// <param name="quotedAt">When the quote arrived.</param>
    public static ProcurementRuleException RfqAlreadyClosed(DateTimeOffset closesAt, DateTimeOffset quotedAt)
        => new(
            "PROCUREMENT_RFQ_ALREADY_CLOSED",
            $"This RFQ closed at {closesAt:O}; the quote is dated {quotedAt:O}. A late quote is accepted "
            + "by re-issuing the RFQ, not by moving the closing date after the fact.");

    /// <summary>An award was attempted on an RFQ that has already awarded one.</summary>
    public static ProcurementRuleException RfqAlreadyAwarded()
        => new(
            "PROCUREMENT_RFQ_ALREADY_AWARDED",
            "This RFQ has already awarded a response. One RFQ buys one thing from one supplier; "
            + "splitting an award across suppliers is two RFQs.");

    /// <summary>A response line quoted for something the RFQ does not ask for.</summary>
    public static ProcurementRuleException ResponseLineNotOnRfq()
        => new(
            "PROCUREMENT_RESPONSE_LINE_NOT_ON_RFQ",
            "That RFQ line is not on this RFQ. A supplier cannot quote for something nobody asked for.");

    /// <summary>A submitted response was edited.</summary>
    public static ProcurementRuleException ResponseIsFrozen()
        => new(
            "PROCUREMENT_RESPONSE_IS_FROZEN",
            "A submitted quote is frozen (business rule 2). A supplier who changes their price gives a "
            + "new quote; editing the old one destroys the record of what they originally said.");

    /// <summary>Something was attempted that the purchase order's status does not allow.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static ProcurementRuleException UnexpectedOrderStatus(PurchaseOrderStatus status)
        => new("PROCUREMENT_UNEXPECTED_ORDER_STATUS", $"That is not allowed while the order is {status}.");

    /// <summary>An order was approved or issued with nothing on it.</summary>
    public static ProcurementRuleException OrderHasNoLines()
        => new(
            "PROCUREMENT_ORDER_HAS_NO_LINES",
            "A purchase order must have at least one line before it is approved.");

    /// <summary>A line's money was denominated in a currency the order is not (business rule 4).</summary>
    /// <param name="expected">The order's currency.</param>
    /// <param name="actual">The currency the line supplied.</param>
    public static ProcurementRuleException CurrencyMismatch(string expected, string actual)
        => new(
            "PROCUREMENT_CURRENCY_MISMATCH",
            $"This order is denominated in {expected}; the line supplied {actual}. The currency is the "
            + "supplier's and is bound at creation — converting needs an explicit rate and rate date, "
            + "which is Finance's job. See PROGRESS.md §4.13 for what happens to a document that lets a "
            + "currency in through a line.");

    /// <summary>The same order line was put on one document twice.</summary>
    /// <param name="what">The document kind, for example <c>receipt</c>.</param>
    public static ProcurementRuleException DuplicateOrderLineReference(string what)
        => new(
            "PROCUREMENT_DUPLICATE_ORDER_LINE_REFERENCE",
            $"That order line is already on this {what}. Change its quantity rather than adding a second "
            + "row — two rows for one line is how a per-row check gets passed twice.");

    /// <summary>Something was attempted that the receipt's status does not allow.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static ProcurementRuleException UnexpectedReceiptStatus(GoodsReceiptStatus status)
        => new("PROCUREMENT_UNEXPECTED_RECEIPT_STATUS", $"That is not allowed while the receipt is {status}.");

    /// <summary>A receipt was completed with nothing on it.</summary>
    public static ProcurementRuleException ReceiptHasNoLines()
        => new(
            "PROCUREMENT_RECEIPT_HAS_NO_LINES",
            "A goods receipt must have at least one line before it completes.");

    /// <summary>Goods were received against an order that is not open for delivery.</summary>
    /// <param name="status">The order's status.</param>
    public static ProcurementRuleException OrderNotOpenForReceipt(PurchaseOrderStatus status)
        => new(
            "PROCUREMENT_ORDER_NOT_OPEN_FOR_RECEIPT",
            $"Goods can only be received against an issued order; this one is {status}.");

    /// <summary>A delivery exceeded what was ordered, beyond the over-receipt tolerance (business rule 6).</summary>
    /// <param name="ordered">How many were ordered.</param>
    /// <param name="alreadyReceived">How many have already arrived.</param>
    /// <param name="requested">How many this receipt claims.</param>
    /// <param name="tolerancePercentage">The tolerance that was applied, as a percentage.</param>
    public static ProcurementRuleException ReceiptExceedsOrderedQuantity(
        decimal ordered, decimal alreadyReceived, decimal requested, decimal tolerancePercentage)
        => new(
            "PROCUREMENT_RECEIPT_EXCEEDS_ORDERED",
            $"That line ordered {ordered}, of which {alreadyReceived} has already arrived; this receipt "
            + $"claims {requested}. With a {tolerancePercentage}% over-receipt tolerance that is more "
            + "than was bought. Amend the order or send the excess back — accepting it silently is how "
            + "a supplier bills for stock nobody agreed to buy.");

    /// <summary>A rejected quantity exceeded the quantity that arrived.</summary>
    public static ProcurementRuleException RejectedExceedsDelivered()
        => new(
            "PROCUREMENT_REJECTED_EXCEEDS_DELIVERED",
            "More was rejected than arrived. Rejected quantity is part of what was delivered, not extra.");

    /// <summary>A rejected quantity was recorded with no reason, or a reason with no quantity.</summary>
    public static ProcurementRuleException RejectionReasonRequired()
        => new(
            "PROCUREMENT_REJECTION_REASON_REQUIRED",
            "A rejected quantity needs a reason and a reason needs a rejected quantity — a rejection "
            + "with no reason is invisible on a supplier scorecard.");

    /// <summary>Something was attempted that the match's status does not allow.</summary>
    public static ProcurementRuleException MatchAlreadyReleased()
        => new(
            "PROCUREMENT_MATCH_ALREADY_RELEASED",
            "This match has already been released and is frozen (§7 rule 7). A correction is a supplier "
            + "credit note and a new match.");

    /// <summary>A blocked match was released.</summary>
    /// <param name="status">The verdict that blocked it.</param>
    public static ProcurementRuleException BlockedMatchCannotBeReleased(ThreeWayMatchStatus status)
        => new(
            "PROCUREMENT_BLOCKED_MATCH_CANNOT_BE_RELEASED",
            $"A {status} match is not payable (business rule 13). Resolve the variance with the supplier "
            + "and match their credit note; releasing this would pay a bill the delivery does not support.");

    /// <summary>A match was built with no lines.</summary>
    public static ProcurementRuleException MatchHasNoLines()
        => new(
            "PROCUREMENT_MATCH_HAS_NO_LINES",
            "A three-way match must compare at least one line. An invoice with no lines is not an invoice.");

    /// <summary>A tolerance was outside the range that means anything.</summary>
    public static ProcurementRuleException ToleranceOutOfRange()
        => new(
            "PROCUREMENT_TOLERANCE_OUT_OF_RANGE",
            "A tolerance percentage must be between 0 and 100, and an absolute tolerance cannot be "
            + "negative.");

    /// <summary>A scorecard period ended before it began.</summary>
    public static ProcurementRuleException ScorecardPeriodInverted()
        => new(
            "PROCUREMENT_SCORECARD_PERIOD_INVERTED",
            "The period's last day cannot fall before its first day.");

    /// <summary>A scorecard was snapshotted over a period that has not finished yet.</summary>
    /// <param name="periodEnd">The period's last day.</param>
    /// <param name="today">Today, in the tenant's terms.</param>
    public static ProcurementRuleException ScorecardPeriodNotClosed(DateOnly periodEnd, DateOnly today)
        => new(
            "PROCUREMENT_SCORECARD_PERIOD_NOT_CLOSED",
            $"That period ends {periodEnd:yyyy-MM-dd} and today is {today:yyyy-MM-dd}. A scorecard is a "
            + "snapshot over a closed period (ADR-084) — one taken mid-period says something different "
            + "every time it is read, which is not a rating.");
}
