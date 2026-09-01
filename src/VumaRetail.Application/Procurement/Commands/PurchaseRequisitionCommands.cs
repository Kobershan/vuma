using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Commands;

/// <summary>Raises a requisition — somebody in the shop saying they need something.</summary>
/// <param name="LocationId">Where the goods are wanted, or <c>null</c>.</param>
/// <param name="RequiredBy">When they are needed by.</param>
/// <param name="Justification">Why. The thing an approver actually reads.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePurchaseRequisitionCommand(
    Guid? LocationId, DateOnly RequiredBy, string Justification) : ICommand<Guid>;

/// <summary>Rejects a malformed create-requisition command before it reaches the handler.</summary>
public sealed class CreatePurchaseRequisitionCommandValidator
    : AbstractValidator<CreatePurchaseRequisitionCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePurchaseRequisitionCommandValidator()
        => RuleFor(command => command.Justification).NotEmpty().MaximumLength(1000);
}

/// <summary>Raises the requisition, drawing its number from ADR-065's <c>REQ</c> series.</summary>
/// <param name="requisitions">Requisition insertion.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="tenant">The tenant and store the requisition belongs to.</param>
/// <param name="principal">Who is asking.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreatePurchaseRequisitionCommandHandler(
    IPurchaseRequisitionRepository requisitions,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CreatePurchaseRequisitionCommand, Guid>
{
    /// <summary>The document number series a requisition number is drawn from.</summary>
    public const string RequisitionNumberSeries = "REQ";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreatePurchaseRequisitionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid requestedBy = ProcurementActor.RequireUserId(principal);

        string number = await numbers
            .NextAsync(RequisitionNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        PurchaseRequisition created = PurchaseRequisition.Raise(
            tenant.TenantId,
            tenant.StoreId,
            number,
            requestedBy,
            command.LocationId,
            command.RequiredBy,
            command.Justification,
            clock.UtcNow);

        requisitions.Add(created);

        return created.Id;
    }
}

/// <summary>Puts a line on a draft requisition.</summary>
/// <param name="PurchaseRequisitionId">The requisition.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What it is, in the requester's words.</param>
/// <param name="Quantity">How much is needed.</param>
/// <param name="EstimatedUnitCost">What the requester thinks it costs, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddPurchaseRequisitionLineCommand(
    Guid PurchaseRequisitionId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    Quantity Quantity,
    Money? EstimatedUnitCost) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddPurchaseRequisitionLineCommandValidator
    : AbstractValidator<AddPurchaseRequisitionLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddPurchaseRequisitionLineCommandValidator()
    {
        RuleFor(command => command.PurchaseRequisitionId).NotEmpty();
        RuleFor(command => command.Description).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
    }
}

/// <summary>Adds the line.</summary>
/// <param name="requisitions">Requisition lookup.</param>
public sealed class AddPurchaseRequisitionLineCommandHandler(IPurchaseRequisitionRepository requisitions)
    : ICommandHandler<AddPurchaseRequisitionLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddPurchaseRequisitionLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseRequisition requisition = await requisitions
            .FindAsync(command.PurchaseRequisitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase requisition", command.PurchaseRequisitionId);

        PurchaseRequisitionLine created = requisition.AddLine(
            command.ItemId,
            command.ItemVariantId,
            command.Description,
            command.Quantity,
            command.EstimatedUnitCost);

        return created.Id;
    }
}

/// <summary>Sends a requisition for approval. Lines freeze.</summary>
/// <param name="PurchaseRequisitionId">The requisition.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitPurchaseRequisitionCommand(Guid PurchaseRequisitionId) : ICommand;

/// <summary>Rejects a malformed submit command before it reaches the handler.</summary>
public sealed class SubmitPurchaseRequisitionCommandValidator
    : AbstractValidator<SubmitPurchaseRequisitionCommand>
{
    /// <summary>Builds the rules.</summary>
    public SubmitPurchaseRequisitionCommandValidator()
        => RuleFor(command => command.PurchaseRequisitionId).NotEmpty();
}

/// <summary>Submits the requisition.</summary>
/// <param name="requisitions">Requisition lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class SubmitPurchaseRequisitionCommandHandler(
    IPurchaseRequisitionRepository requisitions, IClock clock)
    : ICommandHandler<SubmitPurchaseRequisitionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        SubmitPurchaseRequisitionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseRequisition requisition = await requisitions
            .FindAsync(command.PurchaseRequisitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase requisition", command.PurchaseRequisitionId);

        requisition.Submit(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Decides a submitted requisition.</summary>
/// <param name="PurchaseRequisitionId">The requisition.</param>
/// <param name="Approve">True to approve, false to reject.</param>
/// <param name="Reason">Why it was turned down. Required on a rejection.</param>
/// <remarks>
/// One command with a flag rather than two, because the two decisions are the same act by the same
/// person at the same moment and a UI shows them as one screen with two buttons. Splitting them would
/// also split the approval policy Stage 05 will eventually attach to it.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record DecidePurchaseRequisitionCommand(
    Guid PurchaseRequisitionId, bool Approve, string? Reason) : ICommand;

/// <summary>Rejects a malformed decide command before it reaches the handler.</summary>
public sealed class DecidePurchaseRequisitionCommandValidator
    : AbstractValidator<DecidePurchaseRequisitionCommand>
{
    /// <summary>Builds the rules.</summary>
    public DecidePurchaseRequisitionCommandValidator()
    {
        RuleFor(command => command.PurchaseRequisitionId).NotEmpty();

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .When(command => !command.Approve)
            .WithMessage("A rejected requisition needs a reason the requester can act on.");
    }
}

/// <summary>
/// Approves or rejects the requisition.
/// </summary>
/// <remarks>
/// <b>The approval gate is a documented no-op</b> (§7 rule 13). Stage 05's <c>IApprovalService</c> does
/// not exist, so nothing here consults a threshold policy — the state transition is real and the
/// question of <em>who</em> may approve <em>what</em> is answered by the
/// <c>procurement.requisition.approve</c> permission alone. Stage 07's journal and AP payment commands
/// made the same call. Wiring a policy on later needs no change to this command's shape.
/// </remarks>
/// <param name="requisitions">Requisition lookup.</param>
/// <param name="principal">Who is deciding.</param>
/// <param name="clock">The only source of time.</param>
public sealed class DecidePurchaseRequisitionCommandHandler(
    IPurchaseRequisitionRepository requisitions, IPrincipalAccessor principal, IClock clock)
    : ICommandHandler<DecidePurchaseRequisitionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DecidePurchaseRequisitionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid decidedBy = ProcurementActor.RequireUserId(principal);

        PurchaseRequisition requisition = await requisitions
            .FindAsync(command.PurchaseRequisitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase requisition", command.PurchaseRequisitionId);

        if (command.Approve)
        {
            requisition.Approve(decidedBy, clock.UtcNow);
        }
        else
        {
            requisition.Reject(decidedBy, command.Reason ?? "No reason given.", clock.UtcNow);
        }

        return Unit.Value;
    }
}

/// <summary>Withdraws a requisition nobody has acted on.</summary>
/// <param name="PurchaseRequisitionId">The requisition.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelPurchaseRequisitionCommand(Guid PurchaseRequisitionId) : ICommand;

/// <summary>Rejects a malformed cancel command before it reaches the handler.</summary>
public sealed class CancelPurchaseRequisitionCommandValidator
    : AbstractValidator<CancelPurchaseRequisitionCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelPurchaseRequisitionCommandValidator()
        => RuleFor(command => command.PurchaseRequisitionId).NotEmpty();
}

/// <summary>Cancels the requisition.</summary>
/// <param name="requisitions">Requisition lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CancelPurchaseRequisitionCommandHandler(
    IPurchaseRequisitionRepository requisitions, IClock clock)
    : ICommandHandler<CancelPurchaseRequisitionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        CancelPurchaseRequisitionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseRequisition requisition = await requisitions
            .FindAsync(command.PurchaseRequisitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase requisition", command.PurchaseRequisitionId);

        requisition.Cancel(clock.UtcNow);

        return Unit.Value;
    }
}
