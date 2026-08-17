using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Commands;

/// <summary>Opens a goods receipt against an issued order.</summary>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="DeliveryNoteNumber">The supplier's own delivery-note number, or <c>null</c>.</param>
/// <param name="ReceivedAt">
/// When the goods actually arrived, or <c>null</c> for now. Supplied because a delivery checked in the
/// next morning is still yesterday's delivery, and business rule 16 measures the supplier on when the
/// truck came — not on when somebody got to the paperwork.
/// </param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateGoodsReceiptCommand(
    Guid PurchaseOrderId, string? DeliveryNoteNumber, DateTimeOffset? ReceivedAt) : ICommand<Guid>;

/// <summary>Rejects a malformed create-receipt command before it reaches the handler.</summary>
public sealed class CreateGoodsReceiptCommandValidator : AbstractValidator<CreateGoodsReceiptCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateGoodsReceiptCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.DeliveryNoteNumber).MaximumLength(64);
    }
}

/// <summary>Opens the receipt, drawing its number from ADR-065's <c>GRN</c> series.</summary>
/// <param name="receipts">Receipt insertion.</param>
/// <param name="orders">The order — read for its supplier, location and currency.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="principal">Who is checking the delivery in.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateGoodsReceiptCommandHandler(
    IGoodsReceiptRepository receipts,
    IPurchaseOrderRepository orders,
    IDocumentNumberSequence numbers,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CreateGoodsReceiptCommand, Guid>
{
    /// <summary>The document number series a goods receipt number is drawn from.</summary>
    public const string ReceiptNumberSeries = "GRN";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreateGoodsReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid receivedBy = ProcurementActor.RequireUserId(principal);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        string number = await numbers.NextAsync(ReceiptNumberSeries, cancellationToken).ConfigureAwait(false);

        GoodsReceipt created = GoodsReceipt.Open(
            order, number, command.DeliveryNoteNumber, receivedBy, command.ReceivedAt ?? clock.UtcNow);

        receipts.Add(created);

        return created.Id;
    }
}

/// <summary>Records what arrived against one of the order's lines.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
/// <param name="PurchaseOrderLineId">The order line the goods came against.</param>
/// <param name="AcceptedQuantity">How much was accepted into stock.</param>
/// <param name="RejectedQuantity">How much was turned away, or <c>null</c>.</param>
/// <param name="RejectionReason">Why, when anything was rejected.</param>
/// <param name="Note">Anything the receiver wants on the record.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddGoodsReceiptLineCommand(
    Guid GoodsReceiptId,
    Guid PurchaseOrderLineId,
    Quantity AcceptedQuantity,
    Quantity? RejectedQuantity,
    GoodsRejectionReason RejectionReason,
    string? Note) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddGoodsReceiptLineCommandValidator : AbstractValidator<AddGoodsReceiptLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddGoodsReceiptLineCommandValidator()
    {
        RuleFor(command => command.GoodsReceiptId).NotEmpty();
        RuleFor(command => command.PurchaseOrderLineId).NotEmpty();
        RuleFor(command => command.AcceptedQuantity.Value).GreaterThan(0m);
        RuleFor(command => command.RejectionReason).IsInEnum();
        RuleFor(command => command.Note).MaximumLength(500);
    }
}

/// <summary>Adds the line, snapshotting the order line's cost onto it.</summary>
/// <param name="receipts">Receipt lookup.</param>
/// <param name="orders">The order — read for the line's cost and description.</param>
public sealed class AddGoodsReceiptLineCommandHandler(
    IGoodsReceiptRepository receipts, IPurchaseOrderRepository orders)
    : ICommandHandler<AddGoodsReceiptLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddGoodsReceiptLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        GoodsReceipt receipt = await receipts
            .FindAsync(command.GoodsReceiptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("goods receipt", command.GoodsReceiptId);

        receipt.EnsureDraft();

        PurchaseOrder order = await orders
            .FindAsync(receipt.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", receipt.PurchaseOrderId);

        PurchaseOrderLine orderLine = order.RequireLine(command.PurchaseOrderLineId);

        GoodsReceiptLine created = receipt.AddLine(
            orderLine,
            command.AcceptedQuantity,
            command.RejectedQuantity,
            command.RejectionReason,
            command.Note);

        return created.Id;
    }
}

/// <summary>What a completed receipt hands back to the goods-in desk.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
/// <param name="ReceiptNumber">Its GRN number.</param>
/// <param name="ReceivedValue">What was accepted, at the order's costs.</param>
/// <param name="OrderStatus">Where the order stands now — the answer to "is anything still to come?".</param>
/// <param name="StockPostingsRefused">
/// How many lines completed without their stock movement being posted (ADR-073). Zero on a normal
/// receipt; anything else is a reconciliation the store owes itself.
/// </param>
public sealed record GoodsReceiptCompletionResult(
    Guid GoodsReceiptId,
    string ReceiptNumber,
    Money ReceivedValue,
    PurchaseOrderStatus OrderStatus,
    int StockPostingsRefused);

/// <summary>Completes a receipt: freezes it, posts its stock, advances its order.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteGoodsReceiptCommand(Guid GoodsReceiptId)
    : ICommand<GoodsReceiptCompletionResult>;

/// <summary>Rejects a malformed complete command before it reaches the handler.</summary>
public sealed class CompleteGoodsReceiptCommandValidator : AbstractValidator<CompleteGoodsReceiptCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteGoodsReceiptCommandValidator() => RuleFor(command => command.GoodsReceiptId).NotEmpty();
}

/// <summary>Delegates to <see cref="IGoodsReceiptCompletionService"/> and reports what happened.</summary>
/// <param name="receipts">Receipt lookup.</param>
/// <param name="orders">The order the receipt advances.</param>
/// <param name="completion">The three steps completion always takes, in one place.</param>
public sealed class CompleteGoodsReceiptCommandHandler(
    IGoodsReceiptRepository receipts,
    IPurchaseOrderRepository orders,
    IGoodsReceiptCompletionService completion)
    : ICommandHandler<CompleteGoodsReceiptCommand, GoodsReceiptCompletionResult>
{
    /// <inheritdoc />
    public async Task<GoodsReceiptCompletionResult> HandleAsync(
        CompleteGoodsReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        GoodsReceipt receipt = await receipts
            .FindAsync(command.GoodsReceiptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("goods receipt", command.GoodsReceiptId);

        PurchaseOrder order = await orders
            .FindAsync(receipt.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", receipt.PurchaseOrderId);

        await completion.CompleteAsync(receipt, order, cancellationToken).ConfigureAwait(false);

        return new GoodsReceiptCompletionResult(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.ReceivedValue,
            order.Status,
            receipt.StockPostingsRefused);
    }
}

/// <summary>Abandons a draft receipt. Nothing moved.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelGoodsReceiptCommand(Guid GoodsReceiptId) : ICommand;

/// <summary>Rejects a malformed cancel command before it reaches the handler.</summary>
public sealed class CancelGoodsReceiptCommandValidator : AbstractValidator<CancelGoodsReceiptCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelGoodsReceiptCommandValidator() => RuleFor(command => command.GoodsReceiptId).NotEmpty();
}

/// <summary>Cancels the receipt.</summary>
/// <param name="receipts">Receipt lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CancelGoodsReceiptCommandHandler(IGoodsReceiptRepository receipts, IClock clock)
    : ICommandHandler<CancelGoodsReceiptCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        CancelGoodsReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        GoodsReceipt receipt = await receipts
            .FindAsync(command.GoodsReceiptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("goods receipt", command.GoodsReceiptId);

        receipt.Cancel(clock.UtcNow);

        return Unit.Value;
    }
}
