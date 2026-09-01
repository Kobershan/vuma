using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Permissions;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Enforcement;

namespace VumaRetail.IntegrationTests.Pos;

/// <summary>
/// One tenant, one store, a terminal, an operator, a stock location with stock on it, and the
/// <c>pos</c> module wired over a real database through the Stage 03 dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// Wires the <em>real</em> inventory ledger poster and the <em>real</em> tax engine, because the two
/// things Stage 09 most needs to prove are that a sale actually relieves stock and that its tax comes
/// from a configured rule. Only the financial event publisher is a double
/// (<see cref="RecordingSaleEventPublisher"/>): whether a sale produces the right <em>journal</em> is
/// Stage 07's question, already answered by its own tests, and what this stage has to show is that
/// exactly one event is raised carrying the right amounts.
/// </para>
/// <para>
/// The principal is a real UUID, unlike <c>InventoryHarness</c>'s <c>user:storeman</c>. POS attributes
/// every sale, void, reprint and cash-up to the person who authenticated
/// (<c>PosActor</c>), so a principal that is not a user id would fail before any business rule ran.
/// </para>
/// </remarks>
public sealed class PosHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PosHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid operatorUserId,
        Guid locationId,
        Guid itemId,
        Guid secondItemId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        TerminalId = terminalId;
        OperatorUserId = operatorUserId;
        LocationId = locationId;
        ItemId = itemId;
        SecondItemId = secondItemId;

        Sales = new SaleRepository(context);
        TillSessions = new TillSessionRepository(context);
        ReceiptPrints = new ReceiptPrintRepository(context);
        SaleEvents = new RecordingSaleEventPublisher();

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);

        // Catalog and platform, for what is being sold and whose VAT number is on the slip.
        services.AddSingleton<IUnitOfMeasureRepository>(new UnitOfMeasureRepository(context));
        services.AddSingleton<IItemRepository>(new ItemRepository(context));
        services.AddSingleton<IItemVariantRepository>(new ItemVariantRepository(context));
        services.AddSingleton<IBarcodeRepository>(new BarcodeRepository(context));
        services.AddSingleton<IStoreRepository>(new StoreRepository(context));
        services.AddSingleton<ITenantRepository>(new TenantRepository(context));
        services.AddSingleton<IUserRepository>(new UserRepository(context));

        // §4.15: RecordReceiptPrintCommandHandler checks pos.receipt.reprint in-handler, so the
        // harness needs the same RBAC plumbing IdentityServiceCollectionExtensions wires in production.
        services.AddSingleton<IRoleRepository>(new RoleRepository(context));
        services.AddSingleton<IPermissionCatalogue>(
            new PermissionCatalogue([new PlatformPermissions(), new IdentityPermissions(), new PosPermissions()]));
        services.AddSingleton<IPermissionChecker, PermissionChecker>();

        // §4.10: the in-flight session/sale registry and its hard deadlines, and the read-only state
        // RecordReceiptPrintCommandHandler consults directly (sanctioned in LicensingRulesTests.cs).
        // Normal, not read-only, by default — the dedicated read-only tests live in
        // tests/VumaRetail.IntegrationTests/Licensing, against the real ReadOnlyGuardBehaviour.
        services.AddSingleton<IOpenSessionRegistry, OpenSessionRegistry>();
        services.AddSingleton<IOpenSessionWindows>(new LicensingOptions());
        services.AddSingleton<IEnforcementStatusReader>(new TestEntitlementService());

        // Inventory, real: a sale that does not relieve stock has not been proved to work.
        services.AddSingleton<IStockLocationRepository>(new StockLocationRepository(context));
        services.AddSingleton<IStockLedgerRepository>(new StockLedgerRepository(context));
        services.AddSingleton<IStockBalanceRepository>(new StockBalanceRepository(context));
        services.AddSingleton<IInventoryValuationEventPublisher>(new NullValuationEventPublisher());
        services.AddSingleton<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddSingleton<IStockLedgerPoster, StockLedgerPoster>();

        // Finance's tax engine, real: CLAUDE.md §9 says tax is a rules engine, and a test against a
        // stubbed rate would assert the stub.
        services.AddSingleton<ITaxRuleRepository>(new TaxRuleRepository(context));
        services.AddSingleton<ITaxCalculator, TaxEngine>();
        services.AddSingleton<IDocumentNumberSequence>(new DocumentNumberSequence(context, tenant));

        services.AddSingleton<ISaleRepository>(Sales);
        services.AddSingleton<ITillSessionRepository>(TillSessions);
        services.AddSingleton<IReceiptPrintRepository>(ReceiptPrints);
        services.AddSingleton<ISellableItemResolver, SellableItemResolver>();
        services.AddSingleton<ISaleFinancialEventPublisher>(SaleEvents);
        services.AddSingleton<ISaleCompletionService, SaleCompletionService>();

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

    /// <summary>The principal, so a test can act as a different operator or terminal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The seeded terminal — the one <see cref="Principal"/> reports.</summary>
    public Guid TerminalId { get; }

    /// <summary>The seeded operator — the one <see cref="Principal"/> reports.</summary>
    public Guid OperatorUserId { get; }

    /// <summary>The seeded stock location, with stock already received onto it.</summary>
    public Guid LocationId { get; }

    /// <summary>A stocked item counted in <c>EA</c>, with 100 on hand at <see cref="LocationId"/>.</summary>
    public Guid ItemId { get; }

    /// <summary>A second stocked item, with nothing on hand — the refused-issue path.</summary>
    public Guid SecondItemId { get; }

    /// <summary>Sale repository.</summary>
    public ISaleRepository Sales { get; }

    /// <summary>Till session repository.</summary>
    public ITillSessionRepository TillSessions { get; }

    /// <summary>Receipt print log.</summary>
    public IReceiptPrintRepository ReceiptPrints { get; }

    /// <summary>Every sale event raised so far, in order.</summary>
    public RecordingSaleEventPublisher SaleEvents { get; }

    /// <summary>Creates a harness over a fresh database with everything a till needs already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<PosHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("POS Harness (Pty) Ltd", "Harness");
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

        // §4.15: the harness operator needs pos.receipt.reprint for every existing reprint-path test to
        // keep behaving as it did before the in-handler permission check existed. The dedicated refusal
        // test builds its own principal without this role instead of weakening it here.
        Role cashierRole = Role.Create(seeded.Id, "Cashier");
        context.Roles.Add(cashierRole);
        context.RolePermissions.Add(
            RolePermission.Grant(seeded.Id, cashierRole.Id, PermissionKey.Parse(PosPermissions.ReceiptReprint)));
        context.UserRoleAssignments.Add(UserRoleAssignment.Assign(seeded.Id, operatorUser.Id, cashierRole.Id));

        UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
        context.UnitsOfMeasure.Add(each);

        Item milk = Item.Create(seeded.Id, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
        Item bread = Item.Create(seeded.Id, "BREAD", "White bread loaf", ItemType.Stock, each.Id);
        context.Items.Add(milk);
        context.Items.Add(bread);

        context.Barcodes.Add(Barcode.CreateForItem(milk, "6009876543210", BarcodeSymbology.Ean13));

        // 15% inclusive, the CLAUDE.md §9 default. Configured as data, because that is the only way
        // the tax engine will answer at all.
        context.TaxRules.Add(TaxRule.Define(
            seeded.Id,
            "STANDARD",
            "South African VAT, standard rate",
            0.15m,
            TaxTreatment.Inclusive,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

        StockLocation location = StockLocation.Create(seeded.Id, store.Id, "FLOOR", "Sales floor", StockLocationType.SalesFloor);
        context.StockLocations.Add(location);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        // 100 EA of milk at R20 cost, so a sale has something to relieve. Bread is deliberately left
        // with nothing on hand — that is the ADR-073 path.
        StockLedgerPoster poster = new(
            new StockBalanceRepository(context),
            new StockLedgerRepository(context),
            new NullValuationEventPublisher(),
            clock);

        await poster.ReceiveAsync(
            location, milk.Id, null, new Quantity(100m, "EA"), new Money(20m, "ZAR"), "Harness opening stock");

        await context.CommitAsync();

        return new PosHarness(
            context, tenant, clock, principal,
            seeded.Id, store.Id, terminal.Id, operatorUser.Id, location.Id, milk.Id, bread.Id);
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

/// <summary>Captures the financial events a completed sale raises, so a test can assert on them.</summary>
public sealed class RecordingSaleEventPublisher : ISaleFinancialEventPublisher
{
    private readonly List<SaleTenderedEvent> _events = [];

    /// <summary>Every event raised so far, in the order they were raised.</summary>
    public IReadOnlyList<SaleTenderedEvent> Events => _events;

    /// <inheritdoc />
    public Task PublishAsync(SaleTenderedEvent saleEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(saleEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Swallows inventory valuation events. Whether a stock movement produces the right journal is
/// Stage 08's and Stage 07's question; this harness is about the sale that triggered it.
/// </summary>
public sealed class NullValuationEventPublisher : IInventoryValuationEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
