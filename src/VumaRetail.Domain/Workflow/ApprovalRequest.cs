using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// One gated action waiting for, or already given, a decision.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots the <see cref="ApprovalPolicy"/>'s rules at the moment it was raised —
/// <see cref="RequiredPermission"/>, <see cref="MinApprovals"/> and <see cref="AllowSelfApproval"/> are
/// copied rather than looked up live, so a policy edited or deactivated after a request is in flight
/// cannot retroactively change what that request needs to clear.
/// </para>
/// <para>
/// <see cref="SubjectEntityId"/> is the gated business record — a purchase order, a price change, a
/// journal — referenced by id only, never by a foreign key. Workflow is a module like any other and
/// <c>CONVENTIONS.md</c> §2 forbids a cross-schema foreign key.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.ManualReview)]
public sealed class ApprovalRequest : Entity
{
    private ApprovalRequest(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApprovalRequest()
    {
    }

    /// <summary>The policy that produced this request.</summary>
    public Guid PolicyId { get; private set; }

    /// <summary>The gated action's owning module, copied from the policy for traceability.</summary>
    public string Module { get; private set; } = string.Empty;

    /// <summary>What the action is about, copied from the policy.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>What is being done, copied from the policy.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>The id of the business record this request gates, in <see cref="Module"/>'s own schema.</summary>
    public Guid SubjectEntityId { get; private set; }

    /// <summary>The stored amount half of <see cref="Amount"/>, or <c>null</c>.</summary>
    /// <remarks>
    /// Mapped as two flat nullable columns rather than through
    /// <c>ValueObjectMapping.HasMoney</c>'s complex-type form: EF Core 9's relational providers do not
    /// yet support an optional complex property (dotnet/efcore#31376), so a nullable <see cref="Money"/>
    /// is represented here as two ordinary nullable properties and reassembled by
    /// <see cref="Amount"/>. Kept internal to the pair; callers use <see cref="Amount"/>.
    /// </remarks>
    private decimal? AmountValue { get; set; }

    /// <summary>The stored currency half of <see cref="Amount"/>, or <c>null</c>.</summary>
    private string? AmountCurrency { get; set; }

    /// <summary>The amount at hand, if the gated action carries one.</summary>
    public Money? Amount => AmountValue is { } amount && AmountCurrency is { } currency
        ? new Money(amount, currency)
        : null;

    /// <summary>The principal who raised the request.</summary>
    public string RequestedBy { get; private set; } = string.Empty;

    /// <summary>When it was raised, UTC.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Free text from the requester, for the decider's context.</summary>
    public string? Reason { get; private set; }

    /// <summary>The permission a decider must hold, snapshotted from the policy.</summary>
    public string RequiredPermission { get; private set; } = string.Empty;

    /// <summary>How many approvals are required, snapshotted from the policy.</summary>
    public int MinApprovals { get; private set; } = 1;

    /// <summary>Whether the requester may decide this request, snapshotted from the policy.</summary>
    public bool AllowSelfApproval { get; private set; }

    /// <summary>How many approvals have been recorded so far.</summary>
    public int ApprovalCount { get; private set; }

    /// <summary>How many rejections have been recorded. At most one, under the veto model.</summary>
    public int RejectionCount { get; private set; }

    /// <summary>Where the request stands.</summary>
    public ApprovalRequestStatus Status { get; private set; } = ApprovalRequestStatus.Pending;

    /// <summary>When the request left <see cref="ApprovalRequestStatus.Pending"/>, if it has.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>True while the caller may still act on this request.</summary>
    public bool IsPending => Status is ApprovalRequestStatus.Pending;

    /// <summary>Raises a request against a policy's rules.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store the action is being taken in, if any.</param>
    /// <param name="policy">The policy gating the action.</param>
    /// <param name="subjectEntityId">The business record being gated.</param>
    /// <param name="requestedBy">The principal raising the request.</param>
    /// <param name="amount">The amount at hand, if any.</param>
    /// <param name="reason">Free text for the decider's context.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <returns>The request, pending.</returns>
    public static ApprovalRequest Raise(
        Guid tenantId,
        Guid? storeId,
        ApprovalPolicy policy,
        Guid subjectEntityId,
        string requestedBy,
        Money? amount,
        string? reason,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        return new ApprovalRequest(tenantId, storeId)
        {
            PolicyId = policy.Id,
            Module = policy.Module,
            EntityType = policy.EntityType,
            Action = policy.Action,
            SubjectEntityId = subjectEntityId,
            AmountValue = amount?.Amount,
            AmountCurrency = amount?.Currency,
            RequestedBy = requestedBy,
            RequestedAt = now,
            Reason = reason,
            RequiredPermission = policy.RequiredPermission,
            MinApprovals = policy.MinApprovals,
            AllowSelfApproval = policy.AllowSelfApproval,
            Status = ApprovalRequestStatus.Pending,
        };
    }

    /// <summary>
    /// Records an approval. Moves the request to <see cref="ApprovalRequestStatus.Approved"/> once
    /// <see cref="MinApprovals"/> is reached.
    /// </summary>
    /// <param name="decidedBy">The approving principal.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <exception cref="ApprovalAlreadyDecidedException">The request is no longer pending.</exception>
    /// <exception cref="SelfApprovalNotAllowedException">
    /// The decider is the requester and the policy does not allow self-approval.
    /// </exception>
    public void RegisterApproval(string decidedBy, DateTimeOffset now)
    {
        GuardCanDecide(decidedBy);

        ApprovalCount++;

        if (ApprovalCount >= MinApprovals)
        {
            Status = ApprovalRequestStatus.Approved;
            DecidedAt = now;
        }
    }

    /// <summary>
    /// Records a rejection. A single rejection ends the request outright — the veto model, and the
    /// conservative default for a separation-of-duties control.
    /// </summary>
    /// <param name="decidedBy">The rejecting principal.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <exception cref="ApprovalAlreadyDecidedException">The request is no longer pending.</exception>
    /// <exception cref="SelfApprovalNotAllowedException">
    /// The decider is the requester and the policy does not allow self-approval.
    /// </exception>
    public void RegisterRejection(string decidedBy, DateTimeOffset now)
    {
        GuardCanDecide(decidedBy);

        RejectionCount++;
        Status = ApprovalRequestStatus.Rejected;
        DecidedAt = now;
    }

    /// <summary>Withdraws a request that has not yet been decided.</summary>
    /// <param name="now">The instant, UTC.</param>
    /// <exception cref="ApprovalAlreadyDecidedException">The request is no longer pending.</exception>
    public void Cancel(DateTimeOffset now)
    {
        if (!IsPending)
        {
            throw new ApprovalAlreadyDecidedException(Id, Status);
        }

        Status = ApprovalRequestStatus.Cancelled;
        DecidedAt = now;
    }

    private void GuardCanDecide(string decidedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        if (!IsPending)
        {
            throw new ApprovalAlreadyDecidedException(Id, Status);
        }

        if (!AllowSelfApproval && string.Equals(decidedBy, RequestedBy, StringComparison.Ordinal))
        {
            throw new SelfApprovalNotAllowedException(Id);
        }
    }
}

/// <summary>A decision was attempted on a request that is no longer pending.</summary>
/// <param name="requestId">The request.</param>
/// <param name="status">Where it actually stands.</param>
public sealed class ApprovalAlreadyDecidedException(Guid requestId, ApprovalRequestStatus status)
    : DomainException(
        "WORKFLOW_APPROVAL_ALREADY_DECIDED",
        $"Approval request {requestId} is already {status} and cannot be decided again.",
        DomainProblemKind.Conflict);

/// <summary>The requester tried to decide their own request, and the policy does not allow it.</summary>
/// <param name="requestId">The request.</param>
public sealed class SelfApprovalNotAllowedException(Guid requestId)
    : DomainException(
        "WORKFLOW_SELF_APPROVAL_NOT_ALLOWED",
        $"The person who raised approval request {requestId} may not also decide it.",
        DomainProblemKind.Rule);
