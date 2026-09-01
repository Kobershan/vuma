using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// A gate a module has configured on one of its actions (ADR-019).
/// </summary>
/// <remarks>
/// <para>
/// Data, not code. <c>CLAUDE.md</c> §7 rule 13 says no module implements its own approval logic —
/// what a module does instead is write one of these, naming the action it wants gated, and call
/// <c>IApprovalService.EvaluateAsync</c> unconditionally every time that action happens. Whether
/// anything actually needs approving is this row's business, not the caller's.
/// </para>
/// <para>
/// Identified by <see cref="Module"/>, <see cref="EntityType"/> and <see cref="Action"/> together —
/// the same three-part shape a permission key uses, because both answer "what, in this system, is
/// this about". A tenant may have at most one <em>active</em> policy per combination; the unique index
/// enforcing that is filtered to <see cref="IsActive"/> so a deactivated policy does not block a
/// replacement from being defined.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class ApprovalPolicy : Entity
{
    private ApprovalPolicy(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApprovalPolicy()
    {
    }

    /// <summary>The gated action's owning module — the first segment of the permission it requires.</summary>
    public string Module { get; private set; } = string.Empty;

    /// <summary>What the action is about — <c>purchase-order</c>, <c>price-change</c>, <c>journal</c>.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>What is being done — <c>post</c>, <c>approve</c>, <c>write-off</c>.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>The stored amount half of <see cref="ThresholdAmount"/>, or <c>null</c>.</summary>
    /// <remarks>
    /// Mapped as two flat nullable columns rather than through
    /// <c>ValueObjectMapping.HasMoney</c>'s complex-type form: EF Core 9's relational providers do not
    /// yet support an optional complex property (dotnet/efcore#31376), so a nullable <see cref="Money"/>
    /// is represented here as two ordinary nullable properties and reassembled by
    /// <see cref="ThresholdAmount"/>. Kept internal to the pair; callers use <see cref="ThresholdAmount"/>.
    /// </remarks>
    private decimal? ThresholdAmountValue { get; set; }

    /// <summary>The stored currency half of <see cref="ThresholdAmount"/>, or <c>null</c>.</summary>
    private string? ThresholdAmountCurrency { get; set; }

    /// <summary>
    /// The amount at or above which the action must be approved, or <c>null</c> to gate every
    /// occurrence regardless of amount.
    /// </summary>
    /// <remarks>
    /// A policy with no threshold is a legitimate configuration — not every gated action carries a
    /// monetary value, and some (a schema change, a bulk price import) should always be reviewed.
    /// </remarks>
    public Money? ThresholdAmount => ThresholdAmountValue is { } amount && ThresholdAmountCurrency is { } currency
        ? new Money(amount, currency)
        : null;

    /// <summary>The permission a decider must hold, in the request's store, to approve or reject it.</summary>
    public string RequiredPermission { get; private set; } = string.Empty;

    /// <summary>How many distinct approvals are required before the request is approved.</summary>
    public int MinApprovals { get; private set; } = 1;

    /// <summary>
    /// Whether the person who raised the request may also decide it.
    /// </summary>
    /// <remarks>
    /// False by default. Separation of duties is the entire point of an approval engine; a policy that
    /// wants to waive it for a specific low-risk action has to say so explicitly rather than inherit a
    /// permissive default.
    /// </remarks>
    public bool AllowSelfApproval { get; private set; }

    /// <summary>
    /// Whether this policy is in force. A deactivated policy is never evaluated again, but the
    /// requests it already produced keep the rules they were raised under.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Defines a new policy.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="module">The gated action's owning module.</param>
    /// <param name="entityType">What the action is about.</param>
    /// <param name="action">What is being done.</param>
    /// <param name="requiredPermission">The permission a decider must hold.</param>
    /// <param name="thresholdAmount">The gating amount, or <c>null</c> to gate every occurrence.</param>
    /// <param name="minApprovals">How many approvals are required. At least one.</param>
    /// <param name="allowSelfApproval">Whether the requester may decide their own request.</param>
    /// <returns>The policy.</returns>
    public static ApprovalPolicy Define(
        Guid tenantId,
        string module,
        string entityType,
        string action,
        string requiredPermission,
        Money? thresholdAmount = null,
        int minApprovals = 1,
        bool allowSelfApproval = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPermission);

        if (minApprovals < 1)
        {
            throw new InvalidApprovalPolicyException("An approval policy needs at least one required approval.");
        }

        return new ApprovalPolicy(tenantId, null)
        {
            Module = module,
            EntityType = entityType,
            Action = action,
            RequiredPermission = requiredPermission,
            ThresholdAmountValue = thresholdAmount?.Amount,
            ThresholdAmountCurrency = thresholdAmount?.Currency,
            MinApprovals = minApprovals,
            AllowSelfApproval = allowSelfApproval,
            IsActive = true,
        };
    }

    /// <summary>The <c>module.entityType.action</c> key this policy gates.</summary>
    public string Key => $"{Module}.{EntityType}.{Action}";

    /// <summary>
    /// Whether a proposed amount reaches this policy's threshold.
    /// </summary>
    /// <param name="proposedAmount">The amount at hand, or <c>null</c> when the action carries none.</param>
    /// <remarks>
    /// No threshold means every occurrence is gated. A threshold with no proposed amount to compare —
    /// an action that carries no money at all, gated by a policy that named one anyway — also gates,
    /// on the conservative reading that a configuration mismatch should ask a human rather than guess.
    /// </remarks>
    public bool Gates(Money? proposedAmount)
        => ThresholdAmount is not { } threshold || proposedAmount is not { } amount || amount >= threshold;

    /// <summary>Takes this policy out of force. Requests already raised under it are unaffected.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a deactivated policy back into force.</summary>
    public void Reactivate() => IsActive = true;
}

/// <summary>An approval policy was defined with rules that cannot be satisfied.</summary>
/// <param name="message">What was wrong.</param>
public sealed class InvalidApprovalPolicyException(string message)
    : DomainException("WORKFLOW_POLICY_INVALID", message, DomainProblemKind.Malformed);
