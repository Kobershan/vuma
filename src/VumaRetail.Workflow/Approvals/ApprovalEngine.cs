using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Identity;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Approvals;

/// <summary>
/// The sole implementation of <see cref="IApprovalService"/> (ADR-019, <c>CLAUDE.md</c> §7 rule 13).
/// </summary>
/// <remarks>
/// An architecture test fails the build if a second type anywhere in the solution implements
/// <see cref="IApprovalService"/> — proven by a deliberate violation before this was committed.
/// </remarks>
/// <param name="policies">Configured gates.</param>
/// <param name="requests">Pending and decided requests.</param>
/// <param name="decisions">The append-only decision history.</param>
/// <param name="roles">
/// Resolves what a deciding user actually holds, the same lookup Stage 02 built for
/// <c>/api/v1/me/permissions</c> — a generic <see cref="Permissions.WorkflowPermissions.ApprovalDecide"/>
/// grant gets a user into the inbox; whether they may decide any specific request is a question about
/// the policy's own required permission, asked here.
/// </param>
/// <param name="principal">Who is acting.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ApprovalEngine(
    IApprovalPolicyRepository policies,
    IApprovalRequestRepository requests,
    IApprovalDecisionRepository decisions,
    IRoleRepository roles,
    IPrincipalAccessor principal,
    ITenantContext tenant,
    IClock clock) : IApprovalService
{
    /// <inheritdoc />
    public async Task<ApprovalOutcome> EvaluateAsync(
        ApprovalContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApprovalPolicy? policy = await policies
            .FindActiveAsync(context.Module, context.EntityType, context.Action, cancellationToken)
            .ConfigureAwait(false);

        // No policy configured for this key means no gate. A module calls this unconditionally on
        // every occurrence of the action it wants gated; whether anything is actually configured to
        // gate it is this service's business, not the caller's.
        if (policy is null || !policy.Gates(context.Amount))
        {
            return ApprovalOutcome.NoGate;
        }

        ApprovalRequest request = ApprovalRequest.Raise(
            tenant.TenantId,
            context.StoreId ?? tenant.StoreId,
            policy,
            context.SubjectEntityId,
            principal.Principal,
            context.Amount,
            context.Reason,
            clock.UtcNow);

        requests.Add(request);

        return new ApprovalOutcome(ApprovalOutcomeKind.Pending, request.Id);
    }

    /// <inheritdoc />
    public async Task<ApprovalDecisionResult> DecideAsync(
        Guid requestId,
        ApprovalDecisionOutcome outcome,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        ApprovalRequest request = await FindPendingAsync(requestId, cancellationToken).ConfigureAwait(false);

        string decidedBy = principal.Principal;

        if (await decisions.HasDecidedAsync(requestId, decidedBy, cancellationToken).ConfigureAwait(false))
        {
            throw new ApproverAlreadyDecidedException(requestId);
        }

        // The generic ApprovalDecide grant gets a user into the inbox; the policy's own required
        // permission is what actually lets them act on this specific request.
        IReadOnlyCollection<string> granted = await roles
            .ListEffectivePermissionsAsync(WorkflowPrincipal.RequireUserId(decidedBy), request.StoreId, cancellationToken)
            .ConfigureAwait(false);

        if (!granted.Contains(request.RequiredPermission))
        {
            throw new ApproverLacksRequiredPermissionException(requestId, request.RequiredPermission);
        }

        DateTimeOffset now = clock.UtcNow;

        if (outcome is ApprovalDecisionOutcome.Approved)
        {
            request.RegisterApproval(decidedBy, now);
        }
        else
        {
            request.RegisterRejection(decidedBy, now);
        }

        decisions.Add(ApprovalDecisionEntry.Record(
            request.TenantId,
            request.StoreId,
            request.Id,
            decidedBy,
            outcome,
            comment,
            now));

        return new ApprovalDecisionResult(request.Id, request.Status, request.ApprovalCount, request.MinApprovals);
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        ApprovalRequest request = await FindPendingAsync(requestId, cancellationToken).ConfigureAwait(false);

        request.Cancel(clock.UtcNow);
    }

    /// <inheritdoc />
    public async Task<ApprovalRequestSummary?> FindAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        ApprovalRequest? request = await requests.FindAsync(requestId, cancellationToken).ConfigureAwait(false);

        return request is null ? null : ToSummary(request);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequestSummary>> ListPendingAsync(
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalRequest> pending = await requests
            .ListPendingAsync(storeId, cancellationToken)
            .ConfigureAwait(false);

        return [.. pending.Select(ToSummary)];
    }

    private async Task<ApprovalRequest> FindPendingAsync(Guid requestId, CancellationToken cancellationToken)
        => await requests.FindAsync(requestId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalRequestNotFoundException(requestId);

    private static ApprovalRequestSummary ToSummary(ApprovalRequest request) => new(
        request.Id,
        request.PolicyId,
        request.Module,
        request.EntityType,
        request.Action,
        request.SubjectEntityId,
        request.Amount,
        request.RequestedBy,
        request.RequestedAt,
        request.Reason,
        request.RequiredPermission,
        request.MinApprovals,
        request.ApprovalCount,
        request.RejectionCount,
        request.Status,
        request.DecidedAt);
}
