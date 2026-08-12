using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.IntegrationTests.Workflow;

/// <summary>
/// A throwaway command standing in for "some other module's command handler calling the single
/// choke point", exactly as <c>docs/stages/STAGE-05-workflow.md</c>'s acceptance criteria describes:
/// "raise a request through <c>IApprovalService</c> from a throwaway test handler". Declared in the
/// test assembly rather than in a module, on the same pattern
/// <c>ReadOnlyCorrectnessTests.FinishSaleCommand</c> already uses for the open-session carve-out —
/// there is no real gated module yet (procurement is Stage 12), and the mechanism has to be provable
/// now. Registered into the running host via <c>ApiHarness.CreateAsync</c>'s
/// <c>configureServices</c> hook, so it runs through the real dispatcher and the real transaction.
/// </summary>
/// <param name="Module">The gated action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="SubjectEntityId">The business record being gated.</param>
/// <param name="AmountValue">The amount at hand, if any.</param>
/// <param name="Currency">The amount's currency, required when <paramref name="AmountValue"/> is set.</param>
/// <param name="Reason">Free text for a decider's context.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RaiseTestApprovalCommand(
    string Module,
    string EntityType,
    string Action,
    Guid SubjectEntityId,
    decimal? AmountValue,
    string? Currency,
    string? Reason) : ICommand<ApprovalOutcome>;

/// <summary>Raises the gated action through the single choke point.</summary>
/// <param name="approvals">The engine.</param>
public sealed class RaiseTestApprovalCommandHandler(IApprovalService approvals)
    : ICommandHandler<RaiseTestApprovalCommand, ApprovalOutcome>
{
    /// <inheritdoc />
    public Task<ApprovalOutcome> HandleAsync(
        RaiseTestApprovalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Money? amount = command.AmountValue is { } value ? new Money(value, command.Currency!) : null;

        return approvals.EvaluateAsync(
            new ApprovalContext(command.Module, command.EntityType, command.Action, command.SubjectEntityId, amount, command.Reason),
            cancellationToken);
    }
}
