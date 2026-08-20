using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Orders.Queries;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Orders;

/// <summary>Who is authorising — resolved from the ambient principal, never from the command.</summary>
public sealed class OrdersActorTests
{
    [Fact]
    public void A_user_principal_resolves_to_its_id()
    {
        Guid userId = UuidV7.NewGuid();
        IPrincipalAccessor principal = Substitute.For<IPrincipalAccessor>();
        principal.Principal.Returns($"user:{userId}");

        OrdersActor.RequireUserId(principal).Should().Be(userId);
    }

    [Fact]
    public void A_system_principal_is_refused()
    {
        IPrincipalAccessor principal = Substitute.For<IPrincipalAccessor>();
        principal.Principal.Returns("system:host");

        Action requiring = () => OrdersActor.RequireUserId(principal);

        requiring.Should().Throw<OrdersPrincipalException>().Which.Code.Should().Be("ORDERS_NOT_A_USER");
    }
}

/// <summary><see cref="CancelOrderCommandHandler"/>: cancels every open line, then the order itself.</summary>
public sealed class CancelOrderCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalesOrderRepository _orders = Substitute.For<ISalesOrderRepository>();
    private readonly IOrderFulfilmentReader _fulfilment = Substitute.For<IOrderFulfilmentReader>();
    private readonly IPickWaveRepository _waves = Substitute.For<IPickWaveRepository>();
    private readonly IPrincipalAccessor _principal = Substitute.For<IPrincipalAccessor>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CancelOrderCommandHandler Handler => new(_orders, _fulfilment, _waves, _principal, _clock);

    [Fact]
    public async Task Cancelling_an_order_with_an_open_line_cancels_the_task_and_the_order()
    {
        _clock.UtcNow.Returns(Now);
        _principal.Principal.Returns("user:abc");

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(5m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        PickTask task = PickTask.Create(TenantId, StoreId, UuidV7.NewGuid(), ItemId, null, new Quantity(5m, "EA"), line.Id.ToString());
        task.Allocate(UuidV7.NewGuid(), new Quantity(5m, "EA"));

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _fulfilment.GetLineFulfilmentAsync(line.Id, "EA", Arg.Any<CancellationToken>())
            .Returns(new OrderLineFulfilmentSnapshot(new Quantity(5m, "EA"), Quantity.Zero("EA"), false, [task.Id]));
        _waves.FindTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await Handler.HandleAsync(new CancelOrderCommand(order.Id, "Changed mind"));

        task.Status.Should().Be(PickTaskStatus.Cancelled);
        line.LineStatus.Should().Be(SalesOrderLineStatus.Cancelled);
        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        order.CancelReason.Should().Be("Changed mind");
    }

    [Fact]
    public async Task Cancelling_an_order_that_has_already_shipped_something_is_refused()
    {
        _clock.UtcNow.Returns(Now);

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000002", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(5m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(5m, "EA"));

        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        Func<Task> cancelling = () => Handler.HandleAsync(new CancelOrderCommand(order.Id, null));

        await cancelling.Should().ThrowAsync<OrdersRuleException>().Where(exception => exception.Code == "ORDERS_CANNOT_CANCEL_FULFILLED_ORDER");
    }
}

/// <summary><see cref="RecordOrderSettlementCommandHandler"/>: names which mechanism paid.</summary>
public sealed class RecordOrderSettlementCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recording_a_settlement_updates_the_order()
    {
        ISalesOrderRepository orders = Substitute.For<ISalesOrderRepository>();

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        Guid saleId = UuidV7.NewGuid();
        orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        RecordOrderSettlementCommandHandler handler = new(orders);
        await handler.HandleAsync(new RecordOrderSettlementCommand(order.Id, OrderPaymentStatus.Paid, saleId, null));

        order.PaymentStatus.Should().Be(OrderPaymentStatus.Paid);
        order.SettlingSaleId.Should().Be(saleId);
    }
}

/// <summary><see cref="CreateOrderReturnCommandHandler"/>: raises the return, drawing an ORT number, attributed to the authorising user.</summary>
public sealed class CreateOrderReturnCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Raising_a_return_draws_its_number_from_the_ORT_series()
    {
        ISalesOrderReturnRepository returns = Substitute.For<ISalesOrderReturnRepository>();
        ISalesOrderRepository orders = Substitute.For<ISalesOrderRepository>();
        IDocumentNumberSequence numbers = Substitute.For<IDocumentNumberSequence>();
        IPrincipalAccessor principal = Substitute.For<IPrincipalAccessor>();
        IClock clock = Substitute.For<IClock>();

        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);

        orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        numbers.NextAsync(CreateOrderReturnCommandHandler.ReturnNumberSeries, Arg.Any<CancellationToken>()).Returns("ORT-000001");
        principal.Principal.Returns($"user:{UuidV7.NewGuid()}");
        clock.UtcNow.Returns(Now);

        SalesOrderReturn? added = null;
        returns.WhenForAnyArgs(repo => repo.Add(default!)).Do(call => added = call.Arg<SalesOrderReturn>());

        CreateOrderReturnCommandHandler handler = new(returns, orders, numbers, principal, clock);
        Guid id = await handler.HandleAsync(new CreateOrderReturnCommand(order.Id, "Faulty"));

        added.Should().NotBeNull();
        added!.ReturnNumber.Should().Be("ORT-000001");
        added.Id.Should().Be(id);
    }
}

/// <summary><see cref="CompleteOrderReturnCommandHandler"/>: delegates to the completion service.</summary>
public sealed class CompleteOrderReturnCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Completing_reports_the_returns_refund_and_any_refused_lines()
    {
        ISalesOrderReturnRepository returns = Substitute.For<ISalesOrderReturnRepository>();
        IOrderReturnCompletionService completion = Substitute.For<IOrderReturnCompletionService>();

        SalesOrderReturn orderReturn = SalesOrderReturn.Raise(
            UuidV7.NewGuid(), TenantId, StoreId, "ZAR", "ORT-000001", "Faulty", UserId, Now);
        returns.FindAsync(orderReturn.Id, Arg.Any<CancellationToken>()).Returns(orderReturn);

        CompleteOrderReturnCommandHandler handler = new(returns, completion);
        OrderReturnCompletionResult result = await handler.HandleAsync(new CompleteOrderReturnCommand(orderReturn.Id));

        await completion.Received(1).CompleteAsync(orderReturn, Arg.Any<CancellationToken>());
        result.SalesOrderReturnId.Should().Be(orderReturn.Id);
        result.ReturnNumber.Should().Be("ORT-000001");
    }
}

/// <summary>The three list queries: orders, order returns, and backordered lines.</summary>
public sealed class OrderQueryHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Listing_orders_maps_the_page()
    {
        ISalesOrderRepository orders = Substitute.For<ISalesOrderRepository>();
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);

        orders.ListPageAsync(
            null, null, null, null, null, Arg.Any<VumaRetail.Application.Abstractions.KeysetCursor?>(), 50, Arg.Any<CancellationToken>())
            .Returns(([order], false));

        ListOrdersQueryHandler handler = new(orders);
        VumaRetail.Application.Abstractions.PageResult<SalesOrderSummaryResult> page = await handler.HandleAsync(
            new ListOrdersQuery(null, null, null, null, null, null, null));

        page.Items.Should().ContainSingle(item => item.OrderNumber == "ORD-000001");
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Listing_order_returns_maps_the_page()
    {
        ISalesOrderReturnRepository returns = Substitute.For<ISalesOrderReturnRepository>();
        SalesOrderReturn orderReturn = SalesOrderReturn.Raise(
            UuidV7.NewGuid(), TenantId, StoreId, "ZAR", "ORT-000001", "Faulty", UserId, Now);

        returns.ListForPeriodAsync(
            Now.AddDays(-1), Now.AddDays(1), Arg.Any<VumaRetail.Application.Abstractions.KeysetCursor?>(), 50, Arg.Any<CancellationToken>())
            .Returns(([orderReturn], false));

        ListOrderReturnsQueryHandler handler = new(returns);
        VumaRetail.Application.Abstractions.PageResult<SalesOrderReturnResult> page = await handler.HandleAsync(
            new ListOrderReturnsQuery(Now.AddDays(-1), Now.AddDays(1), null, null));

        page.Items.Should().ContainSingle(item => item.ReturnNumber == "ORT-000001");
    }

    [Fact]
    public async Task Listing_backordered_lines_flattens_every_order()
    {
        ISalesOrderRepository orders = Substitute.For<ISalesOrderRepository>();
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, LocationId,
            null, "ZAR", Now, null);
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(3m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordAllocationOutcome(new Quantity(3m, "EA"));

        orders.ListBackorderedOrdersAsync(Arg.Any<CancellationToken>()).Returns([order]);

        ListBackorderedLinesQueryHandler handler = new(orders);
        IReadOnlyList<BackorderedLineResult> result = await handler.HandleAsync(new ListBackorderedLinesQuery());

        result.Should().ContainSingle(item => item.SalesOrderLineId == line.Id && item.BackorderedQuantity.Value == 3m);
    }
}
