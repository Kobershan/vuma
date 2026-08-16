using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Imports;
using VumaRetail.Application.Imports.Targets;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Partners;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Imports.Readers;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Pos;

namespace VumaRetail.IntegrationTests.Imports;

/// <summary>
/// A tenant that can already trade, plus the whole <c>imports</c> pipeline wired over a real database
/// through the Stage 03 dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// Every module an import writes into is wired <em>real</em> — partners, catalogue, the Stage 08 ledger
/// poster and Stage 10's price lists. That is the point of the stage: ADR-079 says a target handler
/// writes through the domain the command handlers use, and a harness that stubbed those repositories
/// would assert the stub rather than the promise. The stock target in particular has to prove it moved
/// a real balance through <see cref="IStockLedgerPoster"/>, which cannot be shown against a double.
/// </para>
/// <para>
/// The file store is given a per-harness temporary directory rather than the configured default, so a
/// test run never writes into the repository and two harnesses running side by side cannot collide on
/// a batch id.
/// </para>
/// </remarks>
public sealed class ImportsHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _fileDirectory;

    private ImportsHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        string fileDirectory,
        Guid tenantId,
        Guid storeId,
        Guid locationId,
        Guid itemId,
        Guid secondItemId,
        Guid priceListId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        _fileDirectory = fileDirectory;
        TenantId = tenantId;
        StoreId = storeId;
        LocationId = locationId;
        ItemId = itemId;
        SecondItemId = secondItemId;
        PriceListId = priceListId;

        Batches = new ImportBatchRepository(context, tenant);
        Templates = new ImportMappingTemplateRepository(context);
        Partners = new PartnerRepository(context);
        Items = new ItemRepository(context);
        Locations = new StockLocationRepository(context);
        Balances = new StockBalanceRepository(context);
        Ledger = new StockLedgerRepository(context);
        PriceLists = new PriceListRepository(context);

        Poster = new StockLedgerPoster(
            Balances,
            Ledger,
            new NullValuationEventPublisher(),
            clock);

        ImportOptions options = new()
        {
            FileDirectory = fileDirectory,
        };

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);
        services.AddSingleton(options);

        // Platform and master data, real.
        services.AddSingleton<IStoreRepository>(new StoreRepository(context));
        services.AddSingleton<ITenantRepository>(new TenantRepository(context));
        services.AddSingleton<IUnitOfMeasureRepository>(new UnitOfMeasureRepository(context));
        services.AddSingleton<IItemRepository>(Items);
        services.AddSingleton<IItemVariantRepository>(new ItemVariantRepository(context));
        services.AddSingleton<IBarcodeRepository>(new BarcodeRepository(context));
        services.AddSingleton<IPartnerRepository>(Partners);
        services.AddSingleton<IDocumentNumberSequence>(new DocumentNumberSequence(context, tenant));

        // Inventory, real — the stock target has to move a real balance (business rule 10).
        services.AddSingleton<IStockLocationRepository>(Locations);
        services.AddSingleton<IStockLedgerRepository>(Ledger);
        services.AddSingleton<IStockBalanceRepository>(Balances);
        services.AddSingleton<IInventoryValuationEventPublisher>(new NullValuationEventPublisher());
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster>(Poster);

        // Sales, real — the price-list target writes onto a Stage 10 list.
        services.AddSingleton<IPriceListRepository>(PriceLists);

        // The imports module itself.
        services.AddSingleton<IImportSourceReader, CsvImportSourceReader>();
        services.AddSingleton<IImportSourceReader, ExcelImportSourceReader>();
        services.AddSingleton<IOcrTextExtractor, UnavailableOcrTextExtractor>();
        services.AddSingleton<IImportSourceReader, PdfImportSourceReader>();
        services.AddSingleton<IImportSourceReaderFactory, ImportSourceReaderFactory>();

        services.AddSingleton<IImportTargetHandler, SupplierImportTargetHandler>();
        services.AddSingleton<IImportTargetHandler, CustomerImportTargetHandler>();
        services.AddSingleton<IImportTargetHandler, ItemImportTargetHandler>();
        services.AddSingleton<IImportTargetHandler, StockOnHandImportTargetHandler>();
        services.AddSingleton<IImportTargetHandler, PriceListLineImportTargetHandler>();
        services.AddSingleton<IImportTargetHandlerFactory, ImportTargetHandlerFactory>();

        services.AddSingleton<IImportValidator, ImportValidationService>();
        services.AddSingleton<IImportContextFactory, ImportBatchContextFactory>();
        services.AddSingleton<IImportUsageProbe>(new ImportUsageProbe(context));
        services.AddSingleton<IImportBatchRepository>(Batches);
        services.AddSingleton<IImportMappingTemplateRepository>(Templates);
        services.AddSingleton<IImportFileStore>(new FileSystemImportFileStore(options));

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

    /// <summary>The principal, so a test can act as somebody else.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The seeded stock location, code <c>MAIN</c>, with nothing on hand.</summary>
    public Guid LocationId { get; }

    /// <summary>A stocked item, code <c>MILK-2L</c>, counted in <c>EA</c>.</summary>
    public Guid ItemId { get; }

    /// <summary>A second stocked item, code <c>BREAD</c>.</summary>
    public Guid SecondItemId { get; }

    /// <summary>The seeded price list, code <c>RETAIL</c>.</summary>
    public Guid PriceListId { get; }

    /// <summary>Import batch repository.</summary>
    public IImportBatchRepository Batches { get; }

    /// <summary>Mapping template repository.</summary>
    public IImportMappingTemplateRepository Templates { get; }

    /// <summary>Partner repository — what a supplier or customer import writes into.</summary>
    public IPartnerRepository Partners { get; }

    /// <summary>Item repository — what an item import writes into.</summary>
    public IItemRepository Items { get; }

    /// <summary>Stock location repository.</summary>
    public IStockLocationRepository Locations { get; }

    /// <summary>
    /// The Stage 08 poster, exposed so a test can move stock the way the rest of the system does —
    /// which is how "something has happened to what this import created" is set up honestly.
    /// </summary>
    public IStockLedgerPoster Poster { get; }

    /// <summary>The on-hand projection a stock import moves.</summary>
    public IStockBalanceRepository Balances { get; }

    /// <summary>The append-only ledger a stock import posts to.</summary>
    public IStockLedgerRepository Ledger { get; }

    /// <summary>Price list repository — what a specials import writes onto.</summary>
    public IPriceListRepository PriceLists { get; }

    /// <summary>Creates a harness over a fresh database with everything an import needs to land in.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<ImportsHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();
        string fileDirectory = Path.Combine(Path.GetTempPath(), $"vuma-imports-{Guid.NewGuid():N}");

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Imports Harness (Pty) Ltd", "Harness");
        seeded.Activate();

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        User operatorUser = User.Create(seeded.Id, "thandi", "Thandi Nkosi");

        TestPrincipalAccessor principal = new($"user:{operatorUser.Id}");

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        context.Tenants.Add(seeded);
        context.Stores.Add(store);
        context.Users.Add(operatorUser);

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item milk = Item.Create(seeded.Id, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
        Item bread = Item.Create(seeded.Id, "BREAD", "White bread loaf", ItemType.Stock, each.Id);
        context.Items.Add(milk);
        context.Items.Add(bread);

        StockLocation location = StockLocation.Create(
            seeded.Id, store.Id, "MAIN", "Main storeroom", StockLocationType.Warehouse);

        context.StockLocations.Add(location);

        PriceList retail = PriceList.Create(
            seeded.Id,
            storeId: null,
            "RETAIL",
            "Shelf prices",
            "ZAR",
            PriceListKind.Retail,
            pricesIncludeTax: true,
            priority: 100,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1),
            effectiveTo: null);

        context.PriceLists.Add(retail);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new ImportsHarness(
            context,
            tenant,
            clock,
            principal,
            fileDirectory,
            seeded.Id,
            store.Id,
            location.Id,
            milk.Id,
            bread.Id,
            retail.Id);
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

        if (Directory.Exists(_fileDirectory))
        {
            Directory.Delete(_fileDirectory, recursive: true);
        }
    }
}
