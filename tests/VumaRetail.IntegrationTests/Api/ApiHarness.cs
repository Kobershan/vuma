using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Contracts.Identity;
using VumaRetail.Domain.Platform;
using VumaRetail.IntegrationTests.Harness;

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

    private ApiHarness(WebApplicationFactory<Program> factory, Guid tenantId, Guid storeId)
    {
        _factory = factory;
        TenantId = tenantId;
        StoreId = storeId;
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

    /// <summary>Boots the store server against a fresh database with a tenant and a store in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<ApiHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

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
            });

        return new ApiHarness(factory, tenantId, storeId);
    }

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
