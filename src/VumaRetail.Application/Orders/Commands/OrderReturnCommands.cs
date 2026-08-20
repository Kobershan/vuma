using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders.Commands;

/// <summary>Raises an open return against an order.</summary>
/// <param name="SalesOrderId">The order the goods came off.</param>
/// <param name="Reason">Why they came back.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateOrderReturnCommand(Guid SalesOrderId, string Reason) : ICommand<Guid>;

/// <summary>Rejects a malformed create-return command before it reaches the handler.</summary>
public sealed class CreateOrderReturnCommandValidator : AbstractValidator<CreateOrderReturnCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateOrderReturnCommandValidator()
    {
        RuleFor(command => command.SalesOrderId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Raises the return, drawing its number from ADR-065's <c>ORT</c> series (business rule 6).</summary>
/// <param name="returns">Return insertion.</param>
/// <param name="orders">The original order — read, never written.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="principal">Who is authorising.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateOrderReturnCommandHandler(
    ISalesOrderReturnRepository returns,
    ISalesOrderRepository orders,
    IDocumentNumberSequence numbers,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CreateOrderReturnCommand, Guid>
{
    /// <summary>The document number series a return number is drawn from — deliberately not Stage 10's <c>RTN</c>.</summary>
    public const string ReturnNumberSeries = "ORT";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateOrderReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid authorisedBy = OrdersActor.RequireUserId(principal);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        string returnNumber = await numbers.NextAsync(ReturnNumberSeries, cancellationToken).ConfigureAwait(false);

        SalesOrderReturn created = SalesOrderReturn.Raise(
            order.Id, order.TenantId, order.StoreId, order.Currency, returnNumber, command.Reason, authorisedBy, clock.UtcNow);

        returns.Add(created);

        return created.Id;
    }
}

/// <summary>Puts one of the order's fulfilled lines onto a draft return.</summary>
/// <param name="SalesOrderReturnId">The return.</param>
/// <param name="SalesOrderLineId">The original line the goods came off.</param>
/// <param name="Quantity">How much is coming back.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddOrderReturnLineCommand(Guid SalesOrderReturnId, Guid SalesOrderLineId, decimal Quantity) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddOrderReturnLineCommandValidator : AbstractValidator<AddOrderReturnLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddOrderReturnLineCommandValidator()
    {
        RuleFor(command => command.SalesOrderReturnId).NotEmpty();
        RuleFor(command => command.SalesOrderLineId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0m);
    }
}

/// <summary>
/// Adds the line, bounding it by what was actually fulfilled less what earlier return documents already
/// took (business rule 6) — read here, inside the command's transaction, because neither figure is
/// visible to the aggregate on its own (the same shape <c>AddSalesReturnLineCommandHandler</c> uses).
/// </summary>
/// <param name="returns">Return lookup, and the cumulative returned quantity.</param>
/// <param name="orders">The original order — read, never written.</param>
/// <param name="fulfilment">Reads how much of the line was actually fulfilled.</param>
public sealed class AddOrderReturnLineCommandHandler(
    ISalesOrderReturnRepository returns, ISalesOrderRepository orders, IOrderFulfilmentReader fulfilment)
    : ICommandHandler<AddOrderReturnLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddOrderReturnLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrderReturn orderReturn = await returns
            .FindAsync(command.SalesOrderReturnId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order return", command.SalesOrderReturnId);

        SalesOrder order = await orders.FindAsync(orderReturn.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", orderReturn.SalesOrderId);

        SalesOrderLine orderedLine = order.RequireLine(command.SalesOrderLineId);

        OrderLineFulfilmentSnapshot snapshot = await fulfilment
            .GetLineFulfilmentAsync(orderedLine.Id, orderedLine.RequestedQuantity.UnitOfMeasure, cancellationToken)
            .ConfigureAwait(false);

        decimal alreadyReturned = await returns
            .SumReturnedQuantityAsync(orderedLine.Id, cancellationToken)
            .ConfigureAwait(false);

        SalesOrderReturnLine created = orderReturn.AddLine(
            orderedLine, new Quantity(command.Quantity, orderedLine.RequestedQuantity.UnitOfMeasure), snapshot.FulfilledQuantity, alreadyReturned);

        return created.Id;
    }
}

/// <summary>What a completed order return hands back.</summary>
/// <param name="SalesOrderReturnId">The return.</param>
/// <param name="ReturnNumber">The credit document number.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back.</param>
/// <param name="Gross">What to hand the customer.</param>
/// <param name="StockReturnsRefused">
/// How many lines completed without putting stock back (ADR-070/073). Zero on a normal return; anything
/// else is a reconciliation the store owes itself.
/// </param>
public sealed record OrderReturnCompletionResult(
    Guid SalesOrderReturnId, string ReturnNumber, Money Net, Money Tax, Money Gross, int StockReturnsRefused);

/// <summary>Closes the return: freezes it, receives its stock back and raises the financial event.</summary>
/// <param name="SalesOrderReturnId">The return.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteOrderReturnCommand(Guid SalesOrderReturnId) : ICommand<OrderReturnCompletionResult>;

/// <summary>Rejects a malformed complete command before it reaches the handler.</summary>
public sealed class CompleteOrderReturnCommandValidator : AbstractValidator<CompleteOrderReturnCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteOrderReturnCommandValidator() => RuleFor(command => command.SalesOrderReturnId).NotEmpty();
}

/// <summary>Delegates to <see cref="IOrderReturnCompletionService"/> and reports what happened.</summary>
/// <param name="returns">Return lookup.</param>
/// <param name="completion">The steps completion always takes, in one place.</param>
public sealed class CompleteOrderReturnCommandHandler(
    ISalesOrderReturnRepository returns, IOrderReturnCompletionService completion)
    : ICommandHandler<CompleteOrderReturnCommand, OrderReturnCompletionResult>
{
    /// <inheritdoc />
    public async Task<OrderReturnCompletionResult> HandleAsync(
        CompleteOrderReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrderReturn orderReturn = await returns
            .FindAsync(command.SalesOrderReturnId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order return", command.SalesOrderReturnId);

        await completion.CompleteAsync(orderReturn, cancellationToken).ConfigureAwait(false);

        return new OrderReturnCompletionResult(
            orderReturn.Id,
            orderReturn.ReturnNumber,
            orderReturn.Net,
            orderReturn.Tax,
            orderReturn.Gross,
            orderReturn.Lines.Count(line => line.StockReturn == OrderStockReturnStatus.Refused));
    }
}
