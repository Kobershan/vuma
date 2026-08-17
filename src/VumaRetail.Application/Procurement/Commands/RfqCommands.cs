using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Commands;

/// <summary>Raises an RFQ — the buyer asking suppliers what they would charge.</summary>
/// <param name="Title">What is being sourced, in one line.</param>
/// <param name="PurchaseRequisitionId">The approved requisition it comes from, or <c>null</c>.</param>
/// <param name="ClosesAt">When quoting closes.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateRfqCommand(
    string Title, Guid? PurchaseRequisitionId, DateTimeOffset ClosesAt) : ICommand<Guid>;

/// <summary>Rejects a malformed create-RFQ command before it reaches the handler.</summary>
public sealed class CreateRfqCommandValidator : AbstractValidator<CreateRfqCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateRfqCommandValidator() => RuleFor(command => command.Title).NotEmpty().MaximumLength(256);
}

/// <summary>Raises the RFQ, drawing its number from ADR-065's <c>RFQ</c> series.</summary>
/// <param name="rfqs">RFQ insertion.</param>
/// <param name="requisitions">The requisition it is sourced from, when it names one.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="tenant">The tenant and store sourcing.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateRfqCommandHandler(
    IRfqRepository rfqs,
    IPurchaseRequisitionRepository requisitions,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<CreateRfqCommand, Guid>
{
    /// <summary>The document number series an RFQ number is drawn from.</summary>
    public const string RfqNumberSeries = "RFQ";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateRfqCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Read before writing anything: sourcing an unapproved requisition is business rule 1's whole
        // point, and finding out after the RFQ exists would leave a document nobody meant to raise.
        PurchaseRequisition? requisition = null;

        if (command.PurchaseRequisitionId is { } requisitionId)
        {
            requisition = await requisitions.FindAsync(requisitionId, cancellationToken).ConfigureAwait(false)
                ?? throw new ProcurementNotFoundException("purchase requisition", requisitionId);

            if (!requisition.IsApproved)
            {
                throw ProcurementRuleException.UnexpectedRequisitionStatus(requisition.Status);
            }
        }

        string number = await numbers.NextAsync(RfqNumberSeries, cancellationToken).ConfigureAwait(false);

        Rfq created = Rfq.Raise(
            tenant.TenantId,
            tenant.StoreId,
            number,
            command.Title,
            command.PurchaseRequisitionId,
            command.ClosesAt,
            clock.UtcNow);

        rfqs.Add(created);

        return created.Id;
    }
}

/// <summary>Puts a line on a draft RFQ.</summary>
/// <param name="RfqId">The RFQ.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What is wanted.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Specification">Any further requirement, or <c>null</c>.</param>
/// <param name="PurchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddRfqLineCommand(
    Guid RfqId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    Quantity Quantity,
    string? Specification,
    Guid? PurchaseRequisitionLineId) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddRfqLineCommandValidator : AbstractValidator<AddRfqLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddRfqLineCommandValidator()
    {
        RuleFor(command => command.RfqId).NotEmpty();
        RuleFor(command => command.Description).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.Specification).MaximumLength(1000);
    }
}

/// <summary>Adds the line, and marks the requisition line it satisfies as sourced.</summary>
/// <param name="rfqs">RFQ lookup.</param>
/// <param name="requisitions">The requisition whose line is being sourced.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AddRfqLineCommandHandler(
    IRfqRepository rfqs, IPurchaseRequisitionRepository requisitions, IClock clock)
    : ICommandHandler<AddRfqLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddRfqLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        RfqLine created = rfq.AddLine(
            command.ItemId,
            command.ItemVariantId,
            command.Description,
            command.Quantity,
            command.Specification,
            command.PurchaseRequisitionLineId);

        if (command.PurchaseRequisitionLineId is { } requisitionLineId
            && rfq.PurchaseRequisitionId is { } requisitionId)
        {
            PurchaseRequisition requisition = await requisitions
                .FindAsync(requisitionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ProcurementNotFoundException("purchase requisition", requisitionId);

            requisition.RecordLineSourced(requisitionLineId, rfq.Id, clock.UtcNow);
        }

        return created.Id;
    }
}

/// <summary>Sends the RFQ out to suppliers. Lines freeze; quoting opens.</summary>
/// <param name="RfqId">The RFQ.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record IssueRfqCommand(Guid RfqId) : ICommand;

/// <summary>Rejects a malformed issue command before it reaches the handler.</summary>
public sealed class IssueRfqCommandValidator : AbstractValidator<IssueRfqCommand>
{
    /// <summary>Builds the rules.</summary>
    public IssueRfqCommandValidator() => RuleFor(command => command.RfqId).NotEmpty();
}

/// <summary>Issues the RFQ.</summary>
/// <param name="rfqs">RFQ lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class IssueRfqCommandHandler(IRfqRepository rfqs, IClock clock)
    : ICommandHandler<IssueRfqCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(IssueRfqCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        rfq.Issue(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Records one supplier's quote against an open RFQ.</summary>
/// <param name="RfqId">The RFQ.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency they quoted in.</param>
/// <param name="QuotedAt">When the quote is dated.</param>
/// <param name="ValidUntil">How long they hold the price, or <c>null</c>.</param>
/// <param name="LeadTimeDays">How long they say delivery takes.</param>
/// <param name="Notes">Anything else they said.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordRfqResponseCommand(
    Guid RfqId,
    Guid PartnerId,
    string Currency,
    DateTimeOffset QuotedAt,
    DateTimeOffset? ValidUntil,
    int LeadTimeDays,
    string? Notes) : ICommand<Guid>;

/// <summary>Rejects a malformed record-response command before it reaches the handler.</summary>
public sealed class RecordRfqResponseCommandValidator : AbstractValidator<RecordRfqResponseCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordRfqResponseCommandValidator()
    {
        RuleFor(command => command.RfqId).NotEmpty();
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.LeadTimeDays).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Notes).MaximumLength(1000);
    }
}

/// <summary>Records the quote, refusing a supplier who is not one and a quote that is late.</summary>
/// <param name="rfqs">RFQ lookup.</param>
/// <param name="partners">The supplier — validated as a reference, never joined to (CONVENTIONS.md §2).</param>
public sealed class RecordRfqResponseCommandHandler(IRfqRepository rfqs, IPartnerRepository partners)
    : ICommandHandler<RecordRfqResponseCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        RecordRfqResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        await ProcurementPartners
            .RequireSupplierAsync(partners, command.PartnerId, cancellationToken)
            .ConfigureAwait(false);

        RfqResponse created = rfq.RecordResponse(
            command.PartnerId,
            command.Currency,
            command.QuotedAt,
            command.ValidUntil,
            command.LeadTimeDays,
            command.Notes);

        return created.Id;
    }
}

/// <summary>Puts a quoted price against one of the RFQ's lines.</summary>
/// <param name="RfqId">The RFQ.</param>
/// <param name="RfqResponseId">The supplier's response.</param>
/// <param name="RfqLineId">The line being quoted for.</param>
/// <param name="UnitCost">What they charge per unit.</param>
/// <param name="AvailableQuantity">How much they can supply, or <c>null</c> for all of it, in the RFQ line's unit.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddRfqResponseLineCommand(
    Guid RfqId,
    Guid RfqResponseId,
    Guid RfqLineId,
    Money UnitCost,
    decimal? AvailableQuantity) : ICommand<Guid>;

/// <summary>Rejects a malformed add-response-line command before it reaches the handler.</summary>
public sealed class AddRfqResponseLineCommandValidator : AbstractValidator<AddRfqResponseLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddRfqResponseLineCommandValidator()
    {
        RuleFor(command => command.RfqId).NotEmpty();
        RuleFor(command => command.RfqResponseId).NotEmpty();
        RuleFor(command => command.RfqLineId).NotEmpty();
        RuleFor(command => command.UnitCost.Amount).GreaterThanOrEqualTo(0m);
    }
}

/// <summary>Adds the quoted line.</summary>
/// <param name="rfqs">RFQ lookup.</param>
public sealed class AddRfqResponseLineCommandHandler(IRfqRepository rfqs)
    : ICommandHandler<AddRfqResponseLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddRfqResponseLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        RfqResponseLine created = rfq.AddResponseLine(
            command.RfqResponseId, command.RfqLineId, command.UnitCost, command.AvailableQuantity);

        return created.Id;
    }
}

/// <summary>Awards the RFQ to one supplier's quote. Every other quote is declined.</summary>
/// <param name="RfqId">The RFQ.</param>
/// <param name="RfqResponseId">The winning response.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AwardRfqCommand(Guid RfqId, Guid RfqResponseId) : ICommand;

/// <summary>Rejects a malformed award command before it reaches the handler.</summary>
public sealed class AwardRfqCommandValidator : AbstractValidator<AwardRfqCommand>
{
    /// <summary>Builds the rules.</summary>
    public AwardRfqCommandValidator()
    {
        RuleFor(command => command.RfqId).NotEmpty();
        RuleFor(command => command.RfqResponseId).NotEmpty();
    }
}

/// <summary>Awards the RFQ. The order is a separate, deliberate act.</summary>
/// <remarks>
/// Awarding does not raise a purchase order. It is tempting — the buyer has decided, so why not — and
/// it is wrong for two reasons: the order needs a delivery location, an expected date and tax resolved
/// per line, none of which a quote carries, and an award that silently commits money removes the last
/// point at which somebody could notice the wrong response was selected.
/// </remarks>
/// <param name="rfqs">RFQ lookup.</param>
/// <param name="principal">Who is deciding.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AwardRfqCommandHandler(IRfqRepository rfqs, IPrincipalAccessor principal, IClock clock)
    : ICommandHandler<AwardRfqCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(AwardRfqCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid awardedBy = ProcurementActor.RequireUserId(principal);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        rfq.Award(command.RfqResponseId, awardedBy, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Closes an RFQ nobody won, or withdraws one before any award.</summary>
/// <param name="RfqId">The RFQ.</param>
/// <param name="Cancel">True to withdraw it, false to close it unawarded.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CloseRfqCommand(Guid RfqId, bool Cancel) : ICommand;

/// <summary>Rejects a malformed close command before it reaches the handler.</summary>
public sealed class CloseRfqCommandValidator : AbstractValidator<CloseRfqCommand>
{
    /// <summary>Builds the rules.</summary>
    public CloseRfqCommandValidator() => RuleFor(command => command.RfqId).NotEmpty();
}

/// <summary>Closes or cancels the RFQ.</summary>
/// <param name="rfqs">RFQ lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CloseRfqCommandHandler(IRfqRepository rfqs, IClock clock)
    : ICommandHandler<CloseRfqCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CloseRfqCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rfq rfq = await rfqs.FindAsync(command.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", command.RfqId);

        if (command.Cancel)
        {
            rfq.Cancel(clock.UtcNow);
        }
        else
        {
            rfq.Close(clock.UtcNow);
        }

        return Unit.Value;
    }
}
