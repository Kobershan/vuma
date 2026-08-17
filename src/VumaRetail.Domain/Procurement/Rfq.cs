using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// The buyer asking several suppliers what they would charge. The document that turns internal demand
/// into a market price instead of whatever the last supplier happened to quote.
/// </summary>
/// <remarks>
/// <para>
/// <b>Responses are part of this aggregate, and that is the point.</b> "One RFQ awards at most one
/// response" (business rule 2) is an invariant across siblings — no single response can check it by
/// looking at itself. Keeping responses reachable only through the RFQ means the rule is enforced in
/// the one place that can see all of them, rather than by a handler remembering to look.
/// </para>
/// <para>
/// <b>A submitted quote is frozen.</b> A quote is a statement a named supplier made on a named date,
/// and the reason to keep the losing quotes is to be able to show what the winning one was compared
/// against. Editing one destroys exactly that. A supplier who wants to revise theirs gives a new quote
/// against a new RFQ — which also keeps the closing date honest.
/// </para>
/// <para>
/// Each response carries its own currency, deliberately, and the RFQ has none. Asking three suppliers
/// in three countries what they would charge is a normal thing to do; the currency becomes binding at
/// the purchase order, where business rule 4 fixes it for the whole document.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Rfq : Entity
{
    private readonly List<RfqLine> _lines = [];
    private readonly List<RfqResponse> _responses = [];

    private Rfq(
        Guid tenantId,
        Guid? storeId,
        string rfqNumber,
        string title,
        Guid? purchaseRequisitionId,
        DateTimeOffset closesAt,
        DateTimeOffset raisedAt)
        : base(tenantId, storeId)
    {
        RfqNumber = rfqNumber;
        Title = title;
        PurchaseRequisitionId = purchaseRequisitionId;
        ClosesAt = closesAt;
        RaisedAt = raisedAt;
        Status = RfqStatus.Draft;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Rfq()
    {
    }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>RFQ</c>.</summary>
    public string RfqNumber { get; private set; } = string.Empty;

    /// <summary>What is being sourced, in one line.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The requisition this came from, when it came from one.</summary>
    public Guid? PurchaseRequisitionId { get; private set; }

    /// <summary>When quoting closes. A quote dated after this is refused (business rule 2).</summary>
    public DateTimeOffset ClosesAt { get; private set; }

    /// <summary>Where the RFQ stands.</summary>
    public RfqStatus Status { get; private set; }

    /// <summary>When it was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When it went out to suppliers, or <c>null</c>.</summary>
    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>The response that won, or <c>null</c>.</summary>
    public Guid? AwardedResponseId { get; private set; }

    /// <summary>When it was awarded, or <c>null</c>.</summary>
    public DateTimeOffset? AwardedAt { get; private set; }

    /// <summary>What is being asked for.</summary>
    public IReadOnlyList<RfqLine> Lines => _lines;

    /// <summary>What the suppliers said.</summary>
    public IReadOnlyList<RfqResponse> Responses => _responses;

    /// <summary>True while lines may still be changed.</summary>
    public bool IsDraft => Status is RfqStatus.Draft;

    /// <summary>True while suppliers may still quote.</summary>
    public bool IsOpen => Status is RfqStatus.Issued;

    /// <summary>Raises a new RFQ.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store sourcing.</param>
    /// <param name="rfqNumber">The document number.</param>
    /// <param name="title">What is being sourced.</param>
    /// <param name="purchaseRequisitionId">The requisition it came from, or <c>null</c>.</param>
    /// <param name="closesAt">When quoting closes.</param>
    /// <param name="raisedAt">When, UTC.</param>
    public static Rfq Raise(
        Guid tenantId,
        Guid? storeId,
        string rfqNumber,
        string title,
        Guid? purchaseRequisitionId,
        DateTimeOffset closesAt,
        DateTimeOffset raisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rfqNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Rfq(
            tenantId, storeId, rfqNumber.Trim(), title.Trim(), purchaseRequisitionId, closesAt, raisedAt);
    }

    /// <summary>Puts a line on a draft RFQ.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What is wanted.</param>
    /// <param name="quantity">How much. Positive.</param>
    /// <param name="specification">Any further requirement — grade, pack size, delivery split.</param>
    /// <param name="purchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="ProcurementRuleException">The RFQ is not a draft, or the line is malformed.</exception>
    public RfqLine AddLine(
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        string? specification,
        Guid? purchaseRequisitionLineId)
    {
        if (!IsDraft)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        RfqLine line = RfqLine.For(
            TenantId, StoreId, Id, itemId, itemVariantId, description, quantity, specification,
            purchaseRequisitionLineId);

        _lines.Add(line);

        return line;
    }

    /// <summary>Sends the RFQ out. Lines freeze; suppliers may now quote.</summary>
    /// <param name="issuedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">Not a draft, or it has no lines.</exception>
    public void Issue(DateTimeOffset issuedAt)
    {
        if (!IsDraft)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        if (_lines.Count == 0)
        {
            throw ProcurementRuleException.RfqHasNoLines();
        }

        Status = RfqStatus.Issued;
        IssuedAt = issuedAt;
    }

    /// <summary>Records one supplier's quote against an open RFQ.</summary>
    /// <param name="partnerId">The supplier. A bare id into <c>partners</c>.</param>
    /// <param name="currency">The currency this supplier quoted in.</param>
    /// <param name="quotedAt">When the quote is dated, UTC.</param>
    /// <param name="validUntil">How long the supplier will hold the price, or <c>null</c>.</param>
    /// <param name="leadTimeDays">How long they say delivery takes.</param>
    /// <param name="notes">Anything else the supplier said.</param>
    /// <returns>The new response.</returns>
    /// <exception cref="ProcurementRuleException">The RFQ is not open, or the quote is late.</exception>
    /// <exception cref="ProcurementConflictException">That supplier has already quoted.</exception>
    public RfqResponse RecordResponse(
        Guid partnerId,
        string currency,
        DateTimeOffset quotedAt,
        DateTimeOffset? validUntil,
        int leadTimeDays,
        string? notes)
    {
        if (!IsOpen)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        if (quotedAt > ClosesAt)
        {
            throw ProcurementRuleException.RfqAlreadyClosed(ClosesAt, quotedAt);
        }

        if (_responses.Exists(response => response.PartnerId == partnerId))
        {
            throw ProcurementConflictException.DuplicateRfqResponse(partnerId);
        }

        RfqResponse created = RfqResponse.Submit(
            TenantId, StoreId, Id, partnerId, currency, quotedAt, validUntil, leadTimeDays, notes);

        _responses.Add(created);

        return created;
    }

    /// <summary>Puts a price against one of the RFQ's lines on one supplier's quote.</summary>
    /// <param name="responseId">The supplier's response.</param>
    /// <param name="rfqLineId">The line being quoted for. Must be on this RFQ.</param>
    /// <param name="unitCost">What that supplier charges per unit, in their response's currency.</param>
    /// <param name="availableQuantity">How much they can actually supply, or <c>null</c> for all of it, in the RFQ line's unit.</param>
    /// <returns>The new response line.</returns>
    /// <exception cref="ProcurementRuleException">
    /// The RFQ is not open, the response is frozen, the line is not on this RFQ, or the cost is wrong.
    /// </exception>
    public RfqResponseLine AddResponseLine(
        Guid responseId, Guid rfqLineId, Money unitCost, decimal? availableQuantity)
    {
        if (!IsOpen)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        RfqResponse response = RequireResponse(responseId);
        RfqLine line = RequireLine(rfqLineId);

        return response.AddLine(line, unitCost, availableQuantity);
    }

    /// <summary>
    /// Awards the RFQ to one supplier's quote. Every other response is declined and the RFQ closes.
    /// </summary>
    /// <param name="responseId">The winning response.</param>
    /// <param name="awardedByUserId">Who decided.</param>
    /// <param name="awardedAt">When, UTC.</param>
    /// <returns>The awarded response.</returns>
    /// <exception cref="ProcurementRuleException">The RFQ is not open, or has already awarded one.</exception>
    public RfqResponse Award(Guid responseId, Guid awardedByUserId, DateTimeOffset awardedAt)
    {
        if (AwardedResponseId is not null)
        {
            throw ProcurementRuleException.RfqAlreadyAwarded();
        }

        if (!IsOpen)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        RfqResponse winner = RequireResponse(responseId);

        // Declining the losers is not bookkeeping tidiness. Nothing else records that they were
        // considered and not chosen, and "we only ever had one quote" is a different story from
        // "we compared three" when somebody audits the purchase.
        foreach (RfqResponse other in _responses.Where(candidate => candidate.Id != responseId))
        {
            other.Decline(awardedAt);
        }

        winner.Award(awardedByUserId, awardedAt);

        Status = RfqStatus.Awarded;
        AwardedResponseId = winner.Id;
        AwardedAt = awardedAt;

        return winner;
    }

    /// <summary>Closes an RFQ nobody won — no quotes, or nothing acceptable.</summary>
    /// <param name="closedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is not open.</exception>
    public void Close(DateTimeOffset closedAt)
    {
        if (!IsOpen)
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        Status = RfqStatus.Closed;
        AwardedAt = closedAt;
    }

    /// <summary>Withdraws an RFQ before any award.</summary>
    /// <param name="cancelledAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It has already been awarded or closed.</exception>
    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (Status is not (RfqStatus.Draft or RfqStatus.Issued))
        {
            throw ProcurementRuleException.UnexpectedRfqStatus(Status);
        }

        Status = RfqStatus.Cancelled;
        AwardedAt = cancelledAt;
    }

    /// <summary>Finds a line on this RFQ.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="ProcurementNotFoundException">No such line on this RFQ.</exception>
    public RfqLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new ProcurementNotFoundException("RFQ line", lineId);

    /// <summary>Finds a response on this RFQ.</summary>
    /// <param name="responseId">The response.</param>
    /// <exception cref="ProcurementNotFoundException">No such response on this RFQ.</exception>
    public RfqResponse RequireResponse(Guid responseId)
        => _responses.Find(candidate => candidate.Id == responseId)
            ?? throw new ProcurementNotFoundException("RFQ response", responseId);
}
