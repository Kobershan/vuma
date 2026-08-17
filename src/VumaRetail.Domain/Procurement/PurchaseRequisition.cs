using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// Somebody in the shop saying they need something. The first document in the buying chain, and the
/// only one that commits nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately supplier-free.</b> A requisition names what is needed and by when; it does not name
/// who to buy it from, because at the point the demand exists nobody has been asked yet. That is also
/// what makes it the document Stage 15's forecasting can raise automatically — a replenishment
/// suggestion knows the item and the quantity and has no opinion about the supplier.
/// </para>
/// <para>
/// <b>The approval is a real state and a no-op policy.</b> §7 rule 13 says no module implements its own
/// approval logic, and Stage 05's <c>IApprovalService</c> does not exist yet. So the state machine here
/// is genuine — <see cref="Submit"/>, <see cref="Approve"/> and <see cref="Reject"/> are the transitions
/// a policy will eventually drive — while <em>what</em> requires approval, and from whom, is not decided
/// in this module. Stage 07's journal and AP payment commands made the same call for the same reason.
/// </para>
/// <para>
/// The aggregate root: lines are only reachable through the requisition, so "a line changed after
/// approval" cannot happen by writing to a child.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PurchaseRequisition : Entity
{
    private readonly List<PurchaseRequisitionLine> _lines = [];

    private PurchaseRequisition(
        Guid tenantId,
        Guid? storeId,
        string requisitionNumber,
        Guid requestedByUserId,
        Guid? locationId,
        DateOnly requiredBy,
        string justification,
        DateTimeOffset raisedAt)
        : base(tenantId, storeId)
    {
        RequisitionNumber = requisitionNumber;
        RequestedByUserId = requestedByUserId;
        LocationId = locationId;
        RequiredBy = requiredBy;
        Justification = justification;
        RaisedAt = raisedAt;
        Status = PurchaseRequisitionStatus.Draft;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PurchaseRequisition()
    {
    }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>REQ</c>.</summary>
    public string RequisitionNumber { get; private set; } = string.Empty;

    /// <summary>Who asked. A bare id into <c>identity</c>, never a foreign key.</summary>
    public Guid RequestedByUserId { get; private set; }

    /// <summary>Where the goods are wanted, when the requester said. A bare id into <c>inventory</c>.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>When the goods are needed by.</summary>
    public DateOnly RequiredBy { get; private set; }

    /// <summary>Why, in the requester's words. The thing an approver actually reads.</summary>
    public string Justification { get; private set; } = string.Empty;

    /// <summary>Where the requisition stands.</summary>
    public PurchaseRequisitionStatus Status { get; private set; }

    /// <summary>When it was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When it was submitted for approval, or <c>null</c>.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Who decided, or <c>null</c>.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>When they decided, or <c>null</c>.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Why it was turned down, when it was.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>What is being asked for.</summary>
    public IReadOnlyList<PurchaseRequisitionLine> Lines => _lines;

    /// <summary>True while lines may still be changed.</summary>
    public bool IsDraft => Status is PurchaseRequisitionStatus.Draft;

    /// <summary>True once it may be sourced.</summary>
    public bool IsApproved => Status is PurchaseRequisitionStatus.Approved;

    /// <summary>Raises a new requisition.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store that needs the goods.</param>
    /// <param name="requisitionNumber">The document number.</param>
    /// <param name="requestedByUserId">Who is asking.</param>
    /// <param name="locationId">Where the goods are wanted, or <c>null</c>.</param>
    /// <param name="requiredBy">When they are needed by.</param>
    /// <param name="justification">Why.</param>
    /// <param name="raisedAt">When, UTC.</param>
    public static PurchaseRequisition Raise(
        Guid tenantId,
        Guid? storeId,
        string requisitionNumber,
        Guid requestedByUserId,
        Guid? locationId,
        DateOnly requiredBy,
        string justification,
        DateTimeOffset raisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requisitionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);

        return new PurchaseRequisition(
            tenantId,
            storeId,
            requisitionNumber.Trim(),
            requestedByUserId,
            locationId,
            requiredBy,
            justification.Trim(),
            raisedAt);
    }

    /// <summary>Puts a line on a draft requisition.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What it is, in the requester's words.</param>
    /// <param name="quantity">How much is needed. Positive.</param>
    /// <param name="estimatedUnitCost">
    /// What the requester thinks it costs, or <c>null</c>. An estimate, never a price the shop is held
    /// to — the price is the supplier's to quote.
    /// </param>
    /// <returns>The new line.</returns>
    /// <exception cref="ProcurementRuleException">The requisition is not a draft, or the line is malformed.</exception>
    public PurchaseRequisitionLine AddLine(
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money? estimatedUnitCost)
    {
        EnsureDraft();

        PurchaseRequisitionLine line = PurchaseRequisitionLine.For(
            TenantId, StoreId, Id, itemId, itemVariantId, description, quantity, estimatedUnitCost);

        _lines.Add(line);

        return line;
    }

    /// <summary>Takes a line off a draft requisition (soft, per §7 rule 8 — the caller marks it deleted).</summary>
    /// <param name="lineId">The line.</param>
    /// <returns>The line that was removed from the aggregate.</returns>
    /// <exception cref="ProcurementRuleException">The requisition is not a draft.</exception>
    /// <exception cref="ProcurementNotFoundException">No such line.</exception>
    public PurchaseRequisitionLine RemoveLine(Guid lineId)
    {
        EnsureDraft();

        PurchaseRequisitionLine line = RequireLine(lineId);
        _lines.Remove(line);

        return line;
    }

    /// <summary>Sends the requisition for approval. Lines freeze here.</summary>
    /// <param name="submittedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">Not a draft, or it has no lines.</exception>
    public void Submit(DateTimeOffset submittedAt)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw ProcurementRuleException.RequisitionHasNoLines();
        }

        Status = PurchaseRequisitionStatus.Submitted;
        SubmittedAt = submittedAt;
    }

    /// <summary>Approves the requisition. It may now be sourced.</summary>
    /// <param name="decidedByUserId">Who approved it.</param>
    /// <param name="decidedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is not awaiting a decision.</exception>
    public void Approve(Guid decidedByUserId, DateTimeOffset decidedAt)
    {
        EnsureSubmitted();

        Status = PurchaseRequisitionStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAt = decidedAt;
        RejectionReason = null;
    }

    /// <summary>Turns the requisition down, with a reason.</summary>
    /// <param name="decidedByUserId">Who turned it down.</param>
    /// <param name="reason">Why.</param>
    /// <param name="decidedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is not awaiting a decision.</exception>
    public void Reject(Guid decidedByUserId, string reason, DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        EnsureSubmitted();

        Status = PurchaseRequisitionStatus.Rejected;
        DecidedByUserId = decidedByUserId;
        DecidedAt = decidedAt;
        RejectionReason = reason.Trim();
    }

    /// <summary>Withdraws a requisition nobody has acted on.</summary>
    /// <param name="cancelledAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It has already been decided.</exception>
    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (Status is not (PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Submitted))
        {
            throw ProcurementRuleException.UnexpectedRequisitionStatus(Status);
        }

        Status = PurchaseRequisitionStatus.Cancelled;
        DecidedAt = cancelledAt;
    }

    /// <summary>Records that a line was sourced onto a downstream document.</summary>
    /// <param name="lineId">The requisition line.</param>
    /// <param name="documentId">The RFQ or purchase order that now carries it.</param>
    /// <param name="sourcedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">The requisition is not approved.</exception>
    public void RecordLineSourced(Guid lineId, Guid documentId, DateTimeOffset sourcedAt)
    {
        if (!IsApproved)
        {
            // Business rule 1. Sourcing an unapproved requisition is buying something nobody agreed to,
            // and the whole reason this document exists is to make that impossible to do by accident.
            throw ProcurementRuleException.UnexpectedRequisitionStatus(Status);
        }

        RequireLine(lineId).RecordSourced(documentId, sourcedAt);
    }

    /// <summary>Finds a line on this requisition.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="ProcurementNotFoundException">No such line on this requisition.</exception>
    public PurchaseRequisitionLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new ProcurementNotFoundException("purchase requisition line", lineId);

    /// <summary>Refuses to proceed unless the requisition is still a draft.</summary>
    /// <exception cref="ProcurementRuleException">It is not a draft.</exception>
    public void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw ProcurementRuleException.UnexpectedRequisitionStatus(Status);
        }
    }

    private void EnsureSubmitted()
    {
        if (Status is not PurchaseRequisitionStatus.Submitted)
        {
            throw ProcurementRuleException.UnexpectedRequisitionStatus(Status);
        }
    }
}
