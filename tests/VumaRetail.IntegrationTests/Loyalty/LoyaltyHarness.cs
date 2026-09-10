using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Loyalty;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Loyalty;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Loyalty;

/// <summary>
/// A tenant with loyalty enabled, wired over a real database through the Stage 03 dispatcher
/// with the real in-memory Orbit ledger standing in for Proxima (Stage 20).
/// </summary>
public sealed class LoyaltyHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private LoyaltyHarness(
        string connectionString,
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid storeId,
        Guid companyId,
        Guid customerId,
        InMemoryOrbitClient orbit)
    {
        ConnectionString = connectionString;
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        CompanyId = companyId;
        CustomerId = customerId;
        Orbit = orbit;

        Members = new LoyaltyMemberRepository(context);
        Transactions = new LoyaltyTransactionRepository(context);
        Tiers = new LoyaltyTierRepository(context);
        Rewards = new LoyaltyRewardRepository(context);
        Settings = new LoyaltySettingsRepository(context);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);

        services.AddSingleton<ILoyaltyMemberRepository>(Members);
        services.AddSingleton<ILoyaltyTransactionRepository>(Transactions);
        services.AddSingleton<ILoyaltyTierRepository>(Tiers);
        services.AddSingleton<ILoyaltyRewardRepository>(Rewards);
        services.AddSingleton<ILoyaltySettingsRepository>(Settings);
        services.AddSingleton<IOrbitClient>(orbit);

        services.AddSingleton<IConsentRepository>(new ConsentRepository(context));
        services.AddSingleton<IConsentService, ConsentService>();
        services.AddSingleton<ISegmentRepository>(new SegmentRepository(context));
        services.AddSingleton<ISegmentMemberRepository>(new SegmentMemberRepository(context));
        services.AddSingleton<ISegmentService, SegmentService>();
        services.AddSingleton<LoyaltyCacheWriter>();

        services.AddVumaMessaging();

        _services = services.BuildServiceProvider();
        Dispatcher = _services.GetRequiredService<IDispatcher>();
    }

    /// <summary>The Stage 03 dispatcher, with validation, transaction and logging in the chain.</summary>
    public IDispatcher Dispatcher { get; }

    /// <summary>The database this harness is over — forks connect to the same data.</summary>
    public string ConnectionString { get; }

    /// <summary>The database context under test.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The principal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The acting company.</summary>
    public Guid CompanyId { get; }

    /// <summary>The enrolled customer.</summary>
    public Guid CustomerId { get; }

    /// <summary>The Orbit ledger double. Tests take it down and bring it back.</summary>
    public InMemoryOrbitClient Orbit { get; }

    /// <summary>Member repository.</summary>
    public ILoyaltyMemberRepository Members { get; }

    /// <summary>Transaction repository.</summary>
    public ILoyaltyTransactionRepository Transactions { get; }

    /// <summary>Tier repository.</summary>
    public ILoyaltyTierRepository Tiers { get; }

    /// <summary>Reward repository.</summary>
    public ILoyaltyRewardRepository Rewards { get; }

    /// <summary>Settings repository.</summary>
    public ILoyaltySettingsRepository Settings { get; }

    /// <summary>Creates a harness over a fresh database with loyalty enabled for one company.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<LoyaltyHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Domain.Platform.Tenant seeded = Domain.Platform.Tenant.CreateWithSouthAfricanDefaults(
            "Loyalty Harness (Pty) Ltd", "Harness");
        seeded.Activate();

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        User clerk = User.Create(seeded.Id, "clerk", "Loyalty Clerk");

        TestPrincipalAccessor principal = new($"user:{clerk.Id}", terminalId: null);

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        context.Tenants.Add(seeded);
        context.Stores.Add(store);
        context.Users.Add(clerk);

        await context.SaveChangesAsync().ConfigureAwait(false);

        Guid companyId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();
        tenant.SetTenant(seeded.Id, store.Id);

        return new LoyaltyHarness(
            connectionString, context, tenant, clock, principal, seeded.Id, store.Id, companyId,
            customerId, new InMemoryOrbitClient());
    }

    /// <summary>
    /// Opens a second, independent dispatcher over the same database — for the tests that need
    /// two real concurrent transactions, which one shared context cannot hold.
    /// </summary>
    /// <remarks>
    /// The fork shares the orbit ledger double (the gate under test) but nothing else: its own
    /// context, repositories and dispatcher. Tenant, store, company and customer match.
    /// </remarks>
    public LoyaltyFork Fork()
    {
        VumaRetailDbContext context = TestDbContextFactory.For(
            ConnectionString, Clock, Principal, TenantContext);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(TenantContext);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IPrincipalAccessor>(Principal);

        services.AddSingleton<ILoyaltyMemberRepository>(new LoyaltyMemberRepository(context));
        services.AddSingleton<ILoyaltyTransactionRepository>(new LoyaltyTransactionRepository(context));
        services.AddSingleton<ILoyaltyTierRepository>(new LoyaltyTierRepository(context));
        services.AddSingleton<ILoyaltyRewardRepository>(new LoyaltyRewardRepository(context));
        services.AddSingleton<ILoyaltySettingsRepository>(new LoyaltySettingsRepository(context));
        services.AddSingleton<IOrbitClient>(Orbit);

        services.AddSingleton<IConsentRepository>(new ConsentRepository(context));
        services.AddSingleton<IConsentService, ConsentService>();
        services.AddSingleton<ISegmentRepository>(new SegmentRepository(context));
        services.AddSingleton<ISegmentMemberRepository>(new SegmentMemberRepository(context));
        services.AddSingleton<ISegmentService, SegmentService>();
        services.AddSingleton<LoyaltyCacheWriter>();

        services.AddVumaMessaging();

        ServiceProvider provider = services.BuildServiceProvider();
        return new LoyaltyFork(context, provider, provider.GetRequiredService<IDispatcher>());
    }

    /// <summary>Enables loyalty and enrolls the customer. Most tests start here.</summary>
    public async Task EnrollAsync()
    {
        await Dispatcher.SendAsync(new Application.Loyalty.Commands.ConfigureLoyaltyCommand(
            CompanyId, "ZAR", 1m, 365, true)).ConfigureAwait(false);
        await Dispatcher.SendAsync(new Application.Loyalty.Commands.EnrollMemberCommand(
            CompanyId, CustomerId, StoreId)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await Context.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>An independent dispatcher over the same database. See <c>LoyaltyHarness.Fork</c>.</summary>
/// <param name="Context">The fork's own context.</param>
/// <param name="Services">The fork's own container.</param>
/// <param name="Dispatcher">The fork's own dispatcher.</param>
public sealed record LoyaltyFork(
    VumaRetailDbContext Context, ServiceProvider Services, IDispatcher Dispatcher) : IAsyncDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync().ConfigureAwait(false);
        await Context.DisposeAsync().ConfigureAwait(false);
    }
}
