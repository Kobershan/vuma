using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Sales;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sales;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;

namespace VumaRetail.IntegrationTests.Sales;

/// <summary>
/// One tenant, two companies, one store, one item and one price over a real database, with the
/// Stage 10c issuing saga wired to take real serialisable transactions per company.
/// </summary>
/// <remarks>
/// The companies and the registry share one test database (the template migrates both chains into
/// it). That is a test convenience, not the topology: the service under test never assumes it —
/// it opens each company's context through <c>ICompanyDbContextFactory</c> in a child scope and
/// publishes to an explicitly handed registry context, exactly as production does across
/// databases. Company separation inside the shared database is the <c>CompanyId</c> column, which
/// every invoice carries.
/// </remarks>
public sealed class SalesDocumentsHarness : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];
    private readonly ServiceProvider _scopes;

    private SalesDocumentsHarness(
        string connectionString,
        VumaRetailDbContext context,
        VumaRegistryDbContext registry,
        TestClock clock,
        TestTenantContext tenant,
        TestPrincipalAccessor principal,
        RecordingInvoiceEventPublisher invoiceEvents,
        Guid tenantId,
        Guid storeId,
        Guid companyAId,
        Guid companyBId,
        Guid operatorId,
        Guid itemId,
        Guid customerId)
    {
        ConnectionString = connectionString;
        Context = context;
        Registry = registry;
        Clock = clock;
        TenantContext = tenant;
        Principal = principal;
        InvoiceEvents = invoiceEvents;
        TenantId = tenantId;
        StoreId = storeId;
        CompanyAId = companyAId;
        CompanyBId = companyBId;
        OperatorId = operatorId;
        ItemId = itemId;
        CustomerId = customerId;

        Quotes = new QuoteRepository(context);
        Invoices = new InvoiceRepository(context);
        Analytics = new SalesAnalyticsRepository(context);

        ServiceCollection scopes = new();
        scopes.AddSingleton<IClock>(clock);
        scopes.AddSingleton<IPrincipalAccessor>(principal);
        scopes.AddSingleton<IReplicationScope, Infrastructure.Sync.ReplicationScope>();
        scopes.AddScoped<ITenantContext>(_ => TestTenantContext.Unfiltered());
        scopes.AddScoped<ICompanyContext, AmbientCompanyContext>();
        scopes.AddScoped<AuditStamper>();
        scopes.AddScoped<ICompanyDbContextFactory>(provider => new TestCompanyFactory(
            connectionString,
            clock,
            principal,
            provider.GetRequiredService<ITenantContext>(),
            Track));
        scopes.AddScoped<IInvoiceFinancialEventPublisher>(_ => invoiceEvents);
        _scopes = scopes.BuildServiceProvider();
        _owned.Add(_scopes);
    }

    /// <summary>The connection string, for opening further contexts.</summary>
    public string ConnectionString { get; }

    /// <summary>The company database context under test.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The registry context under test.</summary>
    public VumaRegistryDbContext Registry { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The principal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>Every invoice event raised so far, in order.</summary>
    public RecordingInvoiceEventPublisher InvoiceEvents { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The first supplying company.</summary>
    public Guid CompanyAId { get; }

    /// <summary>The second supplying company.</summary>
    public Guid CompanyBId { get; }

    /// <summary>The shared Operator ID.</summary>
    public Guid OperatorId { get; }

    /// <summary>A stocked item counted in <c>EA</c>, priced at R100 tax-exclusive.</summary>
    public Guid ItemId { get; }

    /// <summary>The customer the documents are written against.</summary>
    public Guid CustomerId { get; }

    /// <summary>Quote aggregates.</summary>
    public QuoteRepository Quotes { get; }

    /// <summary>Invoice aggregates.</summary>
    public InvoiceRepository Invoices { get; }

    /// <summary>Analytics read models.</summary>
    public SalesAnalyticsRepository Analytics { get; }

    /// <summary>Creates a harness over a fresh database.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<SalesDocumentsHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        var clock = new TestClock();
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:thandi");
        var invoiceEvents = new RecordingInvoiceEventPublisher();

        Guid tenantId;
        Guid storeId;
        Guid itemId;
        Guid companyAId;
        Guid companyBId;
        Guid operatorId = UuidV7.NewGuid();
        Guid customerId = UuidV7.NewGuid();

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(connectionString, clock, principal, tenant))
        {
            Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Documents Harness (Pty) Ltd", "Harness");
            seeded.Activate();
            seed.Tenants.Add(seeded);

            Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
            seed.Stores.Add(store);

            UnitOfMeasure each = UnitOfMeasure.CreateBase(seeded.Id, "EA", "Each", UnitOfMeasureType.Count);
            seed.UnitsOfMeasure.Add(each);

            UnitOfMeasure box = UnitOfMeasure.CreateDerived(seeded.Id, "CASE10", "Case of 10", each, 10m);
            seed.UnitsOfMeasure.Add(box);

            Item item = Item.Create(seeded.Id, "SUGAR-25", "Sugar 2.5kg", ItemType.Stock, each.Id);
            seed.Items.Add(item);

            seed.TaxRules.Add(TaxRule.Define(
                seeded.Id,
                "STANDARD",
                "South African VAT, standard rate",
                0.15m,
                TaxTreatment.Exclusive,
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

            PriceList retail = PriceList.Create(
                seeded.Id, null, "RETAIL", "Retail", "ZAR", PriceListKind.Retail,
                pricesIncludeTax: false, priority: 10,
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1), null);
            seed.PriceLists.Add(retail);
            seed.PriceListLines.Add(PriceListLine.Create(
                seeded.Id, null, retail.Id, item.Id, null, new Money(100m, "ZAR"), minimumQuantity: 1m));

            await seed.CommitAsync().ConfigureAwait(false);

            tenantId = seeded.Id;
            storeId = store.Id;
            itemId = item.Id;
        }

        await using (VumaRegistryDbContext registrySeed = TestDbContextFactory.ForRegistry(connectionString, tenant))
        {
            Company companyA = Company.Create(tenantId, "SHA", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "SH");
            companyA.AssignOperator(operatorId);
            companyA.SetConnectionSecretRef("test-secret-a");
            companyA.SetLifecycle(CompanyLifecycleState.Seeding);
            companyA.SetLifecycle(CompanyLifecycleState.Registered);
            companyA.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registrySeed.Companies.Add(companyA);

            Company companyB = Company.Create(tenantId, "TR", "Trade Rite", "Trade Rite", "ZAR", "en-ZA", "TR");
            companyB.AssignOperator(operatorId);
            companyB.SetConnectionSecretRef("test-secret-b");
            companyB.SetLifecycle(CompanyLifecycleState.Seeding);
            companyB.SetLifecycle(CompanyLifecycleState.Registered);
            companyB.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registrySeed.Companies.Add(companyB);

            await registrySeed.SaveChangesAsync().ConfigureAwait(false);

            companyAId = companyA.Id;
            companyBId = companyB.Id;
        }

        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);
        VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(connectionString, tenant);

        return new SalesDocumentsHarness(
            connectionString, context, registry, clock, tenant, principal, invoiceEvents,
            tenantId, storeId, companyAId, companyBId, operatorId, itemId, customerId);
    }

    /// <summary>Builds the real issuing saga over this harness's databases.</summary>
    /// <param name="links">The company-link guard. A stub that approves keeps the test about the split.</param>
    public IInvoiceIssuingService CreateIssuingService(ICompanyLinkGuard? links = null)
    {
        ICompanyLinkGuard guard = links ?? NSubstitute.Substitute.For<ICompanyLinkGuard>();

        var replication = new ReplicationRegistry(Context);
        var scope = new Infrastructure.Sync.ReplicationScope();
        var replicas = new ReplicaWriter(Context, scope);
        var node = new NodeIdentity(new NodeIdentityOptions { NodeId = "store:harness", Kind = NodeKind.Store });
        var hybrid = new HybridLogicalClock(Clock, node);

        return new InvoiceIssuingService(
            Registry,
            _scopes.GetRequiredService<IServiceScopeFactory>(),
            guard,
            replication,
            replicas,
            hybrid,
            node,
            Clock,
            NullLogger<InvoiceIssuingService>.Instance);
    }

    /// <summary>Opens a fresh company context over the test database.</summary>
    public VumaRetailDbContext OpenCompanyDb()
    {
        VumaRetailDbContext context = TestDbContextFactory.For(
            ConnectionString, Clock, Principal, TenantContext);
        Track(context);
        return context;
    }

    private void Track(VumaRetailDbContext context)
    {
        lock (_owned)
        {
            _owned.Add(context);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _scopes.DisposeAsync().ConfigureAwait(false);

        List<IAsyncDisposable> owned;
        lock (_owned)
        {
            owned = [.. _owned];
            _owned.Clear();
        }

        foreach (IAsyncDisposable disposable in owned)
        {
            if (!ReferenceEquals(disposable, _scopes))
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }

        await Context.DisposeAsync().ConfigureAwait(false);
        await Registry.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class TestCompanyFactory(
        string connectionString,
        TestClock clock,
        TestPrincipalAccessor principal,
        ITenantContext tenant,
        Action<VumaRetailDbContext> track) : ICompanyDbContextFactory
    {
        public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
            => CreateAsync(CompanyAccessMode.Write, cancellationToken);

        public Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default)
        {
            VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);
            track(context);
            return Task.FromResult(context);
        }
    }
}

/// <summary>Captures the financial events posted invoices raise, so a test can assert on them.</summary>
public sealed class RecordingInvoiceEventPublisher : IInvoiceFinancialEventPublisher
{
    private readonly List<InvoicePostedEvent> _events = [];

    /// <summary>Every event raised so far, in the order it was raised.</summary>
    public IReadOnlyList<InvoicePostedEvent> Events => _events;

    /// <inheritdoc />
    public Task PublishAsync(InvoicePostedEvent invoiceEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(invoiceEvent);
        return Task.CompletedTask;
    }
}
