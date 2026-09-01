using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// <see cref="AddOrderReturnLineCommandHandler"/>: bounds a return line by what was actually fulfilled
/// less what earlier returns already took (business rule 6), reading both figures inside the
/// command's own transaction.
/// </summary>
public sealed class AddOrderReturnLineCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderReturnRepository _returns = Substitute.For<ISalesOrderReturnRepository>();
    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();

    private AddOrderReturnLineCommandHandler Handler => new(_returns, _orders, _fulfilment);

    private (SalesOrder Order, SalesOrderLine Line, SalesOrderReturn OrderReturn) FulfilledOrderWithOpenReturn(decimal fulfilled)
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(fulfilled, "EA"));
        line.ApplyPricing(new Money(20m, "ZAR"), Money.Zero("ZAR"), new Money(15m, "ZAR"), null, string.Empty);
        order.Confirm(Now);

        SalesOrderReturn orderReturn = SalesOrderReturn.Raise(
            order.Id, TenantId, StoreId, "ZAR", "ORT-000001", "Faulty", UserId, Now);

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _returns.FindAsync(orderReturn.Id, Arg.Any<CancellationToken>()).Returns(orderReturn);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(Quantity.Zero("EA"), new Quantity(fulfilled, "EA"), false, []));
        _returns.SumReturnedQuantityAsync(line.Id, Arg.Any<CancellationToken>()).Returns(0m);

        return (order, line, orderReturn);
    }

    [Fact]
    public async Task A_quantity_within_what_was_fulfilled_is_accepted()
    {
        (SalesOrder _, SalesOrderLine line, SalesOrderReturn orderReturn) = FulfilledOrderWithOpenReturn(5m);

        Guid id = await Handler.HandleAsync(new AddOrderReturnLineCommand(orderReturn.Id, line.Id, 3m));

        orderReturn.Lines.Should().ContainSingle(returnLine => returnLine.Id == id && returnLine.Quantity.Value == 3m);
    }

    [Fact]
    public async Task A_quantity_greater_than_what_was_fulfilled_is_refused()
    {
        (SalesOrder _, SalesOrderLine line, SalesOrderReturn orderReturn) = FulfilledOrderWithOpenReturn(5m);

        Func<Task> adding = () => Handler.HandleAsync(new AddOrderReturnLineCommand(orderReturn.Id, line.Id, 6m));

        await adding.Should().ThrowAsync<OrdersRuleException>().Where(exception => exception.Code == "ORDERS_RETURN_EXCEEDS_FULFILLED_QUANTITY");
    }

    [Fact]
    public async Task What_earlier_returns_already_took_narrows_what_remains_available()
    {
        (SalesOrder _, SalesOrderLine line, SalesOrderReturn orderReturn) = FulfilledOrderWithOpenReturn(5m);
        _returns.SumReturnedQuantityAsync(line.Id, Arg.Any<CancellationToken>()).Returns(4m);

        Func<Task> adding = () => Handler.HandleAsync(new AddOrderReturnLineCommand(orderReturn.Id, line.Id, 2m));

        await adding.Should().ThrowAsync<OrdersRuleException>().Where(exception => exception.Code == "ORDERS_RETURN_EXCEEDS_FULFILLED_QUANTITY");
    }
}

/// <summary>
/// <see cref="OrderReturnCompletionService"/>: receives stock back at the original shipment's unit cost
/// (ADR-093), and still completes — refund intact — when the ledger refuses the receipt
/// (ADR-070/073's precedent, applied to an order return).
/// </summary>
public sealed class OrderReturnCompletionServiceTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IStockLocationRepository _locations = Substitute.For<IStockLocationRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly IStockLedgerPoster _stockPoster = Substitute.For<IStockLedgerPoster>();
    private readonly IOrderReturnFinancialEventPublisher _financialEvents = Substitute.For<IOrderReturnFinancialEventPublisher>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private OrderReturnCompletionService Service => new(_orders, _locations, _fulfilment, _stockPoster, _financialEvents, _clock);

    private (SalesOrder Order, SalesOrderLine Line, SalesOrderReturn OrderReturn) SetUp()
    {
        _clock.UtcNow.Returns(Now);

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(5m, "EA"));
        line.ApplyPricing(new Money(20m, "ZAR"), Money.Zero("ZAR"), new Money(15m, "ZAR"), null, string.Empty);
        order.Confirm(Now);

        SalesOrderReturn orderReturn = SalesOrderReturn.Raise(order.Id, TenantId, StoreId, "ZAR", "ORT-000001", "Faulty", UserId, Now);
        orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(5m, "EA"), previouslyReturned: 0m);

        StockLocation location = StockLocation.Create(TenantId, StoreId, "MAIN", "Main warehouse", StockLocationType.Warehouse);

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _locations.FindAsync(LocationId, Arg.Any<CancellationToken>()).Returns(location);

        return (order, line, orderReturn);
    }

    [Fact]
    public async Task The_return_receives_stock_back_at_the_original_shipments_unit_cost()
    {
        (_, SalesOrderLine line, SalesOrderReturn orderReturn) = SetUp();
        Money originalCost = new(12.50m, "ZAR");

        _fulfilment.GetShipmentUnitCostAsync(line.Id, ItemId, null, "ZAR", Arg.Any<CancellationToken>())
            .Returns(originalCost);

        StockLedgerEntry entry = StockLedgerEntry.Post(
            TenantId, StoreId, LocationId, null, ItemId, null, StockMovementType.SalesReturn,
            new Quantity(1m, "EA"), originalCost, StockReferenceType.OrderReturn, orderReturn.Id, null, null);

        _stockPoster.ReceiveForOrderReturnAsync(
                Arg.Any<StockLocation>(), ItemId, null, Arg.Any<Quantity>(), originalCost, orderReturn.Id, Arg.Any<CancellationToken>())
            .Returns(entry);

        await Service.CompleteAsync(orderReturn);

        orderReturn.Status.Should().Be(SalesOrderReturnStatus.Completed);
        orderReturn.Lines[0].StockReturn.Should().Be(OrderStockReturnStatus.Posted);
        orderReturn.Lines[0].StockLedgerEntryId.Should().Be(entry.Id);
        await _stockPoster.Received(1).ReceiveForOrderReturnAsync(
            Arg.Any<StockLocation>(), ItemId, null, Arg.Any<Quantity>(), originalCost, orderReturn.Id, Arg.Any<CancellationToken>());
        await _financialEvents.Received(1).PublishAsync(Arg.Any<OrderReturnCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_return_still_completes_when_the_ledger_refuses_the_receipt()
    {
        // ADR-070/073's precedent: the refund is not held hostage by a ledger refusal.
        (_, SalesOrderLine line, SalesOrderReturn orderReturn) = SetUp();
        Money originalCost = new(12.50m, "ZAR");

        _fulfilment.GetShipmentUnitCostAsync(line.Id, ItemId, null, "ZAR", Arg.Any<CancellationToken>())
            .Returns(originalCost);

        _stockPoster.ReceiveForOrderReturnAsync(
                Arg.Any<StockLocation>(), ItemId, null, Arg.Any<Quantity>(), originalCost, orderReturn.Id, Arg.Any<CancellationToken>())
            .Returns<StockLedgerEntry>(_ => throw InventoryRuleException.InsufficientStock(Quantity.Zero("EA"), new Quantity(1m, "EA")));

        await Service.CompleteAsync(orderReturn);

        orderReturn.Status.Should().Be(SalesOrderReturnStatus.Completed);
        orderReturn.Lines[0].StockReturn.Should().Be(OrderStockReturnStatus.Refused);
        orderReturn.Lines[0].StockLedgerEntryId.Should().BeNull();
        await _financialEvents.Received(1).PublishAsync(Arg.Any<OrderReturnCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_traceable_shipment_leaves_the_line_refused_with_no_receipt_attempted()
    {
        (_, SalesOrderLine line, SalesOrderReturn orderReturn) = SetUp();

        _fulfilment.GetShipmentUnitCostAsync(line.Id, ItemId, null, "ZAR", Arg.Any<CancellationToken>())
            .Returns((Money?)null);

        await Service.CompleteAsync(orderReturn);

        orderReturn.Lines[0].StockReturn.Should().Be(OrderStockReturnStatus.Refused);
        await _stockPoster.DidNotReceiveWithAnyArgs().ReceiveForOrderReturnAsync(
            default!, default, default, default, default, default, default);
        await _financialEvents.Received(1).PublishAsync(Arg.Any<OrderReturnCompletedEvent>(), Arg.Any<CancellationToken>());
    }
}
