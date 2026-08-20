using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Orders.Commands;

/// <summary>Raises a new draft order.</summary>
/// <param name="PartnerId">The customer, or <c>null</c> for a walk-in (business rule 10).</param>
/// <param name="Channel">Where it was taken.</param>
/// <param name="FulfilmentType">Delivery or click &amp; collect.</param>
/// <param name="FulfillingLocationId">The Stage 08 location this order ships from.</param>
/// <param name="DeliveryLine1">Street number and name, when <paramref name="FulfilmentType"/> is <see cref="OrderFulfilmentType.Delivery"/>.</param>
/// <param name="DeliveryLine2">A second address line, or <c>null</c>.</param>
/// <param name="DeliveryCity">The city or town, when delivering.</param>
/// <param name="DeliveryRegion">Province, state or region, or <c>null</c>.</param>
/// <param name="DeliveryPostalCode">The postal or ZIP code, or <c>null</c>.</param>
/// <param name="DeliveryCountryCode">ISO 3166-1 alpha-2, when delivering.</param>
/// <param name="Currency">The order's currency.</param>
/// <param name="RequestedFulfilmentDate">When the customer asked to have it, if they gave a date.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateOrderCommand(
    Guid? PartnerId,
    SalesChannel Channel,
    OrderFulfilmentType FulfilmentType,
    Guid FulfillingLocationId,
    string? DeliveryLine1,
    string? DeliveryLine2,
    string? DeliveryCity,
    string? DeliveryRegion,
    string? DeliveryPostalCode,
    string? DeliveryCountryCode,
    string Currency,
    DateTimeOffset? RequestedFulfilmentDate) : ICommand<Guid>;

/// <summary>Rejects a malformed create-order command before it reaches the handler.</summary>
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.FulfillingLocationId).NotEmpty();
        RuleFor(command => command.Channel).IsInEnum();
        RuleFor(command => command.FulfilmentType).IsInEnum();
        RuleFor(command => command.Currency).NotEmpty().Length(3);

        RuleFor(command => command.DeliveryLine1).NotEmpty()
            .When(command => command.FulfilmentType == OrderFulfilmentType.Delivery);
        RuleFor(command => command.DeliveryCity).NotEmpty()
            .When(command => command.FulfilmentType == OrderFulfilmentType.Delivery);
        RuleFor(command => command.DeliveryCountryCode).NotEmpty().Length(2)
            .When(command => command.FulfilmentType == OrderFulfilmentType.Delivery);
    }
}

/// <summary>Raises the order, drawing its number from ADR-065's <c>ORD</c> series.</summary>
/// <param name="orders">Order insertion.</param>
/// <param name="locations">Stage 08 location lookup — the tenant/store an order is stamped with.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateOrderCommandHandler(
    ISalesOrderRepository orders,
    IStockLocationRepository locations,
    IDocumentNumberSequence numbers,
    IClock clock) : ICommandHandler<CreateOrderCommand, Guid>
{
    /// <summary>The document number series an order number is drawn from.</summary>
    public const string OrderNumberSeries = "ORD";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations
            .FindAsync(command.FulfillingLocationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.FulfillingLocationId);

        Address? deliveryAddress = command.FulfilmentType == OrderFulfilmentType.Delivery
            ? Address.Create(
                command.DeliveryLine1 ?? string.Empty,
                command.DeliveryCity ?? string.Empty,
                command.DeliveryCountryCode ?? string.Empty,
                command.DeliveryLine2,
                command.DeliveryRegion,
                command.DeliveryPostalCode)
            : null;

        string orderNumber = await numbers.NextAsync(OrderNumberSeries, cancellationToken).ConfigureAwait(false);

        SalesOrder order = SalesOrder.Create(
            location.TenantId,
            location.StoreId,
            orderNumber,
            command.PartnerId,
            command.Channel,
            command.FulfilmentType,
            location.Id,
            deliveryAddress,
            command.Currency,
            clock.UtcNow,
            command.RequestedFulfilmentDate);

        orders.Add(order);

        return order.Id;
    }
}

/// <summary>Adds a demand line while the order is still a draft.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="RequestedQuantity">How much is requested.</param>
/// <param name="UnitOfMeasure">The unit the quantity is expressed in — must match the item's own.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddOrderLineCommand(
    Guid SalesOrderId, Guid? ItemId, Guid? ItemVariantId, decimal RequestedQuantity, string UnitOfMeasure) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddOrderLineCommandValidator : AbstractValidator<AddOrderLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddOrderLineCommandValidator()
    {
        RuleFor(command => command.SalesOrderId).NotEmpty();
        RuleFor(command => command.RequestedQuantity).GreaterThan(0m);
        RuleFor(command => command.UnitOfMeasure).NotEmpty().MaximumLength(16);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(AddOrderLineCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Resolves the item and adds the line, unpriced — <see cref="ConfirmOrderCommand"/> prices it.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="catalog">Resolves the item's unit of measure.</param>
public sealed class AddOrderLineCommandHandler(ISalesOrderRepository orders, ISellableItemResolver catalog)
    : ICommandHandler<AddOrderLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddOrderLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        SellableItem item = await catalog
            .ResolveAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(item.UnitOfMeasureCode, command.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(item.UnitOfMeasureCode, command.UnitOfMeasure);
        }

        SalesOrderLine line = order.AddLine(
            item.ItemId, item.ItemVariantId, new Quantity(command.RequestedQuantity, command.UnitOfMeasure));

        return line.Id;
    }
}

/// <summary>
/// Prices every line and accepts the order, then attempts to allocate whatever fits today (business
/// rules 1, 2 and 9).
/// </summary>
/// <param name="SalesOrderId">The order.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConfirmOrderCommand(Guid SalesOrderId) : ICommand;

/// <summary>Rejects a malformed confirm command before it reaches the handler.</summary>
public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConfirmOrderCommandValidator() => RuleFor(command => command.SalesOrderId).NotEmpty();
}

/// <summary>
/// Resolves each line's price through Stage 10's <see cref="IPriceResolver"/> and Stage 07's
/// <see cref="ITaxCalculator"/>, confirms the order, then attempts allocation per line through
/// <see cref="IOrderFulfilmentReader"/> and <see cref="OrderAllocation"/> — the unchanged
/// <c>AddPickTaskCommand</c>/<c>ReleasePickWaveCommand</c> seam, called in-process rather than nested
/// through the dispatcher.
/// </summary>
/// <param name="orders">Order lookup.</param>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="waves">Wave and task insertion — Stage 13's own repository.</param>
/// <param name="binStocks">The allocator's candidate pool.</param>
/// <param name="allocator">Decides which bin(s) satisfy a line's demand.</param>
/// <param name="fulfilment">Reads available-to-promise.</param>
/// <param name="catalog">Resolves an item's tax class.</param>
/// <param name="priceResolver">Stage 10's price resolution.</param>
/// <param name="tax">Stage 07's tax rules engine.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ConfirmOrderCommandHandler(
    ISalesOrderRepository orders,
    IStockLocationRepository locations,
    IPickWaveRepository waves,
    IBinStockRepository binStocks,
    IPickAllocationStrategy allocator,
    IOrderFulfilmentReader fulfilment,
    ISellableItemResolver catalog,
    IPriceResolver priceResolver,
    ITaxCalculator tax,
    IClock clock) : ICommandHandler<ConfirmOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        StockLocation location = await locations
            .FindAsync(order.FulfillingLocationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", order.FulfillingLocationId);

        DateTimeOffset now = clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(now.UtcDateTime);

        foreach (SalesOrderLine line in order.Lines)
        {
            await PriceLineAsync(order, line, today, nowTime, cancellationToken).ConfigureAwait(false);
        }

        order.Confirm(now);

        await AllocateAsync(order, location, now, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task PriceLineAsync(
        SalesOrder order, SalesOrderLine line, DateOnly today, TimeOnly nowTime, CancellationToken cancellationToken)
    {
        SellableItem item = await catalog
            .ResolveAsync(line.ItemId, line.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(item.UnitOfMeasureCode, line.RequestedQuantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(item.UnitOfMeasureCode, line.RequestedQuantity.UnitOfMeasure);
        }

        PriceResolution resolution = await priceResolver
            .ResolveAsync(
                new PriceResolutionRequest(
                    line.ItemId, line.ItemVariantId, CategoryCode: null, line.RequestedQuantity.Value,
                    order.StoreId, today, nowTime, order.Currency),
                cancellationToken)
            .ConfigureAwait(false);

        TaxCalculation calculation = await tax
            .CalculateAsync(item.TaxClassCode, resolution.NetPayable, today, cancellationToken)
            .ConfigureAwait(false);

        line.ApplyPricing(
            resolution.UnitPrice, resolution.DiscountAmount, calculation.TaxAmount, resolution.PriceListId,
            resolution.Explanation);
    }

    private async Task AllocateAsync(SalesOrder order, StockLocation location, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PickWave? wave = null;

        foreach (SalesOrderLine line in order.Lines)
        {
            Quantity available = await fulfilment
                .GetAvailableToPromiseAsync(
                    location.Id, line.ItemId, line.ItemVariantId, line.RequestedQuantity.UnitOfMeasure, cancellationToken)
                .ConfigureAwait(false);

            Quantity attempt = available < line.RequestedQuantity ? available : line.RequestedQuantity;

            (wave, Quantity allocated) = await OrderAllocation
                .AttemptAsync(waves, binStocks, allocator, line, location, attempt, wave, cancellationToken)
                .ConfigureAwait(false);

            Quantity backordered = line.RequestedQuantity - allocated;
            line.RecordAllocationOutcome(backordered.IsNegative ? Quantity.Zero(line.RequestedQuantity.UnitOfMeasure) : backordered);
        }

        wave?.Release(now);

        order.RecomputeStatus();
    }
}

/// <summary>Result of one reattempt pass, for the caller to report.</summary>
/// <param name="OrdersReallocated">How many backordered orders had at least one line allocated further.</param>
/// <param name="LinesReallocated">How many individual lines had at least one line allocated further.</param>
public sealed record ReattemptBackorderedAllocationsResult(int OrdersReallocated, int LinesReallocated);

/// <summary>
/// Reattempts allocation for every backordered line across every order, oldest <c>OrderDate</c> first
/// (business rule 3's fairness queue). Nothing in this codebase triggers this automatically — it is an
/// explicit call, by an operator today or a Stage 15 scheduled job later.
/// </summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReattemptBackorderedAllocationsCommand : ICommand<ReattemptBackorderedAllocationsResult>;

/// <summary>Walks every backordered order, oldest first, and allocates whatever now fits.</summary>
/// <param name="orders">Order lookup — oldest-first backordered order listing.</param>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="waves">Wave and task insertion.</param>
/// <param name="binStocks">The allocator's candidate pool.</param>
/// <param name="allocator">Decides which bin(s) satisfy a line's demand.</param>
/// <param name="fulfilment">Reads available-to-promise.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ReattemptBackorderedAllocationsCommandHandler(
    ISalesOrderRepository orders,
    IStockLocationRepository locations,
    IPickWaveRepository waves,
    IBinStockRepository binStocks,
    IPickAllocationStrategy allocator,
    IOrderFulfilmentReader fulfilment,
    IClock clock) : ICommandHandler<ReattemptBackorderedAllocationsCommand, ReattemptBackorderedAllocationsResult>
{
    /// <inheritdoc />
    public async Task<ReattemptBackorderedAllocationsResult> HandleAsync(
        ReattemptBackorderedAllocationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<SalesOrder> backorderedSummaries = await orders
            .ListBackorderedOrdersAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = clock.UtcNow;
        int ordersReallocated = 0;
        int linesReallocated = 0;

        foreach (SalesOrder summary in backorderedSummaries)
        {
            // Read-only above (the same convention every other List*Async in this codebase follows);
            // re-fetch the aggregate to mutate it, exactly as ConfirmOrderCommandHandler does.
            SalesOrder? order = await orders.FindAsync(summary.Id, cancellationToken).ConfigureAwait(false);

            if (order is null)
            {
                continue;
            }

            StockLocation? location = await locations
                .FindAsync(order.FulfillingLocationId, cancellationToken)
                .ConfigureAwait(false);

            if (location is null)
            {
                continue;
            }

            PickWave? wave = null;
            bool touchedOrder = false;

            foreach (SalesOrderLine line in order.Lines
                .Where(candidate => candidate.LineStatus != SalesOrderLineStatus.Cancelled && !candidate.BackorderedQuantity.IsZero))
            {
                Quantity available = await fulfilment
                    .GetAvailableToPromiseAsync(
                        location.Id, line.ItemId, line.ItemVariantId, line.BackorderedQuantity.UnitOfMeasure, cancellationToken)
                    .ConfigureAwait(false);

                if (available.IsZero)
                {
                    continue;
                }

                Quantity attempt = available < line.BackorderedQuantity ? available : line.BackorderedQuantity;

                (wave, Quantity allocated) = await OrderAllocation
                    .AttemptAsync(waves, binStocks, allocator, line, location, attempt, wave, cancellationToken)
                    .ConfigureAwait(false);

                if (allocated.IsZero)
                {
                    continue;
                }

                Quantity newBackordered = line.BackorderedQuantity - allocated;
                line.RecordAllocationOutcome(
                    newBackordered.IsNegative ? Quantity.Zero(line.BackorderedQuantity.UnitOfMeasure) : newBackordered);

                touchedOrder = true;
                linesReallocated++;
            }

            wave?.Release(now);

            if (touchedOrder)
            {
                order.RecomputeStatus();
                ordersReallocated++;
            }
        }

        return new ReattemptBackorderedAllocationsResult(ordersReallocated, linesReallocated);
    }
}

/// <summary>Cancels one order line — cancelling any open <c>PickTask</c> first (business rule 7).</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="SalesOrderLineId">The line.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelOrderLineCommand(Guid SalesOrderId, Guid SalesOrderLineId) : ICommand;

/// <summary>Rejects a malformed cancel-line command before it reaches the handler.</summary>
public sealed class CancelOrderLineCommandValidator : AbstractValidator<CancelOrderLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelOrderLineCommandValidator()
    {
        RuleFor(command => command.SalesOrderId).NotEmpty();
        RuleFor(command => command.SalesOrderLineId).NotEmpty();
    }
}

/// <summary>
/// Cancels a line with an open task by cancelling the task first (Stage 13's own <c>PickTask.Cancel</c>);
/// a line with none is simply zeroed.
/// </summary>
/// <param name="orders">Order lookup.</param>
/// <param name="fulfilment">Finds the line's open tasks.</param>
/// <param name="waves">Task lookup and cancellation.</param>
public sealed class CancelOrderLineCommandHandler(
    ISalesOrderRepository orders, IOrderFulfilmentReader fulfilment, IPickWaveRepository waves)
    : ICommandHandler<CancelOrderLineCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelOrderLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        SalesOrderLine line = order.RequireLine(command.SalesOrderLineId);

        await CancelOpenTasksAsync(line, fulfilment, waves, cancellationToken).ConfigureAwait(false);

        line.Cancel();
        order.RecomputeStatus();

        return Unit.Value;
    }

    /// <summary>Cancels every open <c>PickTask</c> found for a line. Shared with <see cref="CancelOrderCommandHandler"/>.</summary>
    internal static async Task CancelOpenTasksAsync(
        SalesOrderLine line, IOrderFulfilmentReader fulfilment, IPickWaveRepository waves, CancellationToken cancellationToken)
    {
        OrderLineFulfilmentSnapshot snapshot = await fulfilment
            .GetLineFulfilmentAsync(line.Id, line.RequestedQuantity.UnitOfMeasure, cancellationToken)
            .ConfigureAwait(false);

        foreach (Guid taskId in snapshot.OpenPickTaskIds)
        {
            PickTask? task = await waves.FindTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            task?.Cancel();
        }
    }
}

/// <summary>Cancels a whole order — refusing one that has already shipped anything (business rule 7).</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Reason">Why, in the operator's words.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelOrderCommand(Guid SalesOrderId, string? Reason) : ICommand;

/// <summary>Rejects a malformed cancel-order command before it reaches the handler.</summary>
public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelOrderCommandValidator()
    {
        RuleFor(command => command.SalesOrderId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(500);
    }
}

/// <summary>Cancels every open line, then the order itself.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="fulfilment">Finds each line's open tasks.</param>
/// <param name="waves">Task lookup and cancellation.</param>
/// <param name="principal">Who is cancelling.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CancelOrderCommandHandler(
    ISalesOrderRepository orders,
    IOrderFulfilmentReader fulfilment,
    IPickWaveRepository waves,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CancelOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        order.EnsureCancellable();

        foreach (SalesOrderLine line in order.Lines.Where(candidate => candidate.LineStatus
            is not SalesOrderLineStatus.Fulfilled and not SalesOrderLineStatus.PartiallyFulfilled and not SalesOrderLineStatus.Cancelled))
        {
            await CancelOrderLineCommandHandler.CancelOpenTasksAsync(line, fulfilment, waves, cancellationToken).ConfigureAwait(false);
            line.Cancel();
        }

        order.MarkCancelled(command.Reason, principal.Principal, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Reconciles every active line's status from Stage 13's current state (business rule 4).</summary>
/// <param name="SalesOrderId">The order.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RefreshOrderFulfilmentCommand(Guid SalesOrderId) : ICommand;

/// <summary>Rejects a malformed refresh command before it reaches the handler.</summary>
public sealed class RefreshOrderFulfilmentCommandValidator : AbstractValidator<RefreshOrderFulfilmentCommand>
{
    /// <summary>Builds the rules.</summary>
    public RefreshOrderFulfilmentCommandValidator() => RuleFor(command => command.SalesOrderId).NotEmpty();
}

/// <summary>Refreshes an order's lines and its own status.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="fulfilment">Stage 14's one read seam into Stage 13's state.</param>
public sealed class RefreshOrderFulfilmentCommandHandler(ISalesOrderRepository orders, IOrderFulfilmentReader fulfilment)
    : ICommandHandler<RefreshOrderFulfilmentCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RefreshOrderFulfilmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        await OrderFulfilmentRefresh.ApplyAsync(order, fulfilment, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>What a completed order recognised, or not.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Status">The order's status after refreshing.</param>
/// <param name="RevenueRecognised"><c>true</c> if this call actually recognised revenue; <c>false</c> if it already had (idempotent no-op, business rule 5).</param>
/// <param name="Net">The order's net value.</param>
/// <param name="Tax">The order's tax.</param>
/// <param name="Gross">What the order is worth.</param>
public sealed record CompleteOrderResult(
    Guid SalesOrderId, SalesOrderStatus Status, bool RevenueRecognised, Money Net, Money Tax, Money Gross);

/// <summary>
/// Refreshes fulfilment, then — if every active line has shipped or been cancelled and revenue has not
/// already been recognised — raises <c>orders.order.fulfilled</c> (business rule 5).
/// </summary>
/// <param name="SalesOrderId">The order.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteOrderCommand(Guid SalesOrderId) : ICommand<CompleteOrderResult>;

/// <summary>Rejects a malformed complete command before it reaches the handler.</summary>
public sealed class CompleteOrderCommandValidator : AbstractValidator<CompleteOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteOrderCommandValidator() => RuleFor(command => command.SalesOrderId).NotEmpty();
}

/// <summary>Completes the order and reports what happened.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="fulfilment">Stage 14's one read seam into Stage 13's state.</param>
/// <param name="financialEvents">Where the recognised-revenue event is raised.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CompleteOrderCommandHandler(
    ISalesOrderRepository orders,
    IOrderFulfilmentReader fulfilment,
    IOrderFulfilmentEventPublisher financialEvents,
    IClock clock) : ICommandHandler<CompleteOrderCommand, CompleteOrderResult>
{
    /// <inheritdoc />
    public async Task<CompleteOrderResult> HandleAsync(CompleteOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        await OrderFulfilmentRefresh.ApplyAsync(order, fulfilment, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = clock.UtcNow;
        bool recognised = order.RecogniseRevenueIfDue(now);

        if (recognised)
        {
            OrderFulfilledEvent fulfilledEvent = new(
                order.TenantId, order.StoreId, order.Id, order.OrderNumber, order.Net, order.Tax, order.Gross, now);

            await financialEvents.PublishAsync(fulfilledEvent, cancellationToken).ConfigureAwait(false);
        }

        return new CompleteOrderResult(order.Id, order.Status, recognised, order.Net, order.Tax, order.Gross);
    }
}

/// <summary>Records which mechanism paid for the order — no tender of its own (see "what this stage does not own").</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="PaymentStatus">The new payment status.</param>
/// <param name="SettlingSaleId">The Stage 09 sale that settled it, if a till did.</param>
/// <param name="SettlingCustomerAccountId">The Stage 10b account it was charged to, if one was.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordOrderSettlementCommand(
    Guid SalesOrderId, OrderPaymentStatus PaymentStatus, Guid? SettlingSaleId, Guid? SettlingCustomerAccountId) : ICommand;

/// <summary>Rejects a malformed settlement command before it reaches the handler.</summary>
public sealed class RecordOrderSettlementCommandValidator : AbstractValidator<RecordOrderSettlementCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordOrderSettlementCommandValidator()
    {
        RuleFor(command => command.SalesOrderId).NotEmpty();
        RuleFor(command => command.PaymentStatus).IsInEnum();
    }
}

/// <summary>Records the settlement.</summary>
/// <param name="orders">Order lookup.</param>
public sealed class RecordOrderSettlementCommandHandler(ISalesOrderRepository orders)
    : ICommandHandler<RecordOrderSettlementCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RecordOrderSettlementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesOrder order = await orders.FindAsync(command.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", command.SalesOrderId);

        order.RecordSettlement(command.PaymentStatus, command.SettlingSaleId, command.SettlingCustomerAccountId);

        return Unit.Value;
    }
}
