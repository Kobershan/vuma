using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Commands;

/// <summary>Raises a purchase order — the commitment.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency, bound for the life of the document (business rule 4).</param>
/// <param name="LocationId">Where the goods are to be delivered.</param>
/// <param name="ExpectedAt">When they are expected.</param>
/// <param name="RfqResponseId">The quote this was awarded from, or <c>null</c>.</param>
/// <param name="Notes">Delivery instructions and terms, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePurchaseOrderCommand(
    Guid PartnerId,
    string Currency,
    Guid LocationId,
    DateOnly ExpectedAt,
    Guid? RfqResponseId,
    string? Notes) : ICommand<Guid>;

/// <summary>Rejects a malformed create-order command before it reaches the handler.</summary>
public sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Notes).MaximumLength(2000);
    }
}

/// <summary>Raises the order, drawing its number from ADR-065's <c>PO</c> series.</summary>
/// <param name="orders">Order insertion.</param>
/// <param name="partners">The supplier — validated as a reference, never joined to.</param>
/// <param name="locations">The delivery location — validated the same way.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="tenant">The tenant and store buying.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreatePurchaseOrderCommandHandler(
    IPurchaseOrderRepository orders,
    IPartnerRepository partners,
    IStockLocationRepository locations,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<CreatePurchaseOrderCommand, Guid>
{
    /// <summary>The document number series a purchase order number is drawn from.</summary>
    public const string OrderNumberSeries = "PO";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreatePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await ProcurementPartners
            .RequireSupplierAsync(partners, command.PartnerId, cancellationToken)
            .ConfigureAwait(false);

        _ = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("stock location", command.LocationId);

        string number = await numbers.NextAsync(OrderNumberSeries, cancellationToken).ConfigureAwait(false);

        PurchaseOrder created = PurchaseOrder.Raise(
            tenant.TenantId,
            tenant.StoreId,
            number,
            command.PartnerId,
            command.Currency,
            command.LocationId,
            command.ExpectedAt,
            command.RfqResponseId,
            command.Notes,
            clock.UtcNow);

        orders.Add(created);

        return created.Id;
    }
}

/// <summary>Puts a line on a draft order, resolving its tax once at authoring time (ADR-075).</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What is being bought.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitCost">What one costs, in the order's currency.</param>
/// <param name="TaxCode">The tax code the line is bought under.</param>
/// <param name="PurchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddPurchaseOrderLineCommand(
    Guid PurchaseOrderId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    Quantity Quantity,
    Money UnitCost,
    string TaxCode,
    Guid? PurchaseRequisitionLineId) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddPurchaseOrderLineCommandValidator : AbstractValidator<AddPurchaseOrderLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddPurchaseOrderLineCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.Description).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.UnitCost.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.TaxCode).NotEmpty().MaximumLength(32);
    }
}

/// <summary>
/// Adds the line, extending the cost once and computing tax once through Stage 07's engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The extension is rounded once, on the extended amount.</b> <c>PROGRESS.md</c> §4.14 is this exact
/// arithmetic getting it backwards on the sell side, where rounding per unit and then multiplying
/// overcharged every weighed line by a cent in one direction. On a purchase order for 144 units at a
/// fractional case cost the same mistake is worth more.
/// </para>
/// <para>
/// <b>Tax comes from <see cref="ITaxCalculator"/> and is stored</b> (ADR-075). Whether the supplier's
/// cost is quoted inclusive or exclusive is the matched rule's decision, not the caller's — a wholesale
/// price list is usually exclusive and a cash-and-carry receipt is not, and neither the buyer nor this
/// handler should have to know which.
/// </para>
/// </remarks>
/// <param name="orders">Order lookup.</param>
/// <param name="requisitions">The requisition whose line is being sourced.</param>
/// <param name="tax">Stage 07's tax engine, through the port Application is allowed to see.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AddPurchaseOrderLineCommandHandler(
    IPurchaseOrderRepository orders,
    IPurchaseRequisitionRepository requisitions,
    ITaxCalculator tax,
    IClock clock) : ICommandHandler<AddPurchaseOrderLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddPurchaseOrderLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        order.EnsureDraft();

        Money extended = command.Quantity.Extend(command.UnitCost).RoundToCurrencyScale();

        // The order's own date, which is what ITaxCalculator means by "the document date" — not the
        // expected delivery date. An order raised today at today's rate is what the supplier is being
        // told; if the rate changes before delivery, the invoice is matched against a *stored* figure
        // and the difference shows up as a variance somebody looks at, which is exactly right.
        DateOnly documentDate = DateOnly.FromDateTime(order.RaisedAt.UtcDateTime);

        TaxCalculation calculated = await tax
            .CalculateAsync(command.TaxCode, extended, documentDate, cancellationToken)
            .ConfigureAwait(false);

        PurchaseOrderLine created = order.AddLine(
            command.ItemId,
            command.ItemVariantId,
            command.Description,
            command.Quantity,
            command.UnitCost,
            command.TaxCode,
            calculated.NetAmount,
            calculated.TaxAmount,
            command.PurchaseRequisitionLineId);

        if (command.PurchaseRequisitionLineId is { } requisitionLineId)
        {
            await MarkRequisitionLineSourcedAsync(order, requisitionLineId, cancellationToken)
                .ConfigureAwait(false);
        }

        return created.Id;
    }

    /// <summary>
    /// Marks the requisition line this order line satisfies as sourced, when the line names one.
    /// </summary>
    /// <remarks>
    /// The requisition is found through the line rather than named on the command, because the caller
    /// already told us which line and a second id is a second chance for the two to disagree.
    /// </remarks>
    private async Task MarkRequisitionLineSourcedAsync(
        PurchaseOrder order, Guid requisitionLineId, CancellationToken cancellationToken)
    {
        PurchaseRequisition? requisition = await requisitions
            .FindByLineAsync(requisitionLineId, cancellationToken)
            .ConfigureAwait(false);

        requisition?.RecordLineSourced(requisitionLineId, order.Id, clock.UtcNow);
    }
}

/// <summary>Takes a line off a draft order.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="PurchaseOrderLineId">The line.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RemovePurchaseOrderLineCommand(
    Guid PurchaseOrderId, Guid PurchaseOrderLineId) : ICommand;

/// <summary>Rejects a malformed remove-line command before it reaches the handler.</summary>
public sealed class RemovePurchaseOrderLineCommandValidator
    : AbstractValidator<RemovePurchaseOrderLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public RemovePurchaseOrderLineCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.PurchaseOrderLineId).NotEmpty();
    }
}

/// <summary>Removes the line, soft-deleting the row (§7 rule 8).</summary>
/// <param name="orders">Order lookup.</param>
public sealed class RemovePurchaseOrderLineCommandHandler(IPurchaseOrderRepository orders)
    : ICommandHandler<RemovePurchaseOrderLineCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        RemovePurchaseOrderLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        order.RemoveLine(command.PurchaseOrderLineId);

        return Unit.Value;
    }
}

/// <summary>Approves a draft order and, optionally, sends it in the same act.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Issue">True to issue it to the supplier immediately after approving.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ApprovePurchaseOrderCommand(Guid PurchaseOrderId, bool Issue) : ICommand;

/// <summary>Rejects a malformed approve command before it reaches the handler.</summary>
public sealed class ApprovePurchaseOrderCommandValidator : AbstractValidator<ApprovePurchaseOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApprovePurchaseOrderCommandValidator()
        => RuleFor(command => command.PurchaseOrderId).NotEmpty();
}

/// <summary>
/// Approves the order, freezing its lines, and issues it when asked.
/// </summary>
/// <remarks>
/// <b>The approval gate is a documented no-op</b> (§7 rule 13), the same as the requisition's. What
/// makes the commitment safe today is the <c>procurement.order.issue</c> permission and the state
/// machine; the threshold policy is Stage 05's to attach, and attaching it needs no change here.
/// </remarks>
/// <param name="orders">Order lookup.</param>
/// <param name="principal">Who is approving.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ApprovePurchaseOrderCommandHandler(
    IPurchaseOrderRepository orders, IPrincipalAccessor principal, IClock clock)
    : ICommandHandler<ApprovePurchaseOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ApprovePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid approvedBy = ProcurementActor.RequireUserId(principal);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        DateTimeOffset now = clock.UtcNow;

        order.Approve(approvedBy, now);

        if (command.Issue)
        {
            order.Issue(now);
        }

        return Unit.Value;
    }
}

/// <summary>Sends an approved order to the supplier.</summary>
/// <param name="PurchaseOrderId">The order.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record IssuePurchaseOrderCommand(Guid PurchaseOrderId) : ICommand;

/// <summary>Rejects a malformed issue command before it reaches the handler.</summary>
public sealed class IssuePurchaseOrderCommandValidator : AbstractValidator<IssuePurchaseOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public IssuePurchaseOrderCommandValidator() => RuleFor(command => command.PurchaseOrderId).NotEmpty();
}

/// <summary>Issues the order.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class IssuePurchaseOrderCommandHandler(IPurchaseOrderRepository orders, IClock clock)
    : ICommandHandler<IssuePurchaseOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        IssuePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        order.Issue(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Opens a replacement order at the next version and cancels the one it supersedes.</summary>
/// <param name="PurchaseOrderId">The order being amended.</param>
/// <param name="Reason">Why it is being superseded.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AmendPurchaseOrderCommand(Guid PurchaseOrderId, string Reason) : ICommand<Guid>;

/// <summary>Rejects a malformed amend command before it reaches the handler.</summary>
public sealed class AmendPurchaseOrderCommandValidator : AbstractValidator<AmendPurchaseOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public AmendPurchaseOrderCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Amends the order (business rule 3). The replacement comes back empty; its lines are the caller's.</summary>
/// <param name="orders">Order lookup and insertion.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AmendPurchaseOrderCommandHandler(
    IPurchaseOrderRepository orders, IDocumentNumberSequence numbers, IClock clock)
    : ICommandHandler<AmendPurchaseOrderCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AmendPurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        string number = await numbers
            .NextAsync(CreatePurchaseOrderCommandHandler.OrderNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        PurchaseOrder replacement = order.Amend(number, command.Reason, clock.UtcNow);

        orders.Add(replacement);

        return replacement.Id;
    }
}

/// <summary>Closes or cancels an order.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Cancel">True to cancel it, false to close it.</param>
/// <param name="Reason">Why it was cancelled. Required on a cancellation.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ClosePurchaseOrderCommand(
    Guid PurchaseOrderId, bool Cancel, string? Reason) : ICommand;

/// <summary>Rejects a malformed close command before it reaches the handler.</summary>
public sealed class ClosePurchaseOrderCommandValidator : AbstractValidator<ClosePurchaseOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public ClosePurchaseOrderCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .When(command => command.Cancel)
            .WithMessage("A cancelled order needs a reason — the supplier will ask.");
    }
}

/// <summary>Closes or cancels the order.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ClosePurchaseOrderCommandHandler(IPurchaseOrderRepository orders, IClock clock)
    : ICommandHandler<ClosePurchaseOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ClosePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        if (command.Cancel)
        {
            order.Cancel(command.Reason ?? "No reason given.", clock.UtcNow);
        }
        else
        {
            order.Close(clock.UtcNow);
        }

        return Unit.Value;
    }
}
