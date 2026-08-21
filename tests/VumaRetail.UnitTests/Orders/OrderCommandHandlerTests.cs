using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// <see cref="ConfirmOrderCommandHandler"/>: prices every line, then attempts allocation per line
/// (business rule 2) entirely against fakes of Stage 13's own ports — never a real database, as the
/// stage document's own test list asks for.
/// </summary>
public sealed class ConfirmOrderCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly StockLocation _location = StockLocation.Create(TenantId, StoreId, "MAIN", "Main warehouse", StockLocationType.Warehouse);
    private Guid LocationId => _location.Id;

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IStockLocationRepository _locations = Substitute.For<IStockLocationRepository>();
    private readonly IPickWaveRepository _waves = Substitute.For<IPickWaveRepository>();
    private readonly IBinStockRepository _binStocks = Substitute.For<IBinStockRepository>();
    private readonly IPickAllocationStrategy _allocator = Substitute.For<IPickAllocationStrategy>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly ISellableItemResolver _catalog = Substitute.For<ISellableItemResolver>();
    private readonly IPriceResolver _priceResolver = Substitute.For<IPriceResolver>();
    private readonly ITaxCalculator _tax = Substitute.For<ITaxCalculator>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly List<PickWave> _addedWaves = [];
    private readonly List<PickTask> _addedTasks = [];

    public ConfirmOrderCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);

        _locations.FindAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);

        _catalog.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new SellableItem((Guid?)callInfo[0], (Guid?)callInfo[1], "Item", "EA", "STANDARD"));

        _priceResolver.ResolveAsync(Arg.Any<PriceResolutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                PriceResolutionRequest request = (PriceResolutionRequest)callInfo[0];
                Money unit = new(10m, request.Currency);
                Money extended = unit * request.Quantity;
                return new PriceResolution(null, null, null, true, unit, extended, Money.Zero(request.Currency), extended, [], "test");
            });

        _tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Money stated = (Money)callInfo[1];
                return new TaxCalculation("STANDARD", stated, Money.Zero(stated.Currency), stated, 0m);
            });

        _waves.WhenForAnyArgs(waves => waves.AddWave(default!)).Do(call => _addedWaves.Add(call.Arg<PickWave>()));
        _waves.WhenForAnyArgs(waves => waves.AddTask(default!)).Do(call => _addedTasks.Add(call.Arg<PickTask>()));
    }

    private ConfirmOrderCommandHandler Handler => new(
        _orders, _locations, _waves, _binStocks, _allocator, _fulfilment, _catalog, _priceResolver, _tax, _clock);

    private SalesOrder NewOrderWithOneLine(decimal requestedQuantity, out SalesOrderLine line)
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        line = order.AddLine(ItemId, null, new Quantity(requestedQuantity, "EA"));
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task A_line_that_fully_fits_is_allocated_in_full_with_no_backorder()
    {
        SalesOrder order = NewOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(new Quantity(10m, "EA"));
        _allocator.Allocate(Arg.Any<Quantity>(), Arg.Any<IReadOnlyList<BinStock>>())
            .Returns([new BinAllocation(BinId, new Quantity(10m, "EA"))]);

        await Handler.HandleAsync(new ConfirmOrderCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Allocated);
        line.BackorderedQuantity.IsZero.Should().BeTrue();
        _addedWaves.Should().HaveCount(1);
        _addedTasks.Should().ContainSingle(task => task.OutboundReference == line.Id.ToString() && task.RequestedQuantity.Value == 10m);
    }

    [Fact]
    public async Task A_line_that_partly_fits_splits_into_allocated_and_backordered()
    {
        SalesOrder order = NewOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(new Quantity(6m, "EA"));
        _allocator.Allocate(Arg.Any<Quantity>(), Arg.Any<IReadOnlyList<BinStock>>())
            .Returns([new BinAllocation(BinId, new Quantity(6m, "EA"))]);

        await Handler.HandleAsync(new ConfirmOrderCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyAllocated);
        line.BackorderedQuantity.Value.Should().Be(4m);
        _addedTasks.Should().ContainSingle(task => task.RequestedQuantity.Value == 6m);
    }

    [Fact]
    public async Task A_line_with_nothing_available_backorders_in_full_and_raises_no_task()
    {
        // Business rule 2: never refuses the order as a whole, and no task is created for a line that
        // fits nothing at all.
        SalesOrder order = NewOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(Quantity.Zero("EA"));

        await Handler.HandleAsync(new ConfirmOrderCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Backordered);
        line.BackorderedQuantity.Value.Should().Be(10m);
        _addedWaves.Should().BeEmpty();
        _addedTasks.Should().BeEmpty();
        order.Status.Should().Be(SalesOrderStatus.Confirmed);
    }

    [Fact]
    public async Task The_order_is_confirmed_and_priced_before_allocation_is_attempted()
    {
        SalesOrder order = NewOrderWithOneLine(1m, out SalesOrderLine line);
        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(Quantity.Zero("EA"));

        await Handler.HandleAsync(new ConfirmOrderCommand(order.Id));

        order.Status.Should().NotBe(SalesOrderStatus.Draft);
        line.UnitPrice.Amount.Should().Be(10m);
    }
}

/// <summary>
/// <see cref="ReattemptBackorderedAllocationsCommandHandler"/>: business rule 3's fairness queue —
/// oldest order first, and a later order never jumps the queue by chance of being reattempted sooner.
/// </summary>
public sealed class ReattemptBackorderedAllocationsCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly StockLocation _location = StockLocation.Create(TenantId, StoreId, "MAIN", "Main warehouse", StockLocationType.Warehouse);
    private Guid LocationId => _location.Id;

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IStockLocationRepository _locations = Substitute.For<IStockLocationRepository>();
    private readonly IPickWaveRepository _waves = Substitute.For<IPickWaveRepository>();
    private readonly IBinStockRepository _binStocks = Substitute.For<IBinStockRepository>();
    private readonly IPickAllocationStrategy _allocator = Substitute.For<IPickAllocationStrategy>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private ReattemptBackorderedAllocationsCommandHandler Handler => new(_orders, _locations, _waves, _binStocks, _allocator, _fulfilment, _clock);

    private SalesOrder BackorderedOrder(string number, DateTimeOffset orderDate, decimal backordered, out SalesOrderLine line)
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, number, null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", orderDate, null);
        line = order.AddLine(ItemId, null, new Quantity(backordered, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(orderDate);
        line.RecordAllocationOutcome(new Quantity(backordered, "EA"));
        return order;
    }

    [Fact]
    public async Task With_only_enough_stock_for_one_order_the_older_order_is_served_first()
    {
        SalesOrder older = BackorderedOrder("ORD-000001", Now.AddDays(-2), 2m, out SalesOrderLine olderLine);
        SalesOrder newer = BackorderedOrder("ORD-000002", Now.AddDays(-1), 2m, out SalesOrderLine newerLine);

        // The repository contract is "oldest OrderDate first" — the handler must not re-sort or
        // reverse whatever order it is handed.
        _orders.ListBackorderedOrdersAsync(Arg.Any<CancellationToken>()).Returns([older, newer]);
        _orders.FindAsync(older.Id, Arg.Any<CancellationToken>()).Returns(older);
        _orders.FindAsync(newer.Id, Arg.Any<CancellationToken>()).Returns(newer);

        _locations.FindAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);

        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(new Quantity(2m, "EA"));

        // Only enough stock in the bin for one order's worth: the first call (the older order) takes
        // it all, the second finds nothing left.
        int allocateCalls = 0;
        _allocator.Allocate(Arg.Any<Quantity>(), Arg.Any<IReadOnlyList<BinStock>>())
            .Returns(_ => ++allocateCalls == 1
                ? [new BinAllocation(BinId, new Quantity(2m, "EA"))]
                : (IReadOnlyList<BinAllocation>)[]);

        ReattemptBackorderedAllocationsResult result = await Handler.HandleAsync(new ReattemptBackorderedAllocationsCommand());

        olderLine.BackorderedQuantity.IsZero.Should().BeTrue("the older order was processed first and took the only stock available");
        newerLine.BackorderedQuantity.Value.Should().Be(2m, "nothing was left once the older order was served");
        result.OrdersReallocated.Should().Be(1);
        result.LinesReallocated.Should().Be(1);
    }

    [Fact]
    public async Task An_order_with_no_stock_available_at_all_is_left_untouched()
    {
        SalesOrder order = BackorderedOrder("ORD-000001", Now, 3m, out SalesOrderLine line);
        _orders.ListBackorderedOrdersAsync(Arg.Any<CancellationToken>()).Returns([order]);
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _locations.FindAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);
        _fulfilment.GetAvailableToPromiseAsync(LocationId, ItemId, null, "EA", Arg.Any<CancellationToken>())
            .Returns(Quantity.Zero("EA"));

        ReattemptBackorderedAllocationsResult result = await Handler.HandleAsync(new ReattemptBackorderedAllocationsCommand());

        line.BackorderedQuantity.Value.Should().Be(3m);
        result.OrdersReallocated.Should().Be(0);
        result.LinesReallocated.Should().Be(0);
    }
}

/// <summary>
/// <see cref="RefreshOrderFulfilmentCommandHandler"/>: reconciling a line's status from what Stage 13
/// currently reports (business rule 4), including the short-pick distinction its own worked example
/// names.
/// </summary>
public sealed class RefreshOrderFulfilmentCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();

    private RefreshOrderFulfilmentCommandHandler Handler => new(_orders, _fulfilment);

    private SalesOrder ConfirmedOrderWithOneLine(decimal requested, out SalesOrderLine line)
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        line = order.AddLine(ItemId, null, new Quantity(requested, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task A_fully_shipped_line_becomes_fulfilled()
    {
        SalesOrder order = ConfirmedOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), new Quantity(10m, "EA"), false, []));

        await Handler.HandleAsync(new RefreshOrderFulfilmentCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Fulfilled);
        order.Status.Should().Be(SalesOrderStatus.Fulfilled);
    }

    [Fact]
    public async Task A_partly_shipped_line_becomes_partially_fulfilled()
    {
        SalesOrder order = ConfirmedOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), new Quantity(6m, "EA"), false, []));

        await Handler.HandleAsync(new RefreshOrderFulfilmentCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task A_short_pick_reads_back_as_partially_fulfilled_not_backordered()
    {
        // Business rule 4's own worked example: a short pick is Stage 13's discovered stock
        // discrepancy, never this stage's Backordered.
        SalesOrder order = ConfirmedOrderWithOneLine(10m, out SalesOrderLine line);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), new Quantity(8m, "EA"), true, []));

        await Handler.HandleAsync(new RefreshOrderFulfilmentCommand(order.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyFulfilled);
        line.LineStatus.Should().NotBe(SalesOrderLineStatus.Backordered);
    }
}

/// <summary>
/// <see cref="CompleteOrderCommandHandler"/>: revenue is recognised once per order (business rule 5) —
/// asserted as an idempotency guard across two calls, exactly as the stage document asks.
/// </summary>
public sealed class CompleteOrderCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly IOrderFulfilmentEventPublisher _financialEvents = Substitute.For<IOrderFulfilmentEventPublisher>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CompleteOrderCommandHandler Handler => new(_orders, _fulfilment, _financialEvents, _clock);

    [Fact]
    public async Task Completing_a_fully_shipped_order_twice_raises_the_event_exactly_once()
    {
        _clock.UtcNow.Returns(Now);

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(new Money(10m, "ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), new Quantity(1m, "EA"), false, []));

        CompleteOrderResult first = await Handler.HandleAsync(new CompleteOrderCommand(order.Id));
        CompleteOrderResult second = await Handler.HandleAsync(new CompleteOrderCommand(order.Id));

        first.RevenueRecognised.Should().BeTrue();
        second.RevenueRecognised.Should().BeFalse();
        await _financialEvents.Received(1).PublishAsync(Arg.Any<OrderFulfilledEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_order_with_an_open_line_refuses_to_complete()
    {
        _clock.UtcNow.Returns(Now);

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000002", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(new Quantity(1m, "EA"), Quantity.Zero("EA"), false, [UuidV7.NewGuid()]));

        Func<Task> completing = () => Handler.HandleAsync(new CompleteOrderCommand(order.Id));

        await completing.Should().ThrowAsync<OrdersRuleException>().Where(exception => exception.Code == "ORDERS_NOT_FULLY_ACCOUNTED_FOR");
        await _financialEvents.DidNotReceive().PublishAsync(Arg.Any<OrderFulfilledEvent>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// <see cref="CancelOrderLineCommandHandler"/>: a line with an open task cancels the task first
/// (business rule 7); one with none is simply zeroed; a fulfilled line refuses.
/// </summary>
public sealed class CancelOrderLineCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid PickWaveId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly IPickWaveRepository _waves = Substitute.For<IPickWaveRepository>();

    private CancelOrderLineCommandHandler Handler => new(_orders, _fulfilment, _waves);

    private SalesOrder OrderWithOneLine(out SalesOrderLine line)
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        line = order.AddLine(ItemId, null, new Quantity(5m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task A_line_with_an_open_task_cancels_the_task_first()
    {
        SalesOrder order = OrderWithOneLine(out SalesOrderLine line);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        PickTask task = PickTask.Create(TenantId, StoreId, PickWaveId, ItemId, null, new Quantity(5m, "EA"), line.Id.ToString());
        task.Allocate(UuidV7.NewGuid(), new Quantity(5m, "EA"));

        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(new Quantity(5m, "EA"), Quantity.Zero("EA"), false, [task.Id]));
        _waves.FindTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await Handler.HandleAsync(new CancelOrderLineCommand(order.Id, line.Id));

        task.Status.Should().Be(PickTaskStatus.Cancelled);
        line.LineStatus.Should().Be(SalesOrderLineStatus.Cancelled);
    }

    [Fact]
    public async Task A_line_with_no_open_task_is_simply_zeroed()
    {
        SalesOrder order = OrderWithOneLine(out SalesOrderLine line);
        line.RecordAllocationOutcome(line.RequestedQuantity);

        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), Quantity.Zero("EA"), false, []));

        await Handler.HandleAsync(new CancelOrderLineCommand(order.Id, line.Id));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_an_already_fulfilled_line_is_refused()
    {
        SalesOrder order = OrderWithOneLine(out SalesOrderLine line);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));
        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), line.RequestedQuantity);

        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), line.RequestedQuantity, false, []));

        Func<Task> cancelling = () => Handler.HandleAsync(new CancelOrderLineCommand(order.Id, line.Id));

        await cancelling.Should().ThrowAsync<OrdersRuleException>().Where(exception => exception.Code == "ORDERS_CANNOT_CANCEL_FULFILLED_LINE");
    }
}
