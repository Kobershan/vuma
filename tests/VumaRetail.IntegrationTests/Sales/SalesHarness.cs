using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Sales;
using VumaRetail.Application.Sales.Pricing;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Pos;

namespace VumaRetail.IntegrationTests.Sales;

/// <summary>
/// A tenant that can already trade, plus the <c>sales</c> module wired over a real database through
/// the Stage 03 dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// Wires the <em>real</em> POS module and the <em>real</em> inventory ledger poster, because the two
/// things this stage most needs to prove cross module boundaries: that a return reads a completed sale
/// it did not write, and that completing one actually puts stock back on the shelf at what it left at.
/// The tax engine is real for the same reason Stage 09 kept it real — a price resolved against a
/// stubbed rate asserts the stub.
/// </para>
/// <para>
/// Only the two financial event publishers are doubles. Whether a return produces the right
/// <em>journal</em> is Stage 07's question, answered by its own tests; what this stage has to show is
/// that exactly one event is raised carrying the right amounts and naming no account.
/// </para>
/// </remarks>
public sealed class SalesHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private SalesHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid storeId,
        Guid operatorUserId,
        Guid locationId,
        Guid itemId,
        Guid unstockedItemId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        OperatorUserId = operatorUserId;
        LocationId = locationId;
        ItemId = itemId;
        UnstockedItemId = unstockedItemId;

        PriceLists = new PriceListRepository(context);
        Promotions = new PromotionRepository(context);
        Returns = new SalesReturnRepository(context);
        PriceOverrides = new PriceOverrideLogRepository(context);
        ReturnEvents = new RecordingSalesReturnEventPublisher();

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);

        services.AddSingleton<IUnitOfMeasureRepository>(new UnitOfMeasureRepository(context));
        services.AddSingleton<IItemRepository>(new ItemRepository(context));
        services.AddSingleton<IItemVariantRepository>(new ItemVariantRepository(context));
        services.AddSingleton<IBarcodeRepository>(new BarcodeRepository(context));
        services.AddSingleton<IStoreRepository>(new StoreRepository(context));
        services.AddSingleton<ITenantRepository>(new TenantRepository(context));
        services.AddSingleton<IUserRepository>(new UserRepository(context));

        services.AddSingleton<IStockLocationRepository>(new StockLocationRepository(context));
        services.AddSingleton<IStockLedgerRepository>(new StockLedgerRepository(context));
        services.AddSingleton<IStockBalanceRepository>(new StockBalanceRepository(context));
        services.AddSingleton<IInventoryValuationEventPublisher>(new NullValuationEventPublisher());
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster, StockLedgerPoster>();

        services.AddSingleton<ITaxRuleRepository>(new TaxRuleRepository(context));
        services.AddSingleton<ITaxCalculator, TaxEngine>();
        services.AddSingleton<IDocumentNumberSequence>(new DocumentNumberSequence(context, tenant));

        // POS, real: a return is raised against a sale this module did not write, and reading that sale
        // through the same repository the till uses is the whole of the integration.
        services.AddSingleton<ISaleRepository>(new SaleRepository(context));
        services.AddSingleton<ITillSessionRepository>(new TillSessionRepository(context));
        services.AddSingleton<IReceiptPrintRepository>(new ReceiptPrintRepository(context));
        services.AddSingleton<ISellableItemResolver, SellableItemResolver>();
        services.AddSingleton<ISaleFinancialEventPublisher>(new RecordingSaleEventPublisher());
        services.AddSingleton<ISaleCompletionService, SaleCompletionService>();

        services.AddSingleton<IPriceListRepository>(PriceLists);
        services.AddSingleton<IPromotionRepository>(Promotions);
        services.AddSingleton<ISalesReturnRepository>(Returns);
        services.AddSingleton<IPriceOverrideLogRepository>(PriceOverrides);
        services.AddSingleton<IPriceResolver, PriceResolver>();
        services.AddSingleton<ISalesReturnFinancialEventPublisher>(ReturnEvents);
        services.AddSingleton<ISalesReturnCompletionService, SalesReturnCompletionService>();

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

    /// <summary>The clock the test moves by hand — which is what makes a happy-hour promotion testable.</summary>
    public TestClock Clock { get; }

    /// <summary>The principal, so a test can act as a different operator.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The seeded operator — the one <see cref="Principal"/> reports.</summary>
    public Guid OperatorUserId { get; }

    /// <summary>The seeded stock location, with stock already received onto it.</summary>
    public Guid LocationId { get; }

    /// <summary>A stocked item counted in <c>EA</c>, with 100 on hand at <see cref="LocationId"/>.</summary>
    public Guid ItemId { get; }

    /// <summary>A second item with nothing on hand — the ADR-073 path, in both directions.</summary>
    public Guid UnstockedItemId { get; }

    /// <summary>The seeded terminal.</summary>
    public Guid TerminalId { get; private init; }

    /// <summary>Price list repository.</summary>
    public IPriceListRepository PriceLists { get; }

    /// <summary>Promotion repository.</summary>
    public IPromotionRepository Promotions { get; }

    /// <summary>Sales return repository.</summary>
    public ISalesReturnRepository Returns { get; }

    /// <summary>The append-only price override log.</summary>
    public IPriceOverrideLogRepository PriceOverrides { get; }

    /// <summary>Every return event raised so far, in order.</summary>
    public RecordingSalesReturnEventPublisher ReturnEvents { get; }

    /// <summary>Creates a harness over a fresh database with everything a shop needs already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<SalesHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Sales Harness (Pty) Ltd", "Harness");
        seeded.Activate();

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        User operatorUser = User.Create(seeded.Id, "thandi", "Thandi Nkosi");
        Terminal terminal = Terminal.Enrol(
            seeded.Id, store.Id, "TILL-01", "Front till", "harness-enrolment-hash", clock.UtcNow.AddDays(30));

        TestPrincipalAccessor principal = new($"user:{operatorUser.Id}", terminal.Id);

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        context.Tenants.Add(seeded);
        context.Stores.Add(store);
        context.Users.Add(operatorUser);
        context.Terminals.Add(terminal);

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item milk = Item.Create(seeded.Id, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
        Item bread = Item.Create(seeded.Id, "BREAD", "White bread loaf", ItemType.Stock, each.Id);
        context.Items.Add(milk);
        context.Items.Add(bread);

        context.TaxRules.Add(TaxRule.Define(
            seeded.Id,
            "STANDARD",
            "South African VAT, standard rate",
            0.15m,
            TaxTreatment.Inclusive,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

        StockLocation location = StockLocation.Create(
            seeded.Id, store.Id, "FLOOR", "Sales floor", StockLocationType.SalesFloor);

        context.StockLocations.Add(location);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        StockLedgerPoster poster = new(
            new StockBalanceRepository(context),
            new StockLedgerRepository(context),
            new NullValuationEventPublisher(),
            clock);

        await poster.ReceiveAsync(
            location, milk.Id, null, new Quantity(100m, "EA"), new Money(20m, "ZAR"), "Harness opening stock");

        await context.CommitAsync();

        return new SalesHarness(
            context, tenant, clock, principal,
            seeded.Id, store.Id, operatorUser.Id, location.Id, milk.Id, bread.Id)
        {
            TerminalId = terminal.Id,
        };
    }

    /// <summary>Sends a command through the real pipeline.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command.</param>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) => Dispatcher.SendAsync(command);

    /// <summary>Sends a command with no result through the real pipeline.</summary>
    /// <param name="command">The command.</param>
    public Task SendAsync(ICommand command) => Dispatcher.SendAsync(command);

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

/// <summary>Captures the financial events a completed return raises, so a test can assert on them.</summary>
public sealed class RecordingSalesReturnEventPublisher : ISalesReturnFinancialEventPublisher
{
    private readonly List<SalesReturnCompletedEvent> _events = [];

    /// <summary>Every event raised so far, in the order they were raised.</summary>
    public IReadOnlyList<SalesReturnCompletedEvent> Events => _events;

    /// <inheritdoc />
    public Task PublishAsync(
        SalesReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(returnEvent);
        return Task.CompletedTask;
    }
}
