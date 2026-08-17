using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Partners;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Procurement;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Pos;

namespace VumaRetail.IntegrationTests.Procurement;

/// <summary>
/// A tenant that can already trade, plus the <c>procurement</c> module wired over a real database
/// through the Stage 03 dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// Wires the <em>real</em> inventory ledger poster and the <em>real</em> tax engine, because the two
/// things this stage most needs to prove cross module boundaries: that completing a goods receipt
/// actually moves stock at the order's cost, and that an order line's tax comes from the tenant's rules
/// rather than from a constant somebody typed. A stubbed rate asserts the stub.
/// </para>
/// <para>
/// Only the financial event publisher is a double. Whether a released match produces the right
/// <em>journal</em> is Stage 07's question, answered by its own tests; what this stage has to show is
/// that exactly one event is raised carrying the right amounts and naming no account.
/// </para>
/// </remarks>
public sealed class ProcurementHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private ProcurementHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        ProcurementOptions options,
        Guid tenantId,
        Guid storeId,
        Guid buyerUserId,
        Guid locationId,
        Guid supplierId,
        Guid customerOnlyPartnerId,
        Guid itemId,
        Guid secondItemId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        Options = options;
        TenantId = tenantId;
        StoreId = storeId;
        BuyerUserId = buyerUserId;
        LocationId = locationId;
        SupplierId = supplierId;
        CustomerOnlyPartnerId = customerOnlyPartnerId;
        ItemId = itemId;
        SecondItemId = secondItemId;

        Requisitions = new PurchaseRequisitionRepository(context);
        Rfqs = new RfqRepository(context);
        Orders = new PurchaseOrderRepository(context);
        Receipts = new GoodsReceiptRepository(context);
        Matches = new SupplierInvoiceMatchRepository(context);
        Scorecards = new SupplierScorecardRepository(context);
        MatchEvents = new RecordingProcurementEventPublisher();
        Balances = new StockBalanceRepository(context);
        Ledger = new StockLedgerRepository(context);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);
        services.AddSingleton(options);

        services.AddSingleton<IUnitOfMeasureRepository>(new UnitOfMeasureRepository(context));
        services.AddSingleton<IItemRepository>(new ItemRepository(context));
        services.AddSingleton<IItemVariantRepository>(new ItemVariantRepository(context));
        services.AddSingleton<IBarcodeRepository>(new BarcodeRepository(context));
        services.AddSingleton<IStoreRepository>(new StoreRepository(context));
        services.AddSingleton<ITenantRepository>(new TenantRepository(context));
        services.AddSingleton<IUserRepository>(new UserRepository(context));
        services.AddSingleton<IPartnerRepository>(new PartnerRepository(context));

        services.AddSingleton<IStockLocationRepository>(new StockLocationRepository(context));
        services.AddSingleton<IStockLedgerRepository>(Ledger);
        services.AddSingleton<IStockBalanceRepository>(Balances);
        services.AddSingleton<IInventoryValuationEventPublisher>(new NullValuationEventPublisher());
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster, StockLedgerPoster>();

        services.AddSingleton<ITaxRuleRepository>(new TaxRuleRepository(context));
        services.AddSingleton<ITaxCalculator, TaxEngine>();
        services.AddSingleton<IDocumentNumberSequence>(new DocumentNumberSequence(context, tenant));

        services.AddSingleton<IPurchaseRequisitionRepository>(Requisitions);
        services.AddSingleton<IRfqRepository>(Rfqs);
        services.AddSingleton<IPurchaseOrderRepository>(Orders);
        services.AddSingleton<IGoodsReceiptRepository>(Receipts);
        services.AddSingleton<ISupplierInvoiceMatchRepository>(Matches);
        services.AddSingleton<ISupplierScorecardRepository>(Scorecards);
        services.AddSingleton<IProcurementFinancialEventPublisher>(MatchEvents);
        services.AddSingleton<IThreeWayMatchEngine, ThreeWayMatchEngine>();
        services.AddSingleton<IGoodsReceiptCompletionService, GoodsReceiptCompletionService>();
        services.AddSingleton<ISupplierScorecardCalculator, SupplierScorecardCalculator>();

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

    /// <summary>The clock the test moves by hand — which is what makes a closed scorecard period testable.</summary>
    public TestClock Clock { get; }

    /// <summary>The principal, so a test can act as a different user.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The tolerances, mutable so a test can set its own without rebuilding the container.</summary>
    public ProcurementOptions Options { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The seeded buyer — the one <see cref="Principal"/> reports.</summary>
    public Guid BuyerUserId { get; }

    /// <summary>The seeded stock location, empty until something is bought into it.</summary>
    public Guid LocationId { get; }

    /// <summary>A partner marked as a supplier.</summary>
    public Guid SupplierId { get; }

    /// <summary>A partner marked customer-only — the reference-validation path.</summary>
    public Guid CustomerOnlyPartnerId { get; }

    /// <summary>An item counted in <c>EA</c>.</summary>
    public Guid ItemId { get; }

    /// <summary>A second item, for multi-line documents.</summary>
    public Guid SecondItemId { get; }

    /// <summary>Requisition repository.</summary>
    public IPurchaseRequisitionRepository Requisitions { get; }

    /// <summary>RFQ repository.</summary>
    public IRfqRepository Rfqs { get; }

    /// <summary>Purchase order repository.</summary>
    public IPurchaseOrderRepository Orders { get; }

    /// <summary>Goods receipt repository.</summary>
    public IGoodsReceiptRepository Receipts { get; }

    /// <summary>Three-way match repository.</summary>
    public ISupplierInvoiceMatchRepository Matches { get; }

    /// <summary>Supplier scorecard repository.</summary>
    public ISupplierScorecardRepository Scorecards { get; }

    /// <summary>Stock balance repository — the proof that a receipt really moved stock.</summary>
    public IStockBalanceRepository Balances { get; }

    /// <summary>Stock ledger repository.</summary>
    public IStockLedgerRepository Ledger { get; }

    /// <summary>Every matched-invoice event raised so far, in order.</summary>
    public RecordingProcurementEventPublisher MatchEvents { get; }

    /// <summary>Creates a harness over a fresh database with everything a buyer needs already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<ProcurementHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Procurement Harness (Pty) Ltd", "Harness");
        seeded.Activate();

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        User buyer = User.Create(seeded.Id, "sipho", "Sipho Dlamini");

        TestPrincipalAccessor principal = new($"user:{buyer.Id}", terminalId: null);

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        context.Tenants.Add(seeded);
        context.Stores.Add(store);
        context.Users.Add(buyer);

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item beans = Item.Create(seeded.Id, "BEANS-1KG", "Coffee beans 1kg", ItemType.Stock, each.Id);
        Item filters = Item.Create(seeded.Id, "FILTERS", "Filters, box of 100", ItemType.Stock, each.Id);
        context.Items.Add(beans);
        context.Items.Add(filters);

        Partner supplier = Partner.Create(
            seeded.Id, "SUP-001", "Highveld Coffee Roasters", PartnerType.Supplier);

        // A partner who is only a customer. Nothing in the database stops an order naming them —
        // CONVENTIONS.md §2 forbids the cross-schema foreign key — so the application layer is what has
        // to refuse it, and a harness with no such partner cannot prove that it does.
        Partner customerOnly = Partner.Create(
            seeded.Id, "CUS-001", "Sandton Office Park", PartnerType.Customer);

        context.Partners.Add(supplier);
        context.Partners.Add(customerOnly);

        context.TaxRules.Add(TaxRule.Define(
            seeded.Id,
            "STANDARD",
            "South African VAT, standard rate",
            0.15m,
            TaxTreatment.Exclusive,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

        StockLocation location = StockLocation.Create(
            seeded.Id, store.Id, "STORE", "Back store room", StockLocationType.Warehouse);

        context.StockLocations.Add(location);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new ProcurementHarness(
            context,
            tenant,
            clock,
            principal,
            new ProcurementOptions(),
            seeded.Id,
            store.Id,
            buyer.Id,
            location.Id,
            supplier.Id,
            customerOnly.Id,
            beans.Id,
            filters.Id);
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

/// <summary>Captures the financial events a released match raises, so a test can assert on them.</summary>
public sealed class RecordingProcurementEventPublisher : IProcurementFinancialEventPublisher
{
    private readonly List<SupplierInvoiceMatchedEvent> _events = [];

    /// <summary>Every event raised so far, in the order they were raised.</summary>
    public IReadOnlyList<SupplierInvoiceMatchedEvent> Events => _events;

    /// <summary>The journal id to hand back, so a test can prove the match records it.</summary>
    public Guid? JournalId { get; set; } = UuidV7.NewGuid();

    /// <inheritdoc />
    public Task<Guid?> PublishAsync(
        SupplierInvoiceMatchedEvent matchedEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(matchedEvent);

        return Task.FromResult(JournalId);
    }
}
