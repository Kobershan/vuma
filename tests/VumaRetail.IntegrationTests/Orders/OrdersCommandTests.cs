using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Orders.Queries;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Orders;

/// <summary>
/// The stage document's own end-to-end chain, run against the real store server and a real
/// PostgreSQL database: an order confirms with one line allocated and one backordered, ships,
/// reallocates on an explicit reattempt after a real receipt, completes and recognises revenue once,
/// a click &amp; collect order is collected end to end, and a return really puts stock back and really
/// reaches the GL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrdersCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_order_allocates_backorders_reallocates_ships_and_recognises_revenue_once()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        OrdersScenario scenario = await OrdersHarnessSetup.BuildAsync(harness);

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.Phone, OrderFulfilmentType.Delivery, scenario.LocationId,
            "12 Rivonia Road", null, "Sandton", "Gauteng", "2196", "ZA", "ZAR", RequestedFulfilmentDate: null));

        Guid inStockLineId = await harness.SendAsync(
            new AddOrderLineCommand(orderId, scenario.InStockItemId, null, 5m, "EA"));
        Guid backorderedLineId = await harness.SendAsync(
            new AddOrderLineCommand(orderId, scenario.OutOfStockItemId, null, 3m, "EA"));

        await harness.SendAsync(new ConfirmOrderCommand(orderId));

        // One line's PickTask created, the other backordered — never a refused order (business rule 2).
        SalesOrderResult afterConfirm = await harness.QueryAsync(new GetOrderQuery(orderId));
        afterConfirm.Lines.Single(line => line.Id == inStockLineId).LineStatus.Should().Be(SalesOrderLineStatus.Allocated);
        afterConfirm.Lines.Single(line => line.Id == backorderedLineId).LineStatus.Should().Be(SalesOrderLineStatus.Backordered);
        afterConfirm.Lines.Single(line => line.Id == backorderedLineId).BackorderedQuantity.Value.Should().Be(3m);

        PickTask inStockTask = await FindOpenTaskAsync(harness, inStockLineId);
        await ShipTaskAsync(harness, inStockTask, 5m);

        await harness.SendAsync(new RefreshOrderFulfilmentCommand(orderId));
        SalesOrderResult afterFirstShip = await harness.QueryAsync(new GetOrderQuery(orderId));
        afterFirstShip.Lines.Single(line => line.Id == inStockLineId).LineStatus.Should().Be(SalesOrderLineStatus.Fulfilled);

        // A receipt tops up the short SKU, shelved into a bin — ReattemptBackorderedAllocationsCommand
        // allocates the second line (business rule 3: an explicit call, nothing here is automatic).
        await harness.SendAsync(new ReceiveStockCommand(
            scenario.LocationId, scenario.OutOfStockItemId, null, new Quantity(10m, "EA"), new Money(120m, "ZAR"), "Backorder top-up"));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            scenario.LocationId, scenario.OutOfStockItemId, null, new Quantity(10m, "EA"), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, scenario.SpareBinId, new Quantity(10m, "EA")));

        ReattemptBackorderedAllocationsResult reattempt = await harness.SendAsync(new ReattemptBackorderedAllocationsCommand());
        reattempt.OrdersReallocated.Should().Be(1);
        reattempt.LinesReallocated.Should().Be(1);

        PickTask backorderedTask = await FindOpenTaskAsync(harness, backorderedLineId);
        await ShipTaskAsync(harness, backorderedTask, 3m);

        CompleteOrderResult firstComplete = await harness.SendAsync(new CompleteOrderCommand(orderId));
        firstComplete.RevenueRecognised.Should().BeTrue();

        // Business rule 5: a second call is an idempotent no-op, not a second journal.
        CompleteOrderResult secondComplete = await harness.SendAsync(new CompleteOrderCommand(orderId));
        secondComplete.RevenueRecognised.Should().BeFalse();

        List<Journal> journals = await OrdersHarnessSetup.JournalsForEventTypeAsync(harness, "orders.order.fulfilled");
        journals.Should().ContainSingle(journal => journal.SourceReference == afterConfirm.OrderNumber);
        Journal posted = journals.Single(journal => journal.SourceReference == afterConfirm.OrderNumber);
        posted.Lines.Sum(line => line.SignedAmount).Should().Be(0m);
    }

    [Fact]
    public async Task A_click_and_collect_order_is_collected_end_to_end()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        OrdersScenario scenario = await OrdersHarnessSetup.BuildAsync(harness);

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.InStore, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));

        Guid lineId = await harness.SendAsync(new AddOrderLineCommand(orderId, scenario.InStockItemId, null, 2m, "EA"));

        await harness.SendAsync(new ConfirmOrderCommand(orderId));

        PickTask task = await FindOpenTaskAsync(harness, lineId);

        await harness.SendAsync(new ConfirmPickCommand(task.Id, new Quantity(2m, "EA")));
        await harness.SendAsync(new PackWaveCommand(task.PickWaveId, 1, "Collection"));
        await harness.SendAsync(new ShipWaveCommand(task.PickWaveId, "Customer Collection", null));

        ShipmentConfirmation shipment = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<VumaRetailDbContext>().ShipmentConfirmations
                .FirstAsync(confirmation => confirmation.PickWaveId == task.PickWaveId));

        shipment.Carrier.Should().Be("Customer Collection");

        CompleteOrderResult completed = await harness.SendAsync(new CompleteOrderCommand(orderId));
        completed.RevenueRecognised.Should().BeTrue();
    }

    [Fact]
    public async Task A_return_of_a_shipped_line_puts_stock_back_and_reaches_the_gl()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        OrdersScenario scenario = await OrdersHarnessSetup.BuildAsync(harness);

        Guid orderId = await harness.SendAsync(new CreateOrderCommand(
            PartnerId: null, SalesChannel.InStore, OrderFulfilmentType.ClickAndCollect, scenario.LocationId,
            null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null));
        Guid lineId = await harness.SendAsync(new AddOrderLineCommand(orderId, scenario.InStockItemId, null, 4m, "EA"));
        await harness.SendAsync(new ConfirmOrderCommand(orderId));

        PickTask task = await FindOpenTaskAsync(harness, lineId);
        await ShipTaskAsync(harness, task, 4m);
        await harness.SendAsync(new CompleteOrderCommand(orderId));

        decimal balanceBeforeReturn = await BalanceAsync(harness, scenario.LocationId, scenario.InStockItemId);

        // Built from the aggregate and the completion service directly, exactly as DemoSeed does —
        // raising a return is attributed to an authorising user, and this harness signs in nobody.
        (Guid returnId, string returnNumber) = await harness.InScopeAsync(async provider =>
        {
            ISalesOrderRepository orders = provider.GetRequiredService<ISalesOrderRepository>();
            ISalesOrderReturnRepository returns = provider.GetRequiredService<ISalesOrderReturnRepository>();
            IOrderFulfilmentReader fulfilment = provider.GetRequiredService<IOrderFulfilmentReader>();
            IDocumentNumberSequence numbers = provider.GetRequiredService<IDocumentNumberSequence>();
            IClock clock = provider.GetRequiredService<IClock>();

            Domain.Orders.SalesOrder order = (await orders.FindAsync(orderId))!;
            Domain.Orders.SalesOrderLine line = order.RequireLine(lineId);

            OrderLineFulfilmentSnapshot snapshot = await fulfilment.GetLineFulfilmentAsync(line.Id, "EA");
            string number = await numbers.NextAsync(CreateOrderReturnCommandHandler.ReturnNumberSeries);

            Domain.Orders.SalesOrderReturn orderReturn = Domain.Orders.SalesOrderReturn.Raise(
                order.Id, order.TenantId, order.StoreId, order.Currency, number, "Faulty on arrival",
                UuidV7.NewGuid(), clock.UtcNow);
            orderReturn.AddLine(line, new Quantity(1m, "EA"), snapshot.FulfilledQuantity, previouslyReturned: 0m);
            returns.Add(orderReturn);

            await provider.GetRequiredService<IOrderReturnCompletionService>().CompleteAsync(orderReturn);
            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return (orderReturn.Id, orderReturn.ReturnNumber);
        });

        decimal balanceAfterReturn = await BalanceAsync(harness, scenario.LocationId, scenario.InStockItemId);
        (balanceAfterReturn - balanceBeforeReturn).Should().Be(1m);

        SalesOrderReturnResult result = await harness.QueryAsync(new GetOrderReturnQuery(returnId));
        result.Status.Should().Be(SalesOrderReturnStatus.Completed);
        result.Lines.Should().ContainSingle(line => line.StockReturn == OrderStockReturnStatus.Posted);

        List<Journal> journals = await OrdersHarnessSetup.JournalsForEventTypeAsync(harness, "orders.return.completed");
        journals.Should().ContainSingle(journal => journal.SourceReference == returnNumber);
    }

    private static async Task<PickTask> FindOpenTaskAsync(ApiHarness harness, Guid orderLineId)
        => await harness.InScopeAsync(provider =>
            provider.GetRequiredService<VumaRetailDbContext>().PickTasks
                .Where(task => task.OutboundReference == orderLineId.ToString())
                .OrderByDescending(task => task.CreatedAt)
                .FirstAsync());

    private static async Task ShipTaskAsync(ApiHarness harness, PickTask task, decimal quantity)
    {
        await harness.SendAsync(new ConfirmPickCommand(task.Id, new Quantity(quantity, "EA")));
        await harness.SendAsync(new PackWaveCommand(task.PickWaveId, 1, "Test pick"));
        await harness.SendAsync(new ShipWaveCommand(task.PickWaveId, "Test Carrier", "TRK-TEST"));
    }

    private static async Task<decimal> BalanceAsync(ApiHarness harness, Guid locationId, Guid itemId)
        => await harness.InScopeAsync(async provider =>
        {
            Domain.Inventory.StockBalance? balance = await provider.GetRequiredService<VumaRetailDbContext>()
                .StockBalances
                .FirstOrDefaultAsync(candidate => candidate.LocationId == locationId && candidate.ItemId == itemId);

            return balance?.QuantityOnHand.Value ?? 0m;
        });
}
