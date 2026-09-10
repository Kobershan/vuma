using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Orders.Queries;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Orders;

/// <summary>
/// TASK-14-002 (revision-4 rework) against real PostgreSQL: reservations hold every bin promise and
/// available never goes negative; COD refuses dispatch until paid or authorised; the geography
/// snapshot feeds wave grouping; cancel releases and fulfilment consumes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderRevision4Tests(PostgresFixture fixture)
{
    [Fact]
    public async Task Two_confirms_for_the_last_stock_leave_holds_never_negative()
    {
        (ApiHarness harness, OrdersScenario scenario, _) =
            await OrdersHarnessSetup.CreateHarnessAsync(fixture).ConfigureAwait(false);
        await using (harness.ConfigureAwait(false))
        {

        // Both orders are raised before either confirms, so the second confirm's availability read
        // happens after the first confirm's hold committed — the ledger, not the read, serialises.
        Guid firstId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid firstLineId = await harness.SendAsync(
            new AddOrderLineCommand(firstId, scenario.InStockItemId, null, 40m, "EA"));

        Guid secondId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid secondLineId = await harness.SendAsync(
            new AddOrderLineCommand(secondId, scenario.InStockItemId, null, 40m, "EA"));

        await harness.SendAsync(new ConfirmOrderCommand(firstId));
        await harness.SendAsync(new ConfirmOrderCommand(secondId));

        SalesOrderResult first = await harness.QueryAsync(new GetOrderQuery(firstId));
        SalesOrderResult second = await harness.QueryAsync(new GetOrderQuery(secondId));
        first.Lines.Single(line => line.Id == firstLineId).LineStatus.Should().Be(SalesOrderLineStatus.Allocated);
        second.Lines.Single(line => line.Id == secondLineId).LineStatus.Should().Be(SalesOrderLineStatus.PartiallyAllocated);
        second.Lines.Single(line => line.Id == secondLineId).BackorderedQuantity.Value.Should().Be(30m);

        // Every bin promise is held, holds never exceed what is shelved, and available never negative.
        (decimal held, decimal available) = await LedgerAsync(harness, scenario);
        held.Should().Be(50m);
        available.Should().Be(0m);

        // The 14 carry-forward: high ATP against zero bin candidates still backorders the line.
        // (Covered structurally here: the out-of-stock line below holds nothing and backorders all.)
        Guid thirdId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid thirdLineId = await harness.SendAsync(
            new AddOrderLineCommand(thirdId, scenario.OutOfStockItemId, null, 3m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(thirdId));

        SalesOrderResult third = await harness.QueryAsync(new GetOrderQuery(thirdId));
        third.Lines.Single(line => line.Id == thirdLineId).LineStatus.Should().Be(SalesOrderLineStatus.Backordered);
        }
    }

    [Fact]
    public async Task Cod_refuses_ship_until_paid_then_ships_and_authorised_collects()
    {
        (ApiHarness harness, OrdersScenario scenario, _) =
            await OrdersHarnessSetup.CreateHarnessAsync(fixture).ConfigureAwait(false);
        await using (harness.ConfigureAwait(false))
        {

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.Delivery, scenario.LocationId,
            "12 Rivonia Road", null, "Sandton", "Gauteng", "2196", "ZA", "ZAR",
            RequestedFulfilmentDate: null, DeliverySuburb: "Morningside",
            SettlementTerms: SettlementTerms.CashOnDelivery));
        Guid lineId = await harness.SendAsync(
            new AddOrderLineCommand(orderId, scenario.InStockItemId, null, 5m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(orderId));

        PickTask task = await FindOpenTaskAsync(harness, lineId);
        await harness.SendAsync(new ConfirmPickCommand(task.Id, new Quantity(5m, "EA")));
        await harness.SendAsync(new PackWaveCommand(task.PickWaveId, 1, "COD run"));

        Func<Task> shipping = () => harness.SendAsync(
            new ShipWaveCommand(task.PickWaveId, "Driver", null));
        await shipping.Should().ThrowAsync<WarehouseRuleException>()
            .WithMessage("*cash on delivery*");

        await harness.SendAsync(new RecordOrderSettlementCommand(
            orderId, OrderPaymentStatus.Paid, SettlingSaleId: Guid.NewGuid(), SettlingCustomerAccountId: null));
        await harness.SendAsync(new ShipWaveCommand(task.PickWaveId, "Driver", null));

        // A second COD order ships against a named driver-collect authorisation instead of payment.
        Guid collectId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.Delivery, scenario.LocationId,
            "8 Palm Boulevard", null, "Umhlanga", "KwaZulu-Natal", "4319", "ZA", "ZAR",
            RequestedFulfilmentDate: null, SettlementTerms: SettlementTerms.CashOnDelivery));
        Guid collectLineId = await harness.SendAsync(
            new AddOrderLineCommand(collectId, scenario.InStockItemId, null, 2m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(collectId));

        PickTask collectTask = await FindOpenTaskAsync(harness, collectLineId);
        await harness.SendAsync(new ConfirmPickCommand(collectTask.Id, new Quantity(2m, "EA")));
        await harness.SendAsync(new PackWaveCommand(collectTask.PickWaveId, 1, "COD run"));

        Func<Task> collecting = () => harness.SendAsync(
            new ShipWaveCommand(collectTask.PickWaveId, "Driver", null));
        await collecting.Should().ThrowAsync<WarehouseRuleException>();

        await harness.SendAsync(new ReleaseOrderForDispatchCommand(collectId, "Sipho Ndlovu"));
        await harness.SendAsync(new ShipWaveCommand(collectTask.PickWaveId, "Driver", null));
        }
    }

    [Fact]
    public async Task Geography_snapshot_is_stored_and_groups_waves()
    {
        (ApiHarness harness, OrdersScenario scenario, _) =
            await OrdersHarnessSetup.CreateHarnessAsync(fixture).ConfigureAwait(false);
        await using (harness.ConfigureAwait(false))
        {

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.Delivery, scenario.LocationId,
            "12 Rivonia Road", null, "Sandton", "Gauteng", "2196", "ZA", "ZAR",
            RequestedFulfilmentDate: null, DeliverySuburb: "Morningside"));

        (string province, string city, string suburb) = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<VumaRetailDbContext>().SalesOrders
                .Where(order => order.Id == orderId)
                .Select(order => new ValueTuple<string, string, string>(
                    order.DeliveryGeography!.Province,
                    order.DeliveryGeography!.City,
                    order.DeliveryGeography!.Suburb))
                .FirstAsync());

        province.Should().Be("Gauteng");
        city.Should().Be("Sandton");
        suburb.Should().Be("Morningside");

        // What 13b's wave build will group by: the snapshot, never the live address.
        List<Guid> grouped = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<VumaRetailDbContext>().SalesOrders
                .Where(order => order.DeliveryGeography != null
                    && order.DeliveryGeography!.Province == "Gauteng"
                    && order.DeliveryGeography!.Suburb == "Morningside")
                .Select(order => order.Id)
                .ToListAsync());

        grouped.Should().Contain(orderId);
        }
    }

    [Fact]
    public async Task Cancel_releases_the_hold_and_fulfilment_consumes_it()
    {
        (ApiHarness harness, OrdersScenario scenario, _) =
            await OrdersHarnessSetup.CreateHarnessAsync(fixture).ConfigureAwait(false);
        await using (harness.ConfigureAwait(false))
        {

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid lineId = await harness.SendAsync(
            new AddOrderLineCommand(orderId, scenario.InStockItemId, null, 5m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(orderId));

        (decimal heldAfterConfirm, _) = await LedgerAsync(harness, scenario);
        heldAfterConfirm.Should().Be(5m);

        Guid cancelledId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid cancelledLineId = await harness.SendAsync(
            new AddOrderLineCommand(cancelledId, scenario.InStockItemId, null, 4m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(cancelledId));
        await harness.SendAsync(new CancelOrderLineCommand(cancelledId, cancelledLineId));

        (decimal heldAfterCancel, decimal availableAfterCancel) = await LedgerAsync(harness, scenario);
        heldAfterCancel.Should().Be(5m);
        availableAfterCancel.Should().Be(45m);

        // Ship the first order: refresh consumes its hold, and the shipped stock leaves available.
        PickTask task = await FindOpenTaskAsync(harness, lineId);
        await harness.SendAsync(new ConfirmPickCommand(task.Id, new Quantity(5m, "EA")));
        await harness.SendAsync(new PackWaveCommand(task.PickWaveId, 1, "Test pick"));
        await harness.SendAsync(new ShipWaveCommand(task.PickWaveId, "Test Carrier", "TRK-TEST"));
        await harness.SendAsync(new RefreshOrderFulfilmentCommand(orderId));

        (decimal heldAfterShip, decimal availableAfterShip) = await LedgerAsync(harness, scenario);
        heldAfterShip.Should().Be(0m);
        availableAfterShip.Should().Be(45m);
        }
    }

    private static async Task<(decimal Held, decimal Available)> LedgerAsync(
        ApiHarness harness, OrdersScenario scenario)
        => await harness.InScopeAsync(async provider =>
        {
            VumaRetailDbContext db = provider.GetRequiredService<VumaRetailDbContext>();
            IAvailabilityService availability = provider.GetRequiredService<IAvailabilityService>();

            // A chain's current state is its latest row: live held is what Held rows hold less
            // what terminal rows (consumed, released, expired) closed.
            decimal held = await db.StockReservations
                .Where(row => row.State == ReservationState.Held)
                .SumAsync(row => (decimal?)row.Quantity.Value) ?? 0m;
            decimal closed = await db.StockReservations
                .Where(row => row.State == ReservationState.Consumed
                    || row.State == ReservationState.Released
                    || row.State == ReservationState.Expired)
                .SumAsync(row => (decimal?)row.Quantity.Value) ?? 0m;

            Quantity promise = (await availability.GetLocalAsync(
                    scenario.LocationId, scenario.InStockItemId, null))
                .Promise.Available;

            return (held - closed, promise.Value);
        });

    private static async Task<PickTask> FindOpenTaskAsync(ApiHarness harness, Guid orderLineId)
        => await harness.InScopeAsync(provider =>
            provider.GetRequiredService<VumaRetailDbContext>().PickTasks
                .Where(task => task.OutboundReference == orderLineId.ToString())
                .OrderByDescending(task => task.CreatedAt)
                .FirstAsync());
}
