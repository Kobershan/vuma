using System.Text;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Imports.Commands;
using VumaRetail.Domain.Imports;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Catalog.Commands;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Partners.Commands;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Procurement;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Sales;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Queries;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Finance.Commands;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;

namespace VumaRetail.StoreServer;

/// <summary>
/// Builds a demonstrable tenant: two stores, three roles, staff with PINs, and an enrolled terminal.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/TESTING.md</c> §5 — every stage extends the seed so the whole system stays demonstrable,
/// and the seed doubles as the dataset for the Stage 31 DR drill. Stage 02 starts it with the only
/// things that exist: the platform root and identity.
/// </para>
/// <para>
/// Idempotent. Running it twice must not create a second copy of anything, because the first thing
/// anybody does with a seed script is run it again to see what it did.
/// </para>
/// </remarks>
public static class DemoSeed
{
    /// <summary>The demo tenant's fixed id, so a re-run finds what the last run made.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("01900000-0000-7000-8000-0000000000d0");

    /// <summary>Seeds the demo tenant.</summary>
    /// <param name="services">The host's service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();
        ITenantContext tenantContext = provider.GetRequiredService<ITenantContext>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        Tenant tenant = await EnsureTenantAsync(context, tenantContext, unitOfWork, cancellationToken).ConfigureAwait(false);
        tenantContext.SetTenant(tenant.Id);

        Store johannesburg = await EnsureStoreAsync(context, unitOfWork, tenant.Id, "JHB01", "Vuma Sandton", cancellationToken)
            .ConfigureAwait(false);
        await EnsureStoreAsync(context, unitOfWork, tenant.Id, "CPT02", "Vuma Claremont", cancellationToken)
            .ConfigureAwait(false);

        // Before anything else goes through the pipeline. Stage 04b's read-only guard refuses every
        // business write on an unactivated installation, which is correct in production and would
        // otherwise mean the demo seeder produced a tenant with no users in it.
        await EnsureActivationAsync(provider, tenant.Id, johannesburg.Id, cancellationToken)
            .ConfigureAwait(false);

        Guid ownerRole = await EnsureRoleAsync(provider, "Owner", AllPermissions(provider), cancellationToken)
            .ConfigureAwait(false);

        Guid managerRole = await EnsureRoleAsync(
            provider,
            "Store Manager",
            [
                PlatformPermissions.StoreView,
                PlatformPermissions.AuditView,
                IdentityPermissions.UserView,
                IdentityPermissions.UserManage,
                IdentityPermissions.RoleView,
                IdentityPermissions.TerminalView,
                IdentityPermissions.TerminalEnrol,
            ],
            cancellationToken).ConfigureAwait(false);

        Guid cashierRole = await EnsureRoleAsync(
            provider,
            "Cashier",
            [PlatformPermissions.StoreView],
            cancellationToken).ConfigureAwait(false);

        await EnsureUserAsync(provider, "owner", "Thandi Mokoena", "ChangeMe-Owner-2026", ownerRole, null, null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "manager", "Riaan de Villiers", "ChangeMe-Manager-2026", managerRole, johannesburg.Id, "4821", cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "cashier1", "Naledi Dlamini", "ChangeMe-Cashier-2026", cashierRole, johannesburg.Id, "1174", cancellationToken)
            .ConfigureAwait(false);

        await EnsureTerminalAsync(provider, context, johannesburg.Id, "T01", "Front counter 1", cancellationToken)
            .ConfigureAwait(false);

        // Stage 06: master data. Two base units, one derived; an item with no variants and one with
        // two, so the demo shows both of Barcode's attachment rules; and two partners, one of each type.
        Guid each = await EnsureUnitOfMeasureAsync(provider, context, "EA", "Each", UnitOfMeasureType.Count, cancellationToken)
            .ConfigureAwait(false);
        await EnsureUnitOfMeasureAsync(provider, context, "KG", "Kilogram", UnitOfMeasureType.Weight, cancellationToken)
            .ConfigureAwait(false);
        await EnsureDerivedUnitOfMeasureAsync(provider, context, "BOX12", "Box of 12", each, 12m, cancellationToken)
            .ConfigureAwait(false);

        Guid milk = await EnsureItemAsync(
            provider, context, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each, "Fresh full cream milk, 2 litre bottle", "STANDARD", cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, milk, null, "6009880123456", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);

        Guid shirt = await EnsureItemAsync(
            provider, context, "SHIRT", "Vuma branded T-shirt", ItemType.Stock, each, "Cotton crew-neck T-shirt", "STANDARD", cancellationToken)
            .ConfigureAwait(false);
        Guid shirtMedRed = await EnsureVariantAsync(
            provider, context, shirt, "SHIRT-M-RED", [new VariantAttribute("Size", "M"), new VariantAttribute("Colour", "Red")], cancellationToken)
            .ConfigureAwait(false);
        Guid shirtLgeBlue = await EnsureVariantAsync(
            provider, context, shirt, "SHIRT-L-BLUE", [new VariantAttribute("Size", "L"), new VariantAttribute("Colour", "Blue")], cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, null, shirtMedRed, "6009880234561", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, null, shirtLgeBlue, "6009880234578", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);

        Guid freshFarm = await EnsurePartnerAsync(
            provider, context, "FRESHFARM", "Fresh Farm Distributors", PartnerType.Supplier, "orders@freshfarm.example", cancellationToken)
            .ConfigureAwait(false);
        Guid corpClient = await EnsurePartnerAsync(
            provider, context, "CORPCLIENT", "Corporate Client (Pty) Ltd", PartnerType.Customer, "accounts@corpclient.example", cancellationToken)
            .ConfigureAwait(false);

        await SeedFinanceAsync(provider, context, cancellationToken).ConfigureAwait(false);
        await SeedInventoryAsync(provider, context, milk, cancellationToken).ConfigureAwait(false);
        await SeedPosAsync(provider, context, johannesburg.Id, milk, cancellationToken).ConfigureAwait(false);
        await SeedSalesAsync(
            provider, context, johannesburg.Id, milk, shirtMedRed, cancellationToken).ConfigureAwait(false);
        await SeedImportsAsync(provider, context, cancellationToken).ConfigureAwait(false);
        await SeedProcurementAsync(provider, context, freshFarm, milk, cancellationToken)
            .ConfigureAwait(false);
        await SeedWarehouseAsync(provider, context, milk, cancellationToken).ConfigureAwait(false);
        await SeedOrdersAsync(provider, context, corpClient, milk, shirtMedRed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 14: one delivery order with two lines — one that allocates and ships straight off
    /// existing bin stock, one that deliberately exceeds what is binned so it backorders — a top-up
    /// receipt and putaway that clears the backorder on an explicit reattempt, a click &amp; collect
    /// order collected end to end, and a return of the first order's shipped milk line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CreateOrderCommand</c>, <c>AddOrderLineCommand</c>, <c>ConfirmOrderCommand</c>,
    /// <c>ReattemptBackorderedAllocationsCommand</c> and <c>CompleteOrderCommand</c> all go through the
    /// real dispatcher, exactly like <see cref="SeedWarehouseAsync"/>'s pick/pack/ship — none of them
    /// attributes anything to a person, so nothing here is refused by a principal check. The order
    /// return is the one step built from the aggregate and <see cref="IOrderReturnCompletionService"/>
    /// directly, for the same reason <see cref="SeedSalesAsync"/>'s return is: raising it is attributed
    /// to the authorising user (<c>OrdersActor</c>), and the seed runs as a system principal.
    /// </para>
    /// <para>
    /// <b>The backorder is real, not staged.</b> The shirt variant has never had a unit of stock
    /// anywhere in this seed, so line 2 backorders in full on confirm. Clearing it needs both a
    /// location-level receipt <em>and</em> a putaway into a bin — <c>OrderAllocation</c> only trusts
    /// what a bin can actually hand over (see its own remarks), so a receipt alone would leave the
    /// reattempt finding nothing to allocate, which is exactly the case this seed exists to exercise
    /// honestly rather than assume.
    /// </para>
    /// </remarks>
    /// <param name="provider">The scoped provider.</param>
    /// <param name="context">The database context.</param>
    /// <param name="customerId">The seeded corporate customer.</param>
    /// <param name="milkItemId">The item for the line that allocates straight away.</param>
    /// <param name="shirtVariantId">The variant for the line that backorders.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedOrdersAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid customerId,
        Guid milkItemId,
        Guid shirtVariantId,
        CancellationToken cancellationToken)
    {
        if (await context.SalesOrders.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The two new financial events this stage raises (business rule 5, ADR-093/094). Reuses the
        // accounts SeedFinanceAsync and SeedSalesAsync already opened — the debtor an order's revenue
        // is recognised against, the same sales and VAT control accounts a till sale posts to, and the
        // same sales-returns contra-revenue account a till return posts to. The stock side of the
        // return needs no new rule at all: StockReferenceType.OrderReturn shares
        // StockMovementType.SalesReturn, which already reaches the GL through
        // inventory.sale.returned (see that type's own remarks).
        Guid debtors = await EnsureAccountAsync(
            provider, context, "1100", "Trade debtors", AccountType.Asset,
            ControlAccountType.AccountsReceivable, cancellationToken).ConfigureAwait(false);
        Guid sales = await EnsureAccountAsync(
            provider, context, "4000", "Sales", AccountType.Revenue,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid salesReturns = await EnsureAccountAsync(
            provider, context, "4010", "Sales returns", AccountType.Revenue,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid vatControl = await EnsureAccountAsync(
            provider, context, "2200", "VAT control", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, FinancialOrderFulfilmentEventPublisher.OrderFulfilledEventType,
            "Order revenue recognised",
            [
                new PostingRuleLineInput(debtors, NormalBalance.Debit, "Gross", InheritDimensions: false, "Trade debtors"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, FinancialOrderReturnEventPublisher.OrderReturnCompletedEventType,
            "Order return refund due",
            [
                new PostingRuleLineInput(salesReturns, NormalBalance.Debit, "Net", InheritDimensions: true, "Sales returns"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Output VAT reversed"),
                new PostingRuleLineInput(debtors, NormalBalance.Credit, "Gross", InheritDimensions: false, "Trade debtors"),
            ],
            cancellationToken).ConfigureAwait(false);

        StockLocation warehouse = await context.StockLocations
            .FirstAsync(location => location.Code == "MAIN", cancellationToken)
            .ConfigureAwait(false);

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();
        IClock clock = provider.GetRequiredService<IClock>();

        // Order 1: phone order, delivered, two lines. Milk has 62 EA already binned by
        // SeedWarehouseAsync (35 in A-01, 27 in A-02) — five is comfortably inside that. The shirt
        // variant has never been received anywhere in this seed, so it backorders in full.
        Guid order1 = await dispatcher
            .SendAsync(
                new CreateOrderCommand(
                    customerId, SalesChannel.Phone, OrderFulfilmentType.Delivery, warehouse.Id,
                    "12 Rivonia Road", null, "Sandton", "Gauteng", "2196", "ZA", "ZAR", RequestedFulfilmentDate: null),
                cancellationToken)
            .ConfigureAwait(false);

        Guid milkLineId = await dispatcher
            .SendAsync(new AddOrderLineCommand(order1, milkItemId, null, 5m, "EA"), cancellationToken)
            .ConfigureAwait(false);
        Guid shirtLineId = await dispatcher
            .SendAsync(new AddOrderLineCommand(order1, null, shirtVariantId, 3m, "EA"), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher.SendAsync(new ConfirmOrderCommand(order1), cancellationToken).ConfigureAwait(false);

        PickTask milkTask = await context.PickTasks
            .OrderByDescending(task => task.CreatedAt)
            .FirstAsync(task => task.OutboundReference == milkLineId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        await ShipPickTaskAsync(dispatcher, milkTask, 5m, "Vuma Delivery", "TRK-ORD-001", cancellationToken)
            .ConfigureAwait(false);

        // Clear the backorder: receive the shirt into stock, shelve it into the empty A-03 bin, then
        // reattempt — an explicit call, exactly as business rule 3 requires.
        await dispatcher
            .SendAsync(
                new ReceiveStockCommand(warehouse.Id, null, shirtVariantId, new Quantity(10m, "EA"), new Money(120.00m, "ZAR"), "Backorder top-up"),
                cancellationToken)
            .ConfigureAwait(false);

        Domain.Warehouse.Bin sparseBin = await context.Bins.FirstAsync(bin => bin.Code == "A-03", cancellationToken).ConfigureAwait(false);

        Guid shirtPutawayId = await dispatcher
            .SendAsync(
                new OpenPutawayTaskCommand(warehouse.Id, null, shirtVariantId, new Quantity(10m, "EA"), PutawaySourceReferenceType.ManualReceipt),
                cancellationToken)
            .ConfigureAwait(false);
        await dispatcher
            .SendAsync(new ConfirmPutawayCommand(shirtPutawayId, sparseBin.Id, new Quantity(10m, "EA")), cancellationToken)
            .ConfigureAwait(false);

        ReattemptBackorderedAllocationsResult reattempt = await dispatcher
            .SendAsync(new ReattemptBackorderedAllocationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        PickTask shirtTask = await context.PickTasks
            .OrderByDescending(task => task.CreatedAt)
            .FirstAsync(task => task.OutboundReference == shirtLineId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        await ShipPickTaskAsync(dispatcher, shirtTask, 3m, "Vuma Delivery", "TRK-ORD-002", cancellationToken)
            .ConfigureAwait(false);

        CompleteOrderResult completed1 = await dispatcher
            .SendAsync(new CompleteOrderCommand(order1), cancellationToken)
            .ConfigureAwait(false);

        // Order 2: a walk-in counter order, collected rather than delivered. Same fulfilment pipeline
        // as delivery (business rule 8) — the only difference is no delivery address and the shipment's
        // own carrier label.
        Guid order2 = await dispatcher
            .SendAsync(
                new CreateOrderCommand(
                    PartnerId: null, SalesChannel.InStore, OrderFulfilmentType.ClickAndCollect, warehouse.Id,
                    null, null, null, null, null, null, "ZAR", RequestedFulfilmentDate: null),
                cancellationToken)
            .ConfigureAwait(false);

        Guid collectLineId = await dispatcher
            .SendAsync(new AddOrderLineCommand(order2, milkItemId, null, 3m, "EA"), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher.SendAsync(new ConfirmOrderCommand(order2), cancellationToken).ConfigureAwait(false);

        PickTask collectTask = await context.PickTasks
            .OrderByDescending(task => task.CreatedAt)
            .FirstAsync(task => task.OutboundReference == collectLineId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        await ShipPickTaskAsync(dispatcher, collectTask, 3m, "Customer Collection", null, cancellationToken)
            .ConfigureAwait(false);

        CompleteOrderResult completed2 = await dispatcher
            .SendAsync(new CompleteOrderCommand(order2), cancellationToken)
            .ConfigureAwait(false);

        // The return: 1 of the 5 EA milk shipped on order 1 comes back. Built from the aggregate and
        // the completion service directly — see this method's own remarks on why.
        SalesOrder orderedForReturn = await context.SalesOrders
            .Include(order => order.Lines)
            .FirstAsync(order => order.Id == order1, cancellationToken)
            .ConfigureAwait(false);

        SalesOrderLine returnedLine = orderedForReturn.RequireLine(milkLineId);

        User cashier = await provider.GetRequiredService<IUserRepository>()
            .FindByUserNameAsync("cashier1", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Seeded user 'cashier1' was not found.");

        IOrderFulfilmentReader fulfilmentReader = provider.GetRequiredService<IOrderFulfilmentReader>();

        OrderLineFulfilmentSnapshot returnSnapshot = await fulfilmentReader
            .GetLineFulfilmentAsync(returnedLine.Id, returnedLine.RequestedQuantity.UnitOfMeasure, cancellationToken)
            .ConfigureAwait(false);

        string orderReturnNumber = await provider.GetRequiredService<IDocumentNumberSequence>()
            .NextAsync(CreateOrderReturnCommandHandler.ReturnNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        SalesOrderReturn orderReturn = SalesOrderReturn.Raise(
            orderedForReturn.Id, orderedForReturn.TenantId, orderedForReturn.StoreId, orderedForReturn.Currency,
            orderReturnNumber, "Customer changed their mind", cashier.Id, clock.UtcNow);

        orderReturn.AddLine(returnedLine, new Quantity(1m, "EA"), returnSnapshot.FulfilledQuantity, previouslyReturned: 0m);

        provider.GetRequiredService<ISalesOrderReturnRepository>().Add(orderReturn);

        await provider.GetRequiredService<IOrderReturnCompletionService>()
            .CompleteAsync(orderReturn, cancellationToken)
            .ConfigureAwait(false);

        await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"Orders: {orderedForReturn.OrderNumber} confirmed with a real backorder and a real reattempt "
            + $"({reattempt.OrdersReallocated} order(s), {reattempt.LinesReallocated} line(s) reallocated), "
            + $"fulfilled and revenue-recognised ({completed1.RevenueRecognised}, {completed1.Gross}); "
            + $"a click & collect order fulfilled and recognised ({completed2.RevenueRecognised}, {completed2.Gross}); "
            + $"return {orderReturn.ReturnNumber} completed for {orderReturn.Gross}.");
    }

    /// <summary>Picks, packs and ships one order line's allocated task through Stage 13's own commands.</summary>
    private static async Task ShipPickTaskAsync(
        IDispatcher dispatcher, PickTask task, decimal pickedQuantity, string carrier, string? trackingNumber, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ConfirmPickCommand(task.Id, new Quantity(pickedQuantity, "EA")), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher
            .SendAsync(new PackWaveCommand(task.PickWaveId, 1, "Order pick"), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher
            .SendAsync(new ShipWaveCommand(task.PickWaveId, carrier, trackingNumber), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 13: a storage zone and three bins under <c>MAIN</c>, unbinned stock (from
    /// <see cref="SeedInventoryAsync"/>'s opening receipt and <see cref="SeedProcurementAsync"/>'s GRN)
    /// shelved into two of them, a wave picked, packed and shipped from the larger one, and a cycle
    /// count on the other that finds three fewer than the system expects.
    /// </summary>
    /// <remarks>
    /// Guarded on the zone table being empty rather than per-row, the same shape every other
    /// <c>Seed*Async</c> method in this file uses.
    /// </remarks>
    private static async Task SeedWarehouseAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (await context.Zones.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        StockLocation warehouse = await context.StockLocations
            .FirstAsync(location => location.Code == "MAIN", cancellationToken)
            .ConfigureAwait(false);

        Guid zoneId = await dispatcher
            .SendAsync(new CreateZoneCommand(warehouse.Id, "STOR-A", "Storage aisle A", ZoneType.Storage), cancellationToken)
            .ConfigureAwait(false);

        Guid binA01 = await dispatcher
            .SendAsync(new CreateBinCommand(zoneId, "A-01", "Aisle A, shelf 1", BinType.Shelf), cancellationToken)
            .ConfigureAwait(false);
        Guid binA02 = await dispatcher
            .SendAsync(new CreateBinCommand(zoneId, "A-02", "Aisle A, shelf 2", BinType.Shelf), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher
            .SendAsync(new CreateBinCommand(zoneId, "A-03", "Aisle A, shelf 3", BinType.Shelf), cancellationToken)
            .ConfigureAwait(false);

        // Shelve some of the unbinned stock SeedInventoryAsync and SeedProcurementAsync already put on
        // the floor — 60 into the bin the pick wave below will draw from, 30 into the second, split
        // across two confirmations against one task, which is what a picker splitting a pallet looks
        // like (business rule 8 — the confirmed bin need not match any single suggestion).
        Guid putawayId = await dispatcher
            .SendAsync(
                new OpenPutawayTaskCommand(
                    warehouse.Id, itemId, null, new Quantity(90m, "EA"), PutawaySourceReferenceType.ManualReceipt),
                cancellationToken)
            .ConfigureAwait(false);

        await dispatcher.SendAsync(new ConfirmPutawayCommand(putawayId, binA01, new Quantity(60m, "EA")), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher.SendAsync(new ConfirmPutawayCommand(putawayId, binA02, new Quantity(30m, "EA")), cancellationToken)
            .ConfigureAwait(false);

        // Pick, pack and ship 25 units — Order Management (Stage 14) does not exist yet, so the demand
        // is a caller-supplied line, exactly as the stage document's "what this stage does not own" says.
        Guid waveId = await dispatcher.SendAsync(new OpenPickWaveCommand(warehouse.Id), cancellationToken)
            .ConfigureAwait(false);
        Guid pickTaskId = await dispatcher
            .SendAsync(new AddPickTaskCommand(waveId, itemId, null, new Quantity(25m, "EA"), "SO-DEMO-3001"), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher.SendAsync(new ReleasePickWaveCommand(waveId), cancellationToken).ConfigureAwait(false);
        await dispatcher.SendAsync(new ConfirmPickCommand(pickTaskId, new Quantity(25m, "EA")), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher.SendAsync(new PackWaveCommand(waveId, 2, "Two cartons, one pallet"), cancellationToken)
            .ConfigureAwait(false);
        Guid shipmentId = await dispatcher
            .SendAsync(new ShipWaveCommand(waveId, "Vuma Logistics", "TRK-DEMO-001"), cancellationToken)
            .ConfigureAwait(false);

        // A cycle count on the bin the wave never touched finds three fewer than the system expects —
        // business rule 4: the variance corrects the location's own balance, not only the bin's.
        Guid cycleCountId = await dispatcher.SendAsync(new OpenCycleCountCommand(warehouse.Id, zoneId), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher
            .SendAsync(new RecordCycleCountCommand(cycleCountId, binA02, itemId, null, new Quantity(27m, "EA")), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher.SendAsync(new FinalizeCycleCountCommand(cycleCountId), cancellationToken).ConfigureAwait(false);

        BinStockResult binOneStock = await dispatcher.QueryAsync(new GetBinStockQuery(binA01, itemId, null), cancellationToken)
            .ConfigureAwait(false);
        BinStockResult binTwoStock = await dispatcher.QueryAsync(new GetBinStockQuery(binA02, itemId, null), cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"Warehouse: zone {zoneId} shelved into A-01 ({binOneStock.QuantityOnHand}) and A-02 "
            + $"({binTwoStock.QuantityOnHand}); wave {waveId} shipped 25 EA as {shipmentId}; "
            + $"cycle count {cycleCountId} found A-02 three short and posted the variance.");
    }

    /// <summary>
    /// Seeds Stage 11: one supplier file that was imported and kept, and one that was imported and
    /// taken back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both go through the real dispatcher — upload, validate, commit — because the pipeline is the
    /// thing being demonstrated and a batch inserted by hand would show the tables without showing that
    /// any of it works. The CSV is a byte array in this file rather than a fixture on disk for the same
    /// reason the rest of the seeder is self-contained: a demo that depends on a file being deployed
    /// next to it is a demo that fails on somebody else's machine.
    /// </para>
    /// <para>
    /// The second batch exists so the demo can answer the question R5's rollback promise raises and
    /// nothing else in the seed data can: what a taken-back import actually looks like afterwards. Its
    /// partner is soft-deleted, so it is absent from every read and still on disk with the batch that
    /// removed it and the reason somebody gave — which is the whole of ADR-076 in two rows.
    /// </para>
    /// <para>
    /// Guarded on the batch table being empty rather than per-batch, because re-running the seeder
    /// would otherwise be refused by the content-hash check (<c>IMPORTS_FILE_ALREADY_COMMITTED</c>) —
    /// correctly, since it is the same file, which is exactly what that check is for.
    /// </para>
    /// </remarks>
    /// <param name="provider">The scoped provider.</param>
    /// <param name="context">The database context.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedImportsAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.ImportBatches.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        // Headers a person did not have to touch: "Supplier Code", "Supplier Name" and "E-Mail" are all
        // aliases the Suppliers catalogue already knows, so this file maps itself on upload.
        ImportBatchCreated kept = await dispatcher.SendAsync(
            new CreateImportBatchCommand(
                ImportTargetKind.Suppliers,
                ImportSourceFormat.Csv,
                "suppliers-march.csv",
                Encoding.UTF8.GetBytes(
                    "Supplier Code,Supplier Name,E-Mail,Telephone\n"
                    + "COLDCHAIN,Cold Chain Logistics,accounts@coldchain.example,021 555 0142\n"
                    + "PACKRITE,Pack Rite Packaging,orders@packrite.example,011 555 0188\n")),
            cancellationToken).ConfigureAwait(false);

        await dispatcher.SendAsync(new ValidateImportBatchCommand(kept.BatchId), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher.SendAsync(new CommitImportBatchCommand(kept.BatchId), cancellationToken)
            .ConfigureAwait(false);

        ImportBatchCreated undone = await dispatcher.SendAsync(
            new CreateImportBatchCommand(
                ImportTargetKind.Suppliers,
                ImportSourceFormat.Csv,
                "suppliers-wrong-branch.csv",
                Encoding.UTF8.GetBytes(
                    "Supplier Code,Supplier Name,E-Mail\n"
                    + "WRONGCO,Wrong Branch Trading,accounts@wrongco.example\n")),
            cancellationToken).ConfigureAwait(false);

        await dispatcher.SendAsync(new ValidateImportBatchCommand(undone.BatchId), cancellationToken)
            .ConfigureAwait(false);
        await dispatcher.SendAsync(new CommitImportBatchCommand(undone.BatchId), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher.SendAsync(
            new RollbackImportBatchCommand(
                undone.BatchId, "Uploaded against the wrong branch's supplier list."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 10: a retail price list, two live specials, and one completed return against the
    /// sale Stage 09 seeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The price list is what makes the demo answer "what should this cost" rather than "what did
    /// somebody type". Prices are authored tax-inclusive, which is how a South African shelf price is
    /// quoted and what <c>PricesIncludeTax</c> exists to record — the <c>STANDARD</c> rule
    /// <see cref="SeedFinanceAsync"/> writes then splits R59.99 into net and VAT without the till
    /// having to know which kind of list it read.
    /// </para>
    /// <para>
    /// Two promotions rather than one, and deliberately of different kinds: a multibuy on milk, which
    /// only fires above a quantity, and a percentage off a variant, which fires on every unit. Between
    /// them they exercise both halves of the engine's ordering, and a demo with one special cannot show
    /// that priority does anything.
    /// </para>
    /// <para>
    /// The return is built from the domain factories and <see cref="ISalesReturnCompletionService"/>
    /// rather than through the dispatcher, for the same reason <see cref="SeedPosAsync"/> is: a return
    /// is attributed to the person who authorised it (<c>SalesActor</c>), and the seed runs as a system
    /// principal. Going through the completion service still exercises what matters — the stock goes
    /// back at what it left at, and <c>sales.return.completed</c> posts through the rule below.
    /// </para>
    /// </remarks>
    private static async Task SeedSalesAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid storeId,
        Guid milkItemId,
        Guid shirtVariantId,
        CancellationToken cancellationToken)
    {
        IClock clock = provider.GetRequiredService<IClock>();
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // A sales-returns contra-revenue account rather than debiting Sales directly. A shop that
        // cannot see what it refunded cannot tell a bad month from a bad product, and the two figures
        // net to the same revenue either way.
        Guid salesReturns = await EnsureAccountAsync(
            provider, context, "4010", "Sales returns", AccountType.Revenue,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        Guid bank = await EnsureAccountAsync(
            provider, context, "1200", "Bank — cheque account", AccountType.Asset,
            ControlAccountType.Bank, cancellationToken).ConfigureAwait(false);

        Guid vatControl = await EnsureAccountAsync(
            provider, context, "2200", "VAT control", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        // The exact mirror of `pos.sale.tendered`: what was debited is credited and the other way
        // round. The amounts arrive positive and the rule decides the sides — a sales module that
        // negated them would be making a debit-and-credit decision, which §7 rule 12 gives it no
        // standing to make.
        await EnsurePostingRuleAsync(
            provider, context, FinancialSalesReturnEventPublisher.SalesReturnCompletedEventType,
            "Refund at the counter",
            [
                new PostingRuleLineInput(salesReturns, NormalBalance.Debit, "Net", InheritDimensions: true, "Sales returns"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Output VAT reversed"),
                new PostingRuleLineInput(bank, NormalBalance.Credit, "Gross", InheritDimensions: false, "Bank"),
            ],
            cancellationToken).ConfigureAwait(false);

        // The stock side of the same return, and the exact mirror of `inventory.sale.issued`. It gets
        // its own event type rather than reusing `inventory.receipt.posted` because a return credits
        // cost of sales while a supplier delivery credits goods-received-not-invoiced — the same
        // movement of stock against two entirely different accounts.
        Guid inventory = await EnsureAccountAsync(
            provider, context, "1300", "Inventory on hand", AccountType.Asset,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        Guid costOfSales = await EnsureAccountAsync(
            provider, context, "5000", "Cost of sales", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.sale.returned", "Stock back on the shelf from a return",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(costOfSales, NormalBalance.Credit, "Value", InheritDimensions: true, "Cost of sales"),
            ],
            cancellationToken).ConfigureAwait(false);

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        if (!await context.PriceLists.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid retail = await dispatcher.SendAsync(
                new CreatePriceListCommand(
                    "RETAIL", "Shelf prices", "ZAR", PriceListKind.Retail,
                    PricesIncludeTax: true, Priority: 0, today.AddYears(-1)),
                cancellationToken).ConfigureAwait(false);

            await dispatcher.SendAsync(
                new SetPriceListLineCommand(retail, milkItemId, null, new Money(59.99m, "ZAR")),
                cancellationToken).ConfigureAwait(false);

            // A quantity break, so the demo has something to show that a single price cannot: buy six
            // and the price changes, and the resolver explains why.
            await dispatcher.SendAsync(
                new SetPriceListLineCommand(retail, milkItemId, null, new Money(54.99m, "ZAR"), 6m),
                cancellationToken).ConfigureAwait(false);

            await dispatcher.SendAsync(
                new SetPriceListLineCommand(retail, null, shirtVariantId, new Money(249.00m, "ZAR")),
                cancellationToken).ConfigureAwait(false);
        }

        if (!await context.Promotions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid multibuy = await dispatcher.SendAsync(
                new CreatePromotionCommand(
                    "MILK-3-FOR-150", "3 litres of milk for R150", PromotionKind.MultibuyForAmount,
                    today.AddDays(-7), today.AddDays(30),
                    RewardAmount: new Money(150m, "ZAR"), RequiredQuantity: 3m, Priority: 10),
                cancellationToken).ConfigureAwait(false);

            await dispatcher.SendAsync(
                new AddPromotionLineCommand(multibuy, milkItemId), cancellationToken).ConfigureAwait(false);

            Guid weekend = await dispatcher.SendAsync(
                new CreatePromotionCommand(
                    "SHIRT-WEEKEND", "20% off shirts, weekends only", PromotionKind.PercentageOff,
                    today.AddDays(-7), today.AddDays(30),
                    DiscountPercentage: 20m, Priority: 5, Days: PromotionDays.Weekend),
                cancellationToken).ConfigureAwait(false);

            await dispatcher.SendAsync(
                new AddPromotionLineCommand(weekend, null, shirtVariantId),
                cancellationToken).ConfigureAwait(false);
        }

        if (await context.SalesReturns.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Sale? sale = await context.Sales
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Status == SaleStatus.Completed, cancellationToken)
            .ConfigureAwait(false);

        User? cashier = await provider.GetRequiredService<IUserRepository>()
            .FindByUserNameAsync("cashier1", cancellationToken)
            .ConfigureAwait(false);

        if (sale is null || cashier is null || sale.Lines.Count == 0)
        {
            return;
        }

        string returnNumber = await provider.GetRequiredService<IDocumentNumberSequence>()
            .NextAsync(CreateSalesReturnCommandHandler.ReturnNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        SalesReturn salesReturn = SalesReturn.Raise(
            sale, returnNumber, "Bottle leaking", TenderType.Cash, cashier.Id, clock.UtcNow);

        // One of the two sold, so the demo shows a partial return: the refund is half the line to the
        // cent, and the sale it came off is untouched.
        salesReturn.AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        provider.GetRequiredService<ISalesReturnRepository>().Add(salesReturn);

        await provider.GetRequiredService<ISalesReturnCompletionService>()
            .CompleteAsync(salesReturn, cancellationToken)
            .ConfigureAwait(false);

        await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"Return {salesReturn.ReturnNumber} completed: {salesReturn.Gross} refunded "
            + $"({salesReturn.Tax} tax), stock back on the shelf.");
    }

    /// <summary>
    /// Seeds Stage 08: two stock locations, the accounts stock movements post to, the posting rules
    /// that connect them, and an opening receipt so the demo has stock on hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two locations rather than one, because a transfer needs somewhere to go — a single-location
    /// demo cannot exercise the one movement type that posts two correlated ledger entries.
    /// </para>
    /// <para>
    /// <b>No rule is seeded for the two transfer event types.</b> That is the demonstration, not an
    /// omission: with one inventory account a transfer's two sides would debit and credit the same
    /// account for the same amount, so the correct posting is no posting at all. A tenant running
    /// per-location inventory accounts defines the rules and the same events start posting, with no
    /// code change. It also exercises the path where a movement is recorded with no posting rule
    /// configured, which logs a warning and leaves the stock ledger correct (ADR-070).
    /// </para>
    /// </remarks>
    private static async Task SeedInventoryAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        Guid inventory = await EnsureAccountAsync(
            provider, context, "1300", "Inventory on hand", AccountType.Asset,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        // Goods received but not yet invoiced. A receipt increases stock before the supplier's invoice
        // arrives, and the credit has to land somewhere until Stage 12's three-way match clears it.
        Guid grni = await EnsureAccountAsync(
            provider, context, "2150", "Goods received not invoiced", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid costOfSales = await EnsureAccountAsync(
            provider, context, "5000", "Cost of sales", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid stockAdjustments = await EnsureAccountAsync(
            provider, context, "5100", "Stock adjustments", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        // Separate from adjustments on purpose: a documented write-off and an unexplained count
        // difference are different questions to a business, and merging them hides the second.
        Guid shrinkage = await EnsureAccountAsync(
            provider, context, "5200", "Stock shrinkage", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.receipt.posted", "Stock received",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(grni, NormalBalance.Credit, "Value", InheritDimensions: false, "Goods received not invoiced"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.sale.issued", "Stock issued for a sale",
            [
                new PostingRuleLineInput(costOfSales, NormalBalance.Debit, "Value", InheritDimensions: true, "Cost of sales"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.adjustment.decrease", "Stock written off",
            [
                new PostingRuleLineInput(stockAdjustments, NormalBalance.Debit, "Value", InheritDimensions: true, "Stock adjustments"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.adjustment.increase", "Stock written on",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(stockAdjustments, NormalBalance.Credit, "Value", InheritDimensions: true, "Stock adjustments"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.stocktake.shortage", "Stocktake counted less than the system expected",
            [
                new PostingRuleLineInput(shrinkage, NormalBalance.Debit, "Value", InheritDimensions: true, "Stock shrinkage"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.stocktake.surplus", "Stocktake counted more than the system expected",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(shrinkage, NormalBalance.Credit, "Value", InheritDimensions: true, "Stock shrinkage"),
            ],
            cancellationToken).ConfigureAwait(false);

        Guid warehouse = await EnsureStockLocationAsync(
            provider, context, "MAIN", "Sandton back room", StockLocationType.Warehouse, cancellationToken)
            .ConfigureAwait(false);
        await EnsureStockLocationAsync(
            provider, context, "FLOOR", "Sandton sales floor", StockLocationType.SalesFloor, cancellationToken)
            .ConfigureAwait(false);

        // One opening receipt, so a freshly seeded demo has stock on hand to sell, transfer and count
        // rather than an empty ledger that refuses every outbound movement.
        if (!await context.StockLedgerEntries.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await provider
                .GetRequiredService<IDispatcher>()
                .SendAsync(
                    new ReceiveStockCommand(
                        warehouse,
                        itemId,
                        null,
                        new Quantity(40m, "EA"),
                        new Money(42.50m, "ZAR"),
                        "Opening stock"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Seeds Stage 09: an open till session on the demo terminal, and one completed sale through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from the domain factories and <see cref="ISaleCompletionService"/> rather than through the
    /// dispatcher, unlike most of this seed. POS attributes every action to the authenticated operator
    /// and the originating terminal (<c>PosActor</c>), and the seed runs as a system principal with no
    /// terminal — so dispatching <c>OpenSaleCommand</c> here would be refused, correctly. Going through
    /// the completion service still exercises the parts that matter: the stock comes off the ledger and
    /// the <c>pos.sale.tendered</c> event posts through the rule seeded in <see cref="SeedFinanceAsync"/>.
    /// </para>
    /// <para>
    /// The sale is rung up against <c>MAIN</c>, which is where <see cref="SeedInventoryAsync"/>'s
    /// opening receipt put the stock. Selling from <c>FLOOR</c> would demonstrate ADR-073's refused-issue
    /// path instead, which is a real thing to look at but a strange default for a demo — the first
    /// question anybody asks of a seeded database is "does a normal sale work".
    /// </para>
    /// </remarks>
    private static async Task SeedPosAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid storeId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (await context.Sales.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Terminal? terminal = await provider.GetRequiredService<ITerminalRepository>()
            .FindByCodeAsync(storeId, "T01", cancellationToken)
            .ConfigureAwait(false);

        User? cashier = await provider.GetRequiredService<IUserRepository>()
            .FindByUserNameAsync("cashier1", cancellationToken)
            .ConfigureAwait(false);

        StockLocation? location = await context.StockLocations
            .FirstOrDefaultAsync(candidate => candidate.Code == "MAIN", cancellationToken)
            .ConfigureAwait(false);

        if (terminal is null || cashier is null || location is null)
        {
            return;
        }

        IClock clock = provider.GetRequiredService<IClock>();

        TillSession session = TillSession.Open(
            DemoTenantId, storeId, terminal.Id, cashier.Id, new Money(500m, "ZAR"), clock.UtcNow);

        provider.GetRequiredService<ITillSessionRepository>().Add(session);

        string saleNumber = await provider.GetRequiredService<IDocumentNumberSequence>()
            .NextAsync(OpenSaleCommandHandler.SaleNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        Sale sale = Sale.Open(
            UuidV7.NewGuid(),
            DemoTenantId,
            storeId,
            saleNumber,
            session,
            cashier.Id,
            location.Id,
            customerId: null,
            "ZAR",
            clock.UtcNow);

        // R59.99 each, two of them, priced through the rules engine exactly as a till would — the
        // STANDARD rule SeedFinanceAsync writes is 15% inclusive, so R119.98 gross is R104.33 net.
        Money unitPrice = new(59.99m, "ZAR");
        Quantity quantity = new(2m, "EA");

        TaxCalculation tax = await provider.GetRequiredService<ITaxCalculator>()
            .CalculateAsync(
                "STANDARD",
                (unitPrice * quantity.Value).RoundToCurrencyScale(),
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                cancellationToken)
            .ConfigureAwait(false);

        sale.AddLine(SaleLine.Ring(
            DemoTenantId,
            storeId,
            sale.Id,
            sale.NextLineNumber,
            itemId,
            null,
            "Full cream milk 2L",
            quantity,
            unitPrice,
            Money.Zero("ZAR"),
            tax.TaxCode,
            tax.NetAmount,
            tax.TaxAmount,
            tax.GrossAmount));

        sale.AddTender(SaleTender.Capture(
            DemoTenantId, storeId, sale.Id, TenderType.Cash, new Money(150m, "ZAR"), null, clock.UtcNow));

        provider.GetRequiredService<ISaleRepository>().Add(sale);

        await provider.GetRequiredService<ISaleCompletionService>()
            .CompleteAsync(sale, cancellationToken)
            .ConfigureAwait(false);

        provider.GetRequiredService<IReceiptPrintRepository>().Add(ReceiptPrint.Record(
            DemoTenantId, storeId, sale.Id, cashier.Id, terminal.Id, isReprint: false, reason: null, clock.UtcNow));

        await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Sale {sale.SaleNumber} completed: {sale.Gross} tendered, {sale.ChangeGiven} change.");
    }

    /// <summary>
    /// Seeds Stage 12: one complete buying chain, from a requisition somebody raised to a supplier
    /// invoice somebody released for payment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built through the aggregates and the completion service rather than through the dispatcher, for
    /// the reason <see cref="SeedSalesAsync"/> and <see cref="SeedPosAsync"/> both give: approving a
    /// requisition, awarding a quote and releasing an invoice are each attributed to the person who
    /// authorised them (<c>ProcurementActor</c>), and the seed runs as a system principal. Going
    /// through the completion service still exercises what matters — the stock really arrives at the
    /// order's cost, and <c>procurement.invoice.matched</c> really posts through the rule below.
    /// </para>
    /// <para>
    /// Two suppliers quote and one wins, because a demo with a single quote shows the tables without
    /// showing the decision — and the losing quote is the only evidence a buyer compared anything.
    /// </para>
    /// <para>
    /// The posting rule is the other half of <c>inventory.receipt.posted</c>: receiving debited
    /// inventory and credited goods-received-not-invoiced, and this clears that liability into trade
    /// creditors when the invoice is accepted (ADR-085). Nothing here names an account — the rule does
    /// (§7 rule 12).
    /// </para>
    /// </remarks>
    /// <param name="provider">The scoped provider.</param>
    /// <param name="context">The database context.</param>
    /// <param name="supplierId">The seeded supplier to buy from.</param>
    /// <param name="itemId">The item to buy.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedProcurementAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid supplierId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        Guid grni = await EnsureAccountAsync(
            provider, context, "2150", "Goods received not invoiced", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid creditors = await EnsureAccountAsync(
            provider, context, "2100", "Trade creditors", AccountType.Liability,
            ControlAccountType.AccountsPayable, cancellationToken).ConfigureAwait(false);
        Guid vatControl = await EnsureAccountAsync(
            provider, context, "2200", "VAT control", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, FinancialProcurementEventPublisher.SupplierInvoiceMatchedEventType,
            "Supplier invoice matched and released",
            [
                new PostingRuleLineInput(grni, NormalBalance.Debit, "Net", InheritDimensions: false, "Goods received not invoiced"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Input VAT"),
                new PostingRuleLineInput(creditors, NormalBalance.Credit, "Gross", InheritDimensions: false, "Trade creditors"),
            ],
            cancellationToken).ConfigureAwait(false);

        if (await context.PurchaseOrders.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IClock clock = provider.GetRequiredService<IClock>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        ITenantContext tenant = provider.GetRequiredService<ITenantContext>();
        DateTimeOffset now = clock.UtcNow;

        Domain.Identity.User? manager = await context.Users
            .FirstOrDefaultAsync(user => user.UserName == "manager", cancellationToken)
            .ConfigureAwait(false);

        Guid buyerId = manager?.Id ?? Guid.Empty;

        StockLocation warehouse = await context.StockLocations
            .FirstAsync(location => location.Code == "MAIN", cancellationToken)
            .ConfigureAwait(false);

        IDocumentNumberSequence numbers = provider.GetRequiredService<IDocumentNumberSequence>();

        // 1. Somebody says they need something, and the manager agrees.
        PurchaseRequisition requisition = PurchaseRequisition.Raise(
            tenant.TenantId,
            warehouse.StoreId,
            await numbers.NextAsync("REQ", cancellationToken).ConfigureAwait(false),
            buyerId,
            warehouse.Id,
            DateOnly.FromDateTime(now.UtcDateTime).AddDays(10),
            "Milk is down to two days' cover on the floor.",
            now);

        PurchaseRequisitionLine requisitionLine = requisition.AddLine(
            itemId, null, "Full cream milk 2L", new Quantity(120m, "EA"), new Money(19m, "ZAR"));

        requisition.Submit(now);
        requisition.Approve(buyerId, now);
        context.PurchaseRequisitions.Add(requisition);

        // 2. The buyer asks two suppliers. One is cheaper and slower; the buyer takes the cheaper.
        Rfq rfq = Rfq.Raise(
            tenant.TenantId,
            warehouse.StoreId,
            await numbers.NextAsync("RFQ", cancellationToken).ConfigureAwait(false),
            "Milk, 120 units, weekly",
            requisition.Id,
            now.AddDays(3),
            now);

        RfqLine rfqLine = rfq.AddLine(
            itemId, null, "Full cream milk 2L", new Quantity(120m, "EA"),
            "Minimum five days' shelf life on delivery.", requisitionLine.Id);

        rfq.Issue(now);

        Domain.Partners.Partner? alternate = await context.Partners
            .FirstOrDefaultAsync(partner => partner.Code == "CORPCLIENT", cancellationToken)
            .ConfigureAwait(false);

        RfqResponse winning = rfq.RecordResponse(supplierId, "ZAR", now, now.AddDays(30), 3, "Delivers Tuesdays.");
        rfq.AddResponseLine(winning.Id, rfqLine.Id, new Money(18.50m, "ZAR"), null);

        // A second, dearer quote — from a different partner id so the one-quote-per-supplier rule holds.
        // It is declined by the award, which is what makes the demo show a comparison rather than a
        // formality.
        if (alternate is not null)
        {
            RfqResponse losing = rfq.RecordResponse(
                alternate.Id, "ZAR", now, now.AddDays(14), 1, "Next-day delivery, higher price.");

            rfq.AddResponseLine(losing.Id, rfqLine.Id, new Money(20.25m, "ZAR"), null);
        }

        rfq.Award(winning.Id, buyerId, now);
        context.Rfqs.Add(rfq);

        requisition.RecordLineSourced(requisitionLine.Id, rfq.Id, now);

        // 3. The commitment, priced through the tenant's own tax rules (ADR-075).
        ITaxCalculator tax = provider.GetRequiredService<ITaxCalculator>();

        PurchaseOrder order = PurchaseOrder.Raise(
            tenant.TenantId,
            warehouse.StoreId,
            await numbers.NextAsync("PO", cancellationToken).ConfigureAwait(false),
            supplierId,
            "ZAR",
            warehouse.Id,
            DateOnly.FromDateTime(now.UtcDateTime).AddDays(7),
            winning.Id,
            "Deliver to the back room before 10:00.",
            now);

        Money unitCost = new(18.50m, "ZAR");
        Quantity ordered = new(120m, "EA");
        Money extended = ordered.Extend(unitCost).RoundToCurrencyScale();

        TaxCalculation calculated = await tax
            .CalculateAsync("STANDARD", extended, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken)
            .ConfigureAwait(false);

        PurchaseOrderLine orderLine = order.AddLine(
            itemId, null, "Full cream milk 2L", ordered, unitCost, "STANDARD",
            calculated.NetAmount, calculated.TaxAmount, requisitionLine.Id);

        order.Approve(buyerId, now);
        order.Issue(now);
        context.PurchaseOrders.Add(order);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        // 4. The goods arrive. Four bottles are short-dated and go back, which is what gives the
        // scorecard something other than a perfect score to report.
        GoodsReceipt receipt = GoodsReceipt.Open(
            order,
            await numbers.NextAsync("GRN", cancellationToken).ConfigureAwait(false),
            "FF-DN-4471",
            buyerId,
            now);

        receipt.AddLine(
            orderLine,
            new Quantity(116m, "EA"),
            new Quantity(4m, "EA"),
            GoodsRejectionReason.Expired,
            "Four bottles inside two days of their date.");

        context.GoodsReceipts.Add(receipt);

        await provider
            .GetRequiredService<IGoodsReceiptCompletionService>()
            .CompleteAsync(receipt, order, cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        // 5. The supplier bills for what actually arrived, and it agrees.
        Money invoicedNet = new Quantity(116m, "EA").Extend(unitCost).RoundToCurrencyScale();

        TaxCalculation invoicedTax = await tax
            .CalculateAsync("STANDARD", invoicedNet, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken)
            .ConfigureAwait(false);

        SupplierInvoiceMatch match = await provider
            .GetRequiredService<IThreeWayMatchEngine>()
            .MatchAsync(
                order,
                "FF-INV-88213",
                DateOnly.FromDateTime(now.UtcDateTime),
                invoicedTax.NetAmount,
                invoicedTax.TaxAmount,
                [
                    new Application.Procurement.SupplierInvoiceLine(
                        orderLine.Id, "Full cream milk 2L", new Quantity(116m, "EA"), unitCost),
                ],
                cancellationToken)
            .ConfigureAwait(false);

        context.SupplierInvoiceMatches.Add(match);

        match.Release(buyerId, now);

        foreach (SupplierInvoiceMatchLine matchLine in match.Lines
            .Where(candidate => candidate.PurchaseOrderLineId is not null))
        {
            order.RecordInvoiced(matchLine.PurchaseOrderLineId!.Value, matchLine.InvoicedQuantity);
        }

        Guid? journalId = await provider
            .GetRequiredService<IProcurementFinancialEventPublisher>()
            .PublishAsync(
                new SupplierInvoiceMatchedEvent(
                    match.TenantId,
                    match.StoreId,
                    match.Id,
                    order.Id,
                    order.OrderNumber,
                    match.PartnerId,
                    match.SupplierInvoiceNumber,
                    match.ClaimedNet,
                    match.ClaimedTax,
                    match.ClaimedGross,
                    now),
                cancellationToken)
            .ConfigureAwait(false);

        if (journalId is { } posted)
        {
            match.RecordJournal(posted);
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"Purchase order {order.OrderNumber}: {receipt.ReceivedValue} received on {receipt.ReceiptNumber}, "
            + $"supplier invoice {match.SupplierInvoiceNumber} {match.Status} and released.");
    }

    private static async Task<Guid> EnsureStockLocationAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        StockLocationType type,
        CancellationToken cancellationToken)
    {
        if (await context.StockLocations
            .FirstOrDefaultAsync(location => location.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateStockLocationCommand(code, name, type), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 07: a minimal chart of accounts, the current period, the `en-ZA` tax rule, and the
    /// posting rules that make an AR invoice and a till sale post to the ledger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chart is deliberately small — six accounts, enough to post a sale and a purchase and to
    /// give AR, AP and the bank their control accounts. A demo chart with a realistic hundred accounts
    /// would be a fiction nobody's real business matches, and every one of them would have to be
    /// maintained here.
    /// </para>
    /// <para>
    /// Exactly one tax rule, `STANDARD` at 15% inclusive, per CLAUDE.md §9 and the stage brief:
    /// seeding a second jurisdiction would suggest the list is meant to be exhaustive rather than a
    /// starting point a tenant edits.
    /// </para>
    /// <para>
    /// A `pos.sale.tendered` rule is seeded even though Stage 09 does not exist yet. It costs one row
    /// and it is the demonstration that rule 12 works: the POS module, when it arrives, raises that
    /// event and posts correctly without Finance or the seed changing.
    /// </para>
    /// </remarks>
    private static async Task SeedFinanceAsync(
        IServiceProvider provider, VumaRetailDbContext context, CancellationToken cancellationToken)
    {
        Guid debtors = await EnsureAccountAsync(
            provider, context, "1100", "Trade debtors", AccountType.Asset,
            ControlAccountType.AccountsReceivable, cancellationToken).ConfigureAwait(false);
        Guid bank = await EnsureAccountAsync(
            provider, context, "1200", "Bank — cheque account", AccountType.Asset,
            ControlAccountType.Bank, cancellationToken).ConfigureAwait(false);
        Guid creditors = await EnsureAccountAsync(
            provider, context, "2100", "Trade creditors", AccountType.Liability,
            ControlAccountType.AccountsPayable, cancellationToken).ConfigureAwait(false);
        // One combined VAT control account rather than separate input and output accounts. That is
        // what a small South African retailer's chart usually looks like — the VAT201 return is
        // prepared from the net position — and splitting it would mean seeding two accounts to
        // demonstrate one rule.
        Guid vatControl = await EnsureAccountAsync(
            provider, context, "2200", "VAT control", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid sales = await EnsureAccountAsync(
            provider, context, "4000", "Sales", AccountType.Revenue,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid purchases = await EnsureAccountAsync(
            provider, context, "5000", "Cost of sales", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsureCurrentPeriodAsync(provider, context, cancellationToken).ConfigureAwait(false);

        await EnsureTaxRuleAsync(
            provider, context, "STANDARD", "South African VAT, standard rate", 0.15m,
            TaxTreatment.Inclusive, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "ar.invoice.posted", "Customer invoice",
            [
                new PostingRuleLineInput(debtors, NormalBalance.Debit, "Gross", InheritDimensions: false, "Trade debtors"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "pos.sale.tendered", "Cash sale at the till",
            [
                new PostingRuleLineInput(bank, NormalBalance.Debit, "Gross", InheritDimensions: false, "Bank"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            cancellationToken).ConfigureAwait(false);

        // The mirror of the AR rule: cost of sales and input VAT are debited, trade creditors
        // credited. Debiting the same VAT control account is what makes it a net position.
        await EnsurePostingRuleAsync(
            provider, context, "ap.invoice.posted", "Supplier invoice",
            [
                new PostingRuleLineInput(purchases, NormalBalance.Debit, "Net", InheritDimensions: true, "Cost of sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Input VAT"),
                new PostingRuleLineInput(creditors, NormalBalance.Credit, "Gross", InheritDimensions: false, "Trade creditors"),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureAccountAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        AccountType type,
        ControlAccountType controlAccountType,
        CancellationToken cancellationToken)
    {
        if (await context.Accounts
            .FirstOrDefaultAsync(account => account.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateAccountCommand(code, name, type, "ZAR", controlAccountType), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Opens the period covering today, so a demo posting has somewhere to land.</summary>
    private static async Task EnsureCurrentPeriodAsync(
        IServiceProvider provider, VumaRetailDbContext context, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(
            provider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        DateOnly start = new(today.Year, today.Month, 1);
        DateOnly end = start.AddMonths(1).AddDays(-1);

        if (await context.AccountingPeriods
            .FirstOrDefaultAsync(period => period.PeriodStart == start, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new OpenAccountingPeriodCommand(start, end), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureTaxRuleAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        decimal rate,
        TaxTreatment treatment,
        CancellationToken cancellationToken)
    {
        if (await context.TaxRules
            .FirstOrDefaultAsync(rule => rule.Code == code, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        // Effective from the start of the current year rather than today, so a demo can post a
        // back-dated document without the rule having started after the document's own date.
        DateOnly effectiveFrom = new(provider.GetRequiredService<IClock>().UtcNow.Year, 1, 1);

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateTaxRuleCommand(code, name, rate, treatment, effectiveFrom), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePostingRuleAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string eventType,
        string description,
        IReadOnlyList<PostingRuleLineInput> lines,
        CancellationToken cancellationToken)
    {
        if (await context.PostingRules
            .FirstOrDefaultAsync(rule => rule.EventType == eventType, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new DefinePostingRuleCommand(eventType, lines, description), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Activates the demo installation against whichever control plane is wired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Development that is the in-process one, which signs real documents with the development key
    /// — so a demonstration is a fully licensed store with no vendor service anywhere, which is the
    /// difference between a demo that works on a laptop on a plane and one that does not.
    /// </para>
    /// <para>
    /// Against a real control plane there is nothing to register, and the seeder leaves activation to
    /// whoever holds the licence key. It says so rather than failing: a seeded demo against a
    /// production control plane is not a scenario anybody should be in by accident.
    /// </para>
    /// </remarks>
    private static async Task EnsureActivationAsync(
        IServiceProvider provider,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        IActivationRepository activations = provider.GetRequiredService<IActivationRepository>();

        if (await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (provider.GetService<InProcessControlPlane>() is not { } controlPlane)
        {
            Console.WriteLine(
                "Not activated: no in-process control plane is wired. Activate with a real licence "
                + "key through POST /api/v1/licence/activations.");

            return;
        }

        LicenceKey key = controlPlane.Register(new ControlPlaneTenant
        {
            TenantId = tenantId,
            StoreId = storeId,
            PlanCode = "demo",
            Entitlements = [.. provider.GetServices<IModuleManifest>().Select(manifest => manifest.LicenceFlag)],
            Limits = new LicenceLimits(2, 10, 25, 500_000, 50L * 1024 * 1024 * 1024, 1_000_000),
        });

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new ActivateInstallationCommand(key.Value, "Vuma Sandton", "owner@vuma.example"),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Activated the demo installation. Licence key: {key}");
    }

    private static IReadOnlyCollection<string> AllPermissions(IServiceProvider provider)
        => [.. provider.GetRequiredService<IPermissionCatalogue>().All.Select(descriptor => descriptor.Key.Value)];

    private static async Task<Tenant> EnsureTenantAsync(
        VumaRetailDbContext context,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // The tenant row is the one read that legitimately happens before a tenant is resolved: the
        // seeder is asking whether the demo tenant exists at all.
        using IDisposable bypass = tenantContext.BypassTenantFilter("seeding the demo tenant");

        Tenant? existing = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Id == DemoTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Demo Retail (Pty) Ltd", "Vuma Demo");
        SetId(tenant, DemoTenantId);
        tenant.Activate();

        context.Tenants.Add(tenant);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return tenant;
    }

    private static async Task<Store> EnsureStoreAsync(
        VumaRetailDbContext context,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        Store? existing = await context.Stores
            .FirstOrDefaultAsync(store => store.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Store store = Store.Create(tenantId, code, name);
        context.Stores.Add(store);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return store;
    }

    private static async Task<Guid> EnsureRoleAsync(
        IServiceProvider provider,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        IRoleRepository roles = provider.GetRequiredService<IRoleRepository>();

        if (await roles.FindByNameAsync(name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateRoleCommand(name, permissions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureUserAsync(
        IServiceProvider provider,
        string userName,
        string displayName,
        string password,
        Guid roleId,
        Guid? storeId,
        string? pin,
        CancellationToken cancellationToken)
    {
        IUserRepository users = provider.GetRequiredService<IUserRepository>();

        if (await users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        Guid userId = await dispatcher
            .SendAsync(new CreateUserCommand(userName, displayName, password), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher
            .SendAsync(new AssignRoleCommand(userId, roleId, storeId), cancellationToken)
            .ConfigureAwait(false);

        if (pin is not null)
        {
            await dispatcher
                .SendAsync(new SetUserPinCommand(userId, pin), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsureTerminalAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid storeId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        ITerminalRepository terminals = provider.GetRequiredService<ITerminalRepository>();

        if (await terminals.FindByCodeAsync(storeId, code, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        TerminalEnrolment enrolment = await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new EnrolTerminalCommand(storeId, code, name), cancellationToken)
            .ConfigureAwait(false);

        // Printed rather than stored: an activation code is shown once and never persisted in
        // plaintext, and a demo is no reason to make an exception to that.
        Console.WriteLine($"Terminal {code} enrolled. Activation code: {enrolment.EnrolmentCode}");
        Console.WriteLine($"  expires {enrolment.ExpiresAt:u}");

        _ = context;
    }

    private static async Task<Guid> EnsureUnitOfMeasureAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        UnitOfMeasureType type,
        CancellationToken cancellationToken)
    {
        if (await context.UnitsOfMeasure
            .FirstOrDefaultAsync(unit => unit.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateUnitOfMeasureCommand(code, name, type), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureDerivedUnitOfMeasureAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        Guid baseUnitOfMeasureId,
        decimal conversionFactorToBase,
        CancellationToken cancellationToken)
    {
        if (await context.UnitsOfMeasure
            .FirstOrDefaultAsync(unit => unit.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new CreateUnitOfMeasureCommand(
                    code,
                    name,
                    UnitOfMeasureType.Count,
                    baseUnitOfMeasureId,
                    conversionFactorToBase),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureItemAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        ItemType itemType,
        Guid unitOfMeasureId,
        string description,
        string taxClassCode,
        CancellationToken cancellationToken)
    {
        if (await context.Items
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new CreateItemCommand(code, name, itemType, unitOfMeasureId, description, taxClassCode),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureVariantAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid itemId,
        string sku,
        IReadOnlyList<VariantAttribute> attributes,
        CancellationToken cancellationToken)
    {
        if (await context.ItemVariants
            .FirstOrDefaultAsync(variant => variant.Sku == sku, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateItemVariantCommand(itemId, sku, attributes), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureBarcodeAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid? itemId,
        Guid? itemVariantId,
        string code,
        BarcodeSymbology symbology,
        CancellationToken cancellationToken)
    {
        if (await context.Barcodes
            .FirstOrDefaultAsync(barcode => barcode.Code == code, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new AddBarcodeCommand(itemId, itemVariantId, code, symbology), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsurePartnerAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        PartnerType type,
        string email,
        CancellationToken cancellationToken)
    {
        if (await context.Partners
            .FirstOrDefaultAsync(partner => partner.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreatePartnerCommand(code, name, type, Email: email), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pins the demo tenant's id so a re-run finds the same tenant.
    /// </summary>
    /// <remarks>
    /// <see cref="Tenant"/> generates a UUID v7 on creation, which is right for real tenants and
    /// wrong for a fixture that has to be findable. Reflection rather than a public setter, because a
    /// public "change this entity's identity" method would be usable from a module.
    /// </remarks>
    private static void SetId(Tenant tenant, Guid id)
    {
        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.Id))!
            .SetValue(tenant, id);

        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.TenantId))!
            .SetValue(tenant, id);
    }
}
