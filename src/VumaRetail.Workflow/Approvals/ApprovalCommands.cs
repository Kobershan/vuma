using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Approvals;

/// <summary>Defines a new approval policy on a gated action.</summary>
/// <remarks>
/// An ordinary write, and deliberately so — a lapsed tenant should not be reconfiguring which of its
/// own actions require approval any more than it should be performing them (see the stage's business
/// rules: no ADR-028 exemption is claimed here).
/// </remarks>
/// <param name="Module">The gated action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="RequiredPermission">The permission a decider must hold.</param>
/// <param name="ThresholdAmount">The gating amount, or <c>null</c> to gate every occurrence.</param>
/// <param name="Currency">The currency of <paramref name="ThresholdAmount"/>, required when it is set.</param>
/// <param name="MinApprovals">How many approvals are required. At least one.</param>
/// <param name="AllowSelfApproval">Whether the requester may decide their own request.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DefineApprovalPolicyCommand(
    string Module,
    string EntityType,
    string Action,
    string RequiredPermission,
    decimal? ThresholdAmount,
    string? Currency,
    int MinApprovals,
    bool AllowSelfApproval) : ICommand<Guid>;

/// <summary>Rejects a malformed policy before it reaches the handler.</summary>
public sealed class DefineApprovalPolicyCommandValidator : AbstractValidator<DefineApprovalPolicyCommand>
{
    /// <summary>Builds the rules.</summary>
    public DefineApprovalPolicyCommandValidator()
    {
        RuleFor(command => command.Module).NotEmpty().MaximumLength(64);
        RuleFor(command => command.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Action).NotEmpty().MaximumLength(64);
        RuleFor(command => command.RequiredPermission)
            .NotEmpty()
            .Must(value => PermissionKey.TryParse(value, out _))
            .WithMessage("The required permission must be a well-formed module.entity.action key.");
        RuleFor(command => command.MinApprovals).GreaterThanOrEqualTo(1);

        RuleFor(command => command.Currency)
            .NotEmpty()
            .When(command => command.ThresholdAmount is not null)
            .WithMessage("A threshold amount needs a currency.");
    }
}

/// <summary>Defines the policy.</summary>
/// <param name="policies">Configured gates.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class DefineApprovalPolicyCommandHandler(IApprovalPolicyRepository policies, ITenantContext tenant)
    : ICommandHandler<DefineApprovalPolicyCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        DefineApprovalPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await policies.FindActiveAsync(command.Module, command.EntityType, command.Action, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            throw new ApprovalPolicyAlreadyActiveException(command.Module, command.EntityType, command.Action);
        }

        Money? threshold = command.ThresholdAmount is { } amount
            ? new Money(amount, command.Currency!)
            : null;

        ApprovalPolicy policy = ApprovalPolicy.Define(
            tenant.TenantId,
            command.Module,
            command.EntityType,
            command.Action,
            command.RequiredPermission,
            threshold,
            command.MinApprovals,
            command.AllowSelfApproval);

        policies.Add(policy);

        return policy.Id;
    }
}

/// <summary>Takes a policy out of force. Requests already raised under it are unaffected.</summary>
/// <param name="PolicyId">The policy.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateApprovalPolicyCommand(Guid PolicyId) : ICommand;

/// <summary>Deactivates the policy.</summary>
/// <param name="policies">Configured gates.</param>
public sealed class DeactivateApprovalPolicyCommandHandler(IApprovalPolicyRepository policies)
    : ICommandHandler<DeactivateApprovalPolicyCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DeactivateApprovalPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ApprovalPolicy policy = await policies.FindAsync(command.PolicyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalPolicyNotFoundException(command.PolicyId);

        policy.Deactivate();

        return Unit.Value;
    }
}

/// <summary>Approves or rejects a pending request.</summary>
/// <param name="RequestId">The request.</param>
/// <param name="Outcome">What was decided.</param>
/// <param name="Comment">Free text from the decider.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DecideApprovalCommand(Guid RequestId, ApprovalDecisionOutcome Outcome, string? Comment = null)
    : ICommand<ApprovalDecisionResult>;

/// <summary>Rejects a malformed decision before it reaches the handler.</summary>
public sealed class DecideApprovalCommandValidator : AbstractValidator<DecideApprovalCommand>
{
    /// <summary>Builds the rules.</summary>
    public DecideApprovalCommandValidator()
    {
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Outcome).IsInEnum();
        RuleFor(command => command.Comment).MaximumLength(ApprovalDecisionEntry.MaxCommentLength);
    }
}

/// <summary>Records the decision through the single choke point.</summary>
/// <param name="approvals">The engine.</param>
public sealed class DecideApprovalCommandHandler(IApprovalService approvals)
    : ICommandHandler<DecideApprovalCommand, ApprovalDecisionResult>
{
    /// <inheritdoc />
    public Task<ApprovalDecisionResult> HandleAsync(
        DecideApprovalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return approvals.DecideAsync(command.RequestId, command.Outcome, command.Comment, cancellationToken);
    }
}

/// <summary>Withdraws a pending request.</summary>
/// <param name="RequestId">The request.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelApprovalRequestCommand(Guid RequestId) : ICommand;

/// <summary>Cancels the request through the single choke point.</summary>
/// <param name="approvals">The engine.</param>
public sealed class CancelApprovalRequestCommandHandler(IApprovalService approvals)
    : ICommandHandler<CancelApprovalRequestCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        CancelApprovalRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await approvals.CancelAsync(command.RequestId, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>A policy is already active for this module.entityType.action.</summary>
/// <param name="module">The module.</param>
/// <param name="entityType">The entity type.</param>
/// <param name="action">The action.</param>
public sealed class ApprovalPolicyAlreadyActiveException(string module, string entityType, string action)
    : DomainException(
        "WORKFLOW_POLICY_ALREADY_ACTIVE",
        $"An active approval policy already exists for {module}.{entityType}.{action}. Deactivate it "
        + "before defining a replacement.",
        DomainProblemKind.Conflict);

/// <summary>No policy exists with the given id, in this tenant.</summary>
/// <param name="policyId">The policy that was asked for.</param>
public sealed class ApprovalPolicyNotFoundException(Guid policyId)
    : DomainException(
        "WORKFLOW_POLICY_NOT_FOUND",
        $"No approval policy {policyId} was found.",
        DomainProblemKind.NotFound);
