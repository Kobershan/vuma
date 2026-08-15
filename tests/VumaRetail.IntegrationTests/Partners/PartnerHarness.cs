using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Partners;

/// <summary>
/// One tenant, one store, and the <c>partners</c> module wired over a real database — through the
/// Stage 03 dispatcher and its pipeline. Same shape as <c>IdentityHarness</c> and <c>CatalogHarness</c>.
/// </summary>
public sealed class PartnerHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PartnerHarness(VumaRetailDbContext context, TestTenantContext tenant, TestClock clock, Guid tenantId, Guid storeId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        TenantId = tenantId;
        StoreId = storeId;

        Partners = new PartnerRepository(context);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(Partners);
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

    /// <summary>Partner repository.</summary>
    public IPartnerRepository Partners { get; }

    /// <summary>Creates a harness over a fresh database with a tenant and a store already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<PartnerHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        VumaRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            clock,
            new TestPrincipalAccessor("user:arranger"),
            tenant);

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Partner Harness (Pty) Ltd", "Harness");
        seeded.Activate();
        context.Tenants.Add(seeded);

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        context.Stores.Add(store);

        await context.CommitAsync();

        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new PartnerHarness(context, tenant, clock, seeded.Id, store.Id);
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
