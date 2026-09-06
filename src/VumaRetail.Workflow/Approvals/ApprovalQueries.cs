using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Approvals;

/// <summary>The unified pending-approval inbox — every module, one list (ADR-019).</summary>
/// <param name="StoreId">Restrict to one store, or <c>null</c> for the whole tenant.</param>
public sealed record ListPendingApprovalsQuery(Guid? StoreId = null) : IQuery<IReadOnlyList<ApprovalRequestSummary>>;

/// <summary>Answers the inbox through the single choke point.</summary>
/// <param name="approvals">The engine.</param>
public sealed class ListPendingApprovalsQueryHandler(IApprovalService approvals)
    : IQueryHandler<ListPendingApprovalsQuery, IReadOnlyList<ApprovalRequestSummary>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequestSummary>> HandleAsync(
        ListPendingApprovalsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return approvals.ListPendingAsync(query.StoreId, cancellationToken);
    }
}

/// <summary>One request's full state.</summary>
/// <param name="RequestId">The request.</param>
public sealed record GetApprovalRequestQuery(Guid RequestId) : IQuery<ApprovalRequestSummary>;

/// <summary>Finds the request through the single choke point.</summary>
/// <param name="approvals">The engine.</param>
public sealed class GetApprovalRequestQueryHandler(IApprovalService approvals)
    : IQueryHandler<GetApprovalRequestQuery, ApprovalRequestSummary>
{
    /// <inheritdoc />
    public async Task<ApprovalRequestSummary> HandleAsync(
        GetApprovalRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await approvals.FindAsync(query.RequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalRequestNotFoundException(query.RequestId);
    }
}

/// <summary>Every configured policy, active or not — the admin screen that manages them.</summary>
public sealed record ListApprovalPoliciesQuery : IQuery<IReadOnlyList<ApprovalPolicySummary>>;

/// <summary>Lists every policy.</summary>
/// <param name="policies">Configured gates.</param>
public sealed class ListApprovalPoliciesQueryHandler(IApprovalPolicyRepository policies)
    : IQueryHandler<ListApprovalPoliciesQuery, IReadOnlyList<ApprovalPolicySummary>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalPolicySummary>> HandleAsync(
        ListApprovalPoliciesQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalPolicy> policyList = await policies.ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. policyList.Select(policy => new ApprovalPolicySummary(
                policy.Id,
                policy.Module,
                policy.EntityType,
                policy.Action,
                policy.ThresholdAmount,
                policy.RequiredPermission,
                policy.MinApprovals,
                policy.AllowSelfApproval,
                policy.IsActive)),
        ];
    }
}
