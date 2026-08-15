using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Inventory;

/// <summary>
/// One tenant, one store, a unit of measure and an item, with the <c>inventory</c> module wired over a
/// real database through the Stage 03 dispatcher. Same shape as <c>CatalogHarness</c>.
/// </summary>
/// <remarks>
/// Wires <see cref="RecordingValuationEventPublisher"/> rather than either real publisher. Whether a
/// movement produces the right <em>journal</em> is Stage 07's question, already answered by its own
/// tests; what Stage 08 has to prove is that every movement raises exactly one valuation event
/// carrying the right value — which is what this records and the tests assert on.
/// </remarks>
public sealed class InventoryHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private InventoryHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        Guid tenantId,
        Guid storeId,
        Guid itemId,
        Guid secondItemId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        TenantId = tenantId;
        StoreId = storeId;
        ItemId = itemId;
        SecondItemId = secondItemId;

        Locations = new StockLocationRepository(context);
        Ledger = new StockLedgerRepository(context);
        Balances = new StockBalanceRepository(context);
        Transfers = new StockTransferRepository(context);
        Stocktakes = new StocktakeRepository(context);
        ValuationEvents = new RecordingValuationEventPublisher();

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
        services.AddSingleton(Transfers);
        services.AddSingleton(Stocktakes);
        services.AddSingleton<IInventoryValuationEventPublisher>(ValuationEvents);
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster, StockLedgerPoster>();
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

    /// <summary>A second stocked item, for tests that need two balances at one location.</summary>
    public Guid SecondItemId { get; }

    /// <summary>Location repository.</summary>
    public IStockLocationRepository Locations { get; }

    /// <summary>Ledger repository.</summary>
    public IStockLedgerRepository Ledger { get; }

    /// <summary>Balance repository.</summary>
    public IStockBalanceRepository Balances { get; }

    /// <summary>Transfer repository.</summary>
    public IStockTransferRepository Transfers { get; }

    /// <summary>Stocktake repository.</summary>
    public IStocktakeRepository Stocktakes { get; }

    /// <summary>Every valuation event raised so far, in order.</summary>
    public RecordingValuationEventPublisher ValuationEvents { get; }

    /// <summary>Creates a harness over a fresh database with a tenant, store, unit and items in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<InventoryHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        VumaRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            clock,
            new TestPrincipalAccessor("user:storeman"),
            tenant);

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Inventory Harness (Pty) Ltd", "Harness");
        seeded.Activate();
        context.Tenants.Add(seeded);

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        context.Stores.Add(store);

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item milk = Item.Create(seeded.Id, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
        Item bread = Item.Create(seeded.Id, "BREAD", "White bread loaf", ItemType.Stock, each.Id);
        context.Items.Add(milk);
        context.Items.Add(bread);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new InventoryHarness(context, tenant, clock, seeded.Id, store.Id, milk.Id, bread.Id);
    }

    /// <summary>Sends a command through the real pipeline.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command.</param>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) => Dispatcher.SendAsync(command);

    /// <summary>Sends a query through the real pipeline.</summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="query">The query.</param>
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query) => Dispatcher.QueryAsync(query);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Context.DisposeAsync();
    }
}

/// <summary>Captures the valuation events a stock movement raises, so a test can assert on them.</summary>
public sealed class RecordingValuationEventPublisher : IInventoryValuationEventPublisher
{
    private readonly List<InventoryValuationEvent> _events = [];

    /// <summary>Every event raised so far, in the order they were raised.</summary>
    public IReadOnlyList<InventoryValuationEvent> Events => _events;

    /// <inheritdoc />
    public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(valuationEvent);
        return Task.CompletedTask;
    }
}
