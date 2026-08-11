using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Contracts.Identity;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Platform;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The store server itself, over a real database, reachable by HTTP.
/// </summary>
/// <remarks>
/// <c>docs/TESTING.md</c> §1's API level. It runs <c>VumaRetail.StoreServer</c>'s own
/// <c>Program.cs</c> rather than a re-creation of it, because the thing most worth asserting is the
/// wiring — middleware order, the error contract, the OpenAPI document — and a re-creation asserts
/// only that the test author remembered it.
/// </remarks>
public sealed class ApiHarness : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    private ApiHarness(WebApplicationFactory<Program> factory, Guid tenantId, Guid storeId, TestClock clock)
    {
        _factory = factory;
        TenantId = tenantId;
        StoreId = storeId;
        Clock = clock;
        Client = factory.CreateClient();
    }

    /// <summary>An HTTP client pointed at the running host.</summary>
    public HttpClient Client { get; }

    /// <summary>The running host's container, for the tests that inspect its wiring.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>The seeded tenant, which is also the host's own tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>
    /// The host's clock, which the test moves by hand.
    /// </summary>
    /// <remarks>
    /// Substituted for <c>SystemClock</c> in the running host. Stage 04b's ladders run over days and
    /// weeks; the alternative is a test suite that cannot assert a single boundary.
    /// </remarks>
    public TestClock Clock { get; }

    /// <summary>The key this installation was activated with, for a test that wants to change its plan.</summary>
    public LicenceKey LicenceKey { get; private set; }

    /// <summary>The in-process control plane the host wired, for the tests that drive the vendor.</summary>
    /// <remarks>
    /// The store server registers it in Development when no control-plane address is configured, which
    /// is the case here and in CI. It signs real documents with the development key, so the
    /// verification path under test is the production one (ADR-022).
    /// </remarks>
    public InProcessControlPlane ControlPlane => Services.GetRequiredService<InProcessControlPlane>();

    /// <summary>Boots the store server against a fresh database with a tenant and a store in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    /// <param name="activate">
    /// Whether to activate the installation. True for every test that is not about activation itself:
    /// Stage 04b's guard refuses business writes on an unactivated store, which is correct in
    /// production and would otherwise turn every other stage's API tests into licensing tests.
    /// </param>
    public static async Task<ApiHarness> CreateAsync(PostgresFixture fixture, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        // Started at the real instant rather than at TestClock.DefaultStart. The JWT bearer handler
        // validates lifetimes against the system clock and cannot be given this one, so a host whose
        // clock says January would issue tokens the framework rejects as expired before the test could
        // use them. Starting from now keeps both clocks agreeing until a test deliberately moves this
        // one — and a licensing test that moves it by a fortnight drives commands through the
        // dispatcher, where no token is involved.
        TestClock clock = new(DateTimeOffset.UtcNow);

        string connectionString = await fixture.CreateDatabaseAsync();

        Guid tenantId;
        Guid storeId;

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(
            connectionString,
            new TestClock(),
            new TestPrincipalAccessor("system:test", isSystem: true),
            TestTenantContext.Unfiltered()))
        {
            Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("API Retail (Pty) Ltd", "API");
            tenant.Activate();
            seed.Tenants.Add(tenant);

            Store store = Store.Create(tenant.Id, "JHB01", "API Sandton");
            seed.Stores.Add(store);

            await seed.CommitAsync();

            tenantId = tenant.Id;
            storeId = store.Id;
        }

        WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Development, so the host accepts the shipped placeholder signing key. Outside it,
                // Program.cs refuses to start on that key at all, which is the point of the check.
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:Vuma", connectionString);
                builder.UseSetting("Vuma:Host:TenantId", tenantId.ToString());
                builder.UseSetting("Vuma:Host:StoreId", storeId.ToString());
                builder.UseSetting(
                    "Vuma:Licensing:StateDirectory",
                    Path.Combine(Path.GetTempPath(), "vuma-tests", Guid.NewGuid().ToString()));

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(clock);
                });
            });

        ApiHarness harness = new(factory, tenantId, storeId, clock);

        if (activate)
        {
            harness.LicenceKey = await harness.ActivateAsync();
        }

        return harness;
    }

    /// <summary>
    /// Registers a tenant with the in-process control plane and activates this installation against it.
    /// </summary>
    /// <param name="entitlements">The modules the plan includes. Defaults to every declared module.</param>
    /// <param name="limits">The plan's quantities. Defaults to unlimited.</param>
    /// <returns>The licence key, so a test can change the tenant's subscription afterwards.</returns>
    public async Task<LicenceKey> ActivateAsync(
        IReadOnlyList<string>? entitlements = null,
        LicenceLimits? limits = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(TenantId, StoreId);

        LicenceKey key = ControlPlane.Register(new ControlPlaneTenant
        {
            TenantId = TenantId,
            StoreId = StoreId,
            PlanCode = "test",
            Entitlements = entitlements
                ?? [.. scope.ServiceProvider.GetServices<IModuleManifest>().Select(manifest => manifest.LicenceFlag)],
            Limits = limits ?? LicenceLimits.Unlimited,
        });

        await scope.ServiceProvider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new ActivateInstallationCommand(key.Value, "API Sandton", "owner@api.example"));

        return key;
    }

    /// <summary>Runs work in a request-like scope with the tenant resolved, as an endpoint would.</summary>
    /// <typeparam name="TResult">What the work returns.</typeparam>
    /// <param name="work">The work.</param>
    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        using IServiceScope scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(TenantId, StoreId);

        return await work(scope.ServiceProvider);
    }

    /// <summary>Sends a command through the running host's own pipeline.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command.</param>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command)
        => InScopeAsync(provider => provider.GetRequiredService<IDispatcher>().SendAsync(command));

    /// <summary>Sends a query through the running host's own pipeline.</summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="query">The query.</param>
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query)
        => InScopeAsync(provider => provider.GetRequiredService<IDispatcher>().QueryAsync(query));

    /// <summary>Creates a back-office user, optionally holding a role, through the real pipeline.</summary>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="password">The password.</param>
    /// <param name="permissions">Permissions to grant through a role created for the purpose.</param>
    public async Task<Guid> CreateUserAsync(
        string userName,
        string password = "CorrectHorseBattery1",
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        using IServiceScope scope = _factory.Services.CreateScope();

        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(TenantId, StoreId);

        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Guid userId = await dispatcher.SendAsync(new CreateUserCommand(userName, userName, password));

        if (permissions.Length > 0)
        {
            Guid roleId = await dispatcher.SendAsync(new CreateRoleCommand($"role-{userName}", permissions));
            await dispatcher.SendAsync(new AssignRoleCommand(userId, roleId));
        }

        return userId;
    }

    /// <summary>Signs in over HTTP and returns a client carrying the access token.</summary>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="password">The password.</param>
    public async Task<HttpClient> SignInAsync(string userName, string password = "CorrectHorseBattery1")
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new SignInRequest(userName, password));

        response.EnsureSuccessStatusCode();

        TokenResponse token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        HttpClient authenticated = _factory.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return authenticated;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
