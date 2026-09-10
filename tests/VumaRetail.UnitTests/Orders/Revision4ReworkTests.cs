using NSubstitute;
using VumaRetail.Application.Orders;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// TASK-14-002 (revision-4 rework): the geography snapshot (ADR-113), COD settlement terms with the
/// dispatch gate (ADR-111), and the line hold pointer (ADR-103).
/// </summary>
public sealed class Revision4ReworkTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly Address DeliveryAddress = Address.Create(
        "  12 Main Road  ", "Durban", "za", null, " KwaZulu-Natal ", "4001");

    private static SalesOrder DeliveryOrder(
        SettlementTerms terms = SettlementTerms.Standard,
        string? suburb = "  Umhlanga  ")
        => SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", partnerId: null, SalesChannel.Phone,
            OrderFulfilmentType.Delivery, LocationId, DeliveryAddress, "ZAR", Now, null,
            terms, suburb);

    private static SalesOrderLine NewLine(SalesOrder order, decimal requested = 5m)
    {
        SalesOrderLine line = SalesOrderLine.Create(
            TenantId, StoreId, order.Id, ItemId, null, new Quantity(requested, "EA"), "ZAR");
        return line;
    }

    [Fact]
    public void Delivery_snapshots_normalised_geography_at_capture()
    {
        SalesOrder order = DeliveryOrder();

        order.DeliveryGeography.Should().Be(
            new DeliveryGeography("KwaZulu-Natal", "Durban", "Umhlanga", "4001"));
    }

    [Fact]
    public void Click_and_collect_carries_no_geography_snapshot()
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000002", partnerId: null, SalesChannel.Phone,
            OrderFulfilmentType.ClickAndCollect, LocationId, null, "ZAR", Now, null);

        order.DeliveryGeography.Should().BeNull();
    }

    [Fact]
    public void Missing_snapshot_parts_become_empty_not_null()
    {
        Address sparse = Address.Create("12 Main Road", "Durban", "ZA");
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000003", partnerId: null, SalesChannel.Phone,
            OrderFulfilmentType.Delivery, LocationId, sparse, "ZAR", Now, null);

        order.DeliveryGeography.Should().Be(new DeliveryGeography(string.Empty, "Durban", string.Empty, string.Empty));
    }

    [Fact]
    public void Unknown_settlement_terms_are_refused()
    {
        Action creating = () => SalesOrder.Create(
            TenantId, StoreId, "ORD-000004", partnerId: null, SalesChannel.Phone,
            OrderFulfilmentType.ClickAndCollect, LocationId, null, "ZAR", Now, null,
            (SettlementTerms)99);

        creating.Should().Throw<OrdersRuleException>()
            .WithMessage("*settlement terms*");
    }

    [Fact]
    public void Standard_terms_ship_without_payment_or_authorisation()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.Standard);

        Action releasing = () => order.ReleaseForDispatch(Now);

        releasing.Should().NotThrow();
    }

    [Fact]
    public void Cod_blocks_dispatch_while_unpaid_and_unauthorised()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);

        Action releasing = () => order.ReleaseForDispatch(Now);

        releasing.Should().Throw<OrdersRuleException>()
            .WithMessage("*cash-on-delivery*");
    }

    [Fact]
    public void Cod_ships_once_paid()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);
        order.RecordSettlement(OrderPaymentStatus.Paid, Guid.NewGuid(), null);

        Action releasing = () => order.ReleaseForDispatch(Now);

        releasing.Should().NotThrow();
    }

    [Fact]
    public void Cod_ships_against_a_named_driver_collect()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);
        order.AuthoriseDriverCollect("  Sipho Ndlovu  ", "user:ayanda", Now);

        order.DriverCollectAuthorisedBy.Should().Be("Sipho Ndlovu");

        Action releasing = () => order.ReleaseForDispatch(Now);

        releasing.Should().NotThrow();
    }

    [Fact]
    public void Driver_collect_with_nobody_named_is_refused()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);

        Action authorising = () => order.AuthoriseDriverCollect("   ", "user:ayanda", Now);

        authorising.Should().Throw<OrdersRuleException>()
            .WithMessage("*name*");
    }

    [Fact]
    public void Dispatch_refuses_a_cancelled_order()
    {
        SalesOrder order = DeliveryOrder();
        order.EnsureCancellable();
        order.MarkCancelled("test", "user:ayanda", Now);

        Action releasing = () => order.ReleaseForDispatch(Now);

        releasing.Should().Throw<OrdersRuleException>();
    }

    [Fact]
    public void Attaching_a_second_live_hold_is_refused()
    {
        SalesOrder order = DeliveryOrder();
        SalesOrderLine line = NewLine(order);
        line.AttachReservation(Guid.NewGuid());

        Action attaching = () => line.AttachReservation(Guid.NewGuid());

        attaching.Should().Throw<OrdersRuleException>()
            .WithMessage("*reservation*");
    }

    [Fact]
    public void Detach_clears_the_hold_pointer()
    {
        SalesOrder order = DeliveryOrder();
        SalesOrderLine line = NewLine(order);
        line.AttachReservation(Guid.NewGuid());

        line.DetachReservation();

        line.ReservationId.Should().BeNull();
    }

    [Fact]
    public void Cancel_clears_the_hold_pointer()
    {
        SalesOrder order = DeliveryOrder();
        SalesOrderLine line = NewLine(order);
        line.AttachReservation(Guid.NewGuid());

        line.Cancel();

        line.ReservationId.Should().BeNull();
    }

    [Fact]
    public void Dispatch_gate_passes_non_order_references_untouched()
    {
        var orders = Substitute.For<ISalesOrderRepository>();
        var gate = new OrderDispatchGate(orders);

        Func<Task> shipping = () => gate.EnsureDispatchAllowedAsync(
            ["transfer:123", "not-a-guid"], Now);

        shipping.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispatch_gate_blocks_an_unpaid_cod_order_by_number()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);
        SalesOrderLine line = NewLine(order);

        var orders = Substitute.For<ISalesOrderRepository>();
        orders.FindOrderIdByLineAsync(line.Id, Arg.Any<CancellationToken>()).Returns(order.Id);
        orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var gate = new OrderDispatchGate(orders);

        Func<Task> shipping = () => gate.EnsureDispatchAllowedAsync([line.Id.ToString()], Now);

        shipping.Should().ThrowAsync<Domain.Warehouse.WarehouseRuleException>()
            .WithMessage("*ORD-000001*");
    }

    [Fact]
    public void Dispatch_gate_checks_each_order_once()
    {
        SalesOrder order = DeliveryOrder(SettlementTerms.CashOnDelivery);
        order.RecordSettlement(OrderPaymentStatus.Paid, Guid.NewGuid(), null);
        SalesOrderLine first = NewLine(order);
        SalesOrderLine second = NewLine(order);

        var orders = Substitute.For<ISalesOrderRepository>();
        orders.FindOrderIdByLineAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order.Id);
        orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var gate = new OrderDispatchGate(orders);

        Func<Task> shipping = () => gate.EnsureDispatchAllowedAsync(
            [first.Id.ToString(), second.Id.ToString()], Now);

        shipping.Should().NotThrowAsync();
        orders.Received(1).FindAsync(order.Id, Arg.Any<CancellationToken>());
    }
}
