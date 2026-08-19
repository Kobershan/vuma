using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Inventory;

namespace VumaRetail.IntegrationTests.Warehouse;

/// <summary>
/// One tenant, one store, a Stage 08 location and an item, with both the <c>inventory</c> and
/// <c>warehouse</c> modules wired over a real database through the Stage 03 dispatcher. Same shape as
/// <c>InventoryHarness</c>, extended one level down.
/// </summary>
public sealed class WarehouseHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private WarehouseHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        Guid tenantId,
        Guid storeId,
        Guid itemId,
        Guid secondItemId,
        Guid locationId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        TenantId = tenantId;
        StoreId = storeId;
        ItemId = itemId;
        SecondItemId = secondItemId;
        LocationId = locationId;

        Locations = new StockLocationRepository(context);
        Ledger = new StockLedgerRepository(context);
        Balances = new StockBalanceRepository(context);
        ValuationEvents = new RecordingValuationEventPublisher();

        Zones = new ZoneRepository(context);
        Bins = new BinRepository(context);
        BinStocks = new BinStockRepository(context);
        BinStockMovements = new BinStockMovementRepository(context);
        PutawayTasks = new PutawayTaskRepository(context);
        PickWaves = new PickWaveRepository(context);
        PackTasks = new PackTaskRepository(context);
        Shipments = new ShipmentConfirmationRepository(context);
        CycleCounts = new CycleCountRepository(context);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IUnitOfMeasureRepository>(new UnitOfMeasureRepository(context));
        services.AddSingleton<IItemRepository>(new ItemRepository(context));
        services.AddSingleton<IItemVariantRepository>(new ItemVariantRepository(context));

        services.AddSingleton(Locations);
        services.AddSingleton(Ledger);
        services.AddSingleton(Balances);
        services.AddSingleton<IInventoryValuationEventPublisher>(ValuationEvents);
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster, StockLedgerPoster>();

        services.AddSingleton(Zones);
        services.AddSingleton(Bins);
        services.AddSingleton(BinStocks);
        services.AddSingleton(BinStockMovements);
        services.AddSingleton(PutawayTasks);
        services.AddSingleton(PickWaves);
        services.AddSingleton(PackTasks);
        services.AddSingleton(Shipments);
        services.AddSingleton(CycleCounts);
        services.AddSingleton<IBinStockMover, BinStockMover>();
        services.AddSingleton<IPickAllocationStrategy, LargestBinFirstAllocationStrategy>();

        services.AddVumaMessaging();

        _services = services.BuildServiceProvider();
        Dispatcher = _services.GetRequiredService<IDispatcher>();
    }

    /// <summary>The Stage 03 dispatcher, with validation, transaction and logging in the chain.</summary>
    public IDispatcher Dispatcher { get; }

    /// <summary>The database context under test.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The tenant context, so a test can move between tenants.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>A stocked item counted in <c>EA</c>.</summary>
    public Guid ItemId { get; }

    /// <summary>A second stocked item, for tests that need two balances.</summary>
    public Guid SecondItemId { get; }

    /// <summary>A seeded Stage 08 stock location every bin in this harness subdivides.</summary>
    public Guid LocationId { get; }

    /// <summary>Stage 08 location repository.</summary>
    public IStockLocationRepository Locations { get; }

    /// <summary>Stage 08 ledger repository.</summary>
    public IStockLedgerRepository Ledger { get; }

    /// <summary>Stage 08 balance repository.</summary>
    public IStockBalanceRepository Balances { get; }

    /// <summary>Every valuation event raised so far, in order.</summary>
    public RecordingValuationEventPublisher ValuationEvents { get; }

    /// <summary>Zone repository.</summary>
    public IZoneRepository Zones { get; }

    /// <summary>Bin repository.</summary>
    public IBinRepository Bins { get; }

    /// <summary>Bin-stock projection repository.</summary>
    public IBinStockRepository BinStocks { get; }

    /// <summary>Bin-stock movement repository.</summary>
    public IBinStockMovementRepository BinStockMovements { get; }

    /// <summary>Putaway task repository.</summary>
    public IPutawayTaskRepository PutawayTasks { get; }

    /// <summary>Pick wave/task repository.</summary>
    public IPickWaveRepository PickWaves { get; }

    /// <summary>Pack task repository.</summary>
    public IPackTaskRepository PackTasks { get; }

    /// <summary>Shipment confirmation repository.</summary>
    public IShipmentConfirmationRepository Shipments { get; }

    /// <summary>Cycle count repository.</summary>
    public ICycleCountRepository CycleCounts { get; }

    /// <summary>Creates a harness over a fresh database with a tenant, store, item and location in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<WarehouseHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        VumaRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            clock,
            new TestPrincipalAccessor("user:warehouseman"),
            tenant);

        Domain.Platform.Tenant seeded = Domain.Platform.Tenant.CreateWithSouthAfricanDefaults("Warehouse Harness (Pty) Ltd", "Harness");
        seeded.Activate();
        context.Tenants.Add(seeded);

        Store store = Store.Create(seeded.Id, "JHB02", "Harness Midrand");
        context.Stores.Add(store);

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item widget = Item.Create(seeded.Id, "WIDGET", "Blue widget", ItemType.Stock, each.Id);
        Item gadget = Item.Create(seeded.Id, "GADGET", "Red gadget", ItemType.Stock, each.Id);
        context.Items.Add(widget);
        context.Items.Add(gadget);

        StockLocation location = StockLocation.Create(seeded.Id, store.Id, "DC01", "Distribution centre", StockLocationType.Warehouse);
        context.StockLocations.Add(location);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new WarehouseHarness(context, tenant, clock, seeded.Id, store.Id, widget.Id, gadget.Id, location.Id);
    }

    /// <summary>Sends a command through the real pipeline.</summary>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) => Dispatcher.SendAsync(command);

    /// <summary>Sends a query through the real pipeline.</summary>
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query) => Dispatcher.QueryAsync(query);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Context.DisposeAsync();
    }
}
