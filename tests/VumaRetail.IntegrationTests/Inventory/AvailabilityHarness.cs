using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;

namespace VumaRetail.IntegrationTests.Inventory;

/// <summary>
/// One tenant, one company, one store, one location and one item over a real database, with the
/// Stage 08c reservation service wired to take real serialisable transactions.
/// </summary>
/// <remarks>
/// The company and the registry share one test database (the template migrates both chains into
/// it). That is a test convenience, not the topology: the service under test never assumes it —
/// it opens its company context through <c>ICompanyDbContextFactory</c> and publishes to an
/// explicitly handed registry context, exactly as production does across two databases. The
/// two-database proof is TASK-08C-002's saga test.
/// </remarks>
public sealed class AvailabilityHarness : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly List<IAsyncDisposable> _owned = [];

    private AvailabilityHarness(
        string connectionString,
        TestClock clock,
        TestTenantContext tenant,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid companyId,
        Guid storeId,
        Guid locationId,
        Guid itemId)
    {
        _connectionString = connectionString;
        Clock = clock;
        TenantContext = tenant;
        TenantId = tenantId;
        CompanyId = companyId;
        StoreId = storeId;
        LocationId = locationId;
        ItemId = itemId;

        Registry = TestDbContextFactory.ForRegistry(connectionString, tenant);
        _owned.Add(Registry);

        Relay = new GroupAvailabilityRelay(Options.Create(new GroupAvailabilityOptions()));
    }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The acting company.</summary>
    public Guid CompanyId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The seeded location.</summary>
    public Guid LocationId { get; }

    /// <summary>A stocked item counted in <c>EA</c>.</summary>
    public Guid ItemId { get; }

    /// <summary>The registry context (same test database, explicit handle).</summary>
    public VumaRegistryDbContext Registry { get; }

    /// <summary>The outbox-tail relay.</summary>
    public GroupAvailabilityRelay Relay { get; }

    /// <summary>Creates a harness over a fresh database.</summary>
    public static async Task<AvailabilityHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        var clock = new TestClock();
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:storeman");

        Guid tenantId;
        Guid storeId;
        Guid locationId;
        Guid itemId;
        Guid companyId;

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(connectionString, clock, principal, tenant))
        {
            Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Availability Harness (Pty) Ltd", "Harness");
            seeded.Activate();
            seed.Tenants.Add(seeded);

            Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
            seed.Stores.Add(store);

            UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
            seed.UnitsOfMeasure.Add(each);

            Item milk = Item.Create(seeded.Id, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
            seed.Items.Add(milk);

            await seed.CommitAsync().ConfigureAwait(false);

            tenantId = seeded.Id;
            storeId = store.Id;
            itemId = milk.Id;
        }

        await using (VumaRegistryDbContext registrySeed = TestDbContextFactory.ForRegistry(connectionString, tenant))
        {
            Company company = Company.Create(tenantId, "SH", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "SH");
            registrySeed.Companies.Add(company);
            await registrySeed.SaveChangesAsync().ConfigureAwait(false);
            companyId = company.Id;
        }

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(connectionString, clock, principal, tenant))
        {
            StockLocation location = StockLocation.Create(tenantId, storeId, "FLOOR", "Sales floor", StockLocationType.SalesFloor);
            location.AssignCompany(companyId);
            seed.StockLocations.Add(location);
            await seed.CommitAsync().ConfigureAwait(false);
            locationId = location.Id;
        }

        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        return new AvailabilityHarness(
            connectionString, clock, tenant, principal,
            tenantId, companyId, storeId, locationId, itemId);
    }

    /// <summary>Opens a fresh company context over the test database.</summary>
    public VumaRetailDbContext OpenCompanyDb()
    {
        VumaRetailDbContext context = TestDbContextFactory.For(
            _connectionString, Clock, new TestPrincipalAccessor("user:storeman"), TenantContext);
        _owned.Add(context);
        return context;
    }

    /// <summary>
    /// Creates a reservation service with its own company context — one per scope, like production.
    /// Two services means two connections and two transactions: the concurrency tests' whole point.
    /// </summary>
    /// <param name="registry">The registry to publish to, or <c>null</c> for no direct publish (the relay-only path).</param>
    public IReservationService CreateService(VumaRegistryDbContext? registry = null, bool publish = true)
    {
        VumaRetailDbContext companyDb = OpenCompanyDb();
        var factory = new SingleContextFactory(companyDb);
        var scope = new VumaRetail.Infrastructure.Sync.ReplicationScope();
        var stamper = new AuditStamper(Clock, new TestPrincipalAccessor("user:storeman"), scope);
        var node = new NodeIdentity(new NodeIdentityOptions { NodeId = "store:harness", Kind = NodeKind.Store });
        var hybrid = new HybridLogicalClock(Clock, node);
        IGroupAvailabilityPublisher? publisher = publish
            ? new RegistryAvailabilityPublisher(registry ?? Registry)
            : null;

        var service = new ReservationService(
            factory,
            Clock,
            stamper,
            new ReplicationRegistry(companyDb),
            new ReplicaWriter(companyDb, scope),
            hybrid,
            node,
            NullLogger<ReservationService>.Instance,
            publisher);
        _owned.Add(service);
        return service;
    }

    /// <summary>Receives stock through the real poster, so on-hand figures are honest.</summary>
    public async Task ReceiveAsync(decimal quantity, decimal unitCost = 10m)
    {
        await using VumaRetailDbContext context = TestDbContextFactory.For(
            _connectionString, Clock, new TestPrincipalAccessor("user:storeman"), TenantContext);

        var locations = new StockLocationRepository(context);
        var skus = new StockKeepingUnitResolver(
            new ItemRepository(context),
            new ItemVariantRepository(context),
            new UnitOfMeasureRepository(context));
        var events = new RecordingValuationEventPublisher();
        var poster = new StockLedgerPoster(
            new StockBalanceRepository(context),
            new StockLedgerRepository(context),
            events,
            Clock);

        StockLocation location = await locations.FindAsync(LocationId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The harness location is missing.");

        string uom = await skus.ResolveUnitOfMeasureCodeAsync(ItemId, null).ConfigureAwait(false);
        await poster.ReceiveAsync(
            location, ItemId, null,
            new Domain.Primitives.Quantity(quantity, uom),
            new Domain.Primitives.Money(unitCost, "ZAR"),
            note: null).ConfigureAwait(false);

        await context.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>Reads the current available figure straight from the tables.</summary>
    public async Task<decimal> ReadAvailableAsync()
    {
        await using VumaRetailDbContext context = TestDbContextFactory.For(
            _connectionString, Clock, new TestPrincipalAccessor("user:storeman"), TenantContext);

        var balances = new StockBalanceRepository(context);
        var positions = new AvailableBalanceRepository(context);

        Domain.Inventory.StockBalance? balance = await balances.FindAsync(LocationId, ItemId, null).ConfigureAwait(false);
        AvailableBalance? position = await positions.FindAsync(LocationId, ItemId, null).ConfigureAwait(false);

        return (balance?.QuantityOnHand.Value ?? 0m) - (position?.Reserved.Value ?? 0m) - (position?.InStaging.Value ?? 0m);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable owned in _owned)
        {
            await owned.DisposeAsync().ConfigureAwait(false);
        }

        _owned.Clear();
    }

    private sealed class SingleContextFactory(VumaRetailDbContext context) : ICompanyDbContextFactory
    {
        public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(context);

        public Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default)
            => Task.FromResult(context);
    }
}
