using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Identity.Queries;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Identity;

/// <summary>
/// One tenant, one store, and the identity services wired over a real database — through the Stage
/// 03 dispatcher and its pipeline.
/// </summary>
/// <remarks>
/// The collaborators are constructed by hand and handed to a container, rather than resolved from a
/// host, so a test can still reach in and move the clock or change tenant. What goes through the
/// container is the part that matters: commands run the real validation and transaction behaviours,
/// which is where the commit now happens (ADR-044).
/// </remarks>
public sealed class IdentityHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private IdentityHarness(VumaRetailDbContext context, TestTenantContext tenant, TestClock clock, Guid tenantId, Guid storeId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        TenantId = tenantId;
        StoreId = storeId;

        Users = new UserRepository(context);
        Roles = new RoleRepository(context);
        Terminals = new TerminalRepository(context, tenant);
        Tokens = new RefreshTokenRepository(context, tenant);

        PasswordHasher = new IdentityPasswordHasher();
        TokenHasher = new Sha256TokenHasher();
        Catalogue = new PermissionCatalogue([new PlatformPermissions(), new IdentityPermissions()]);
        Issuer = new JwtTokenIssuer(
            new JwtOptions { SigningKey = new string('k', 64) },
            clock);

        Authentication = new AuthenticationService(
            Users,
            Terminals,
            Tokens,
            PasswordHasher,
            TokenHasher,
            Issuer,
            context,
            tenant,
            clock);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(Users);
        services.AddSingleton(Roles);
        services.AddSingleton(Terminals);
        services.AddSingleton(Tokens);
        services.AddSingleton(PasswordHasher);
        services.AddSingleton(TokenHasher);
        services.AddSingleton(Catalogue);
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

    /// <summary>User repository.</summary>
    public IUserRepository Users { get; }

    /// <summary>Role, grant and assignment repository.</summary>
    public IRoleRepository Roles { get; }

    /// <summary>Terminal repository.</summary>
    public ITerminalRepository Terminals { get; }

    /// <summary>Refresh token repository.</summary>
    public IRefreshTokenRepository Tokens { get; }

    /// <summary>The password, PIN and enrolment-code hasher.</summary>
    public IPasswordHasher PasswordHasher { get; }

    /// <summary>The refresh token digester.</summary>
    public ITokenHasher TokenHasher { get; }

    /// <summary>The access token issuer.</summary>
    public ITokenIssuer Issuer { get; }

    /// <summary>The permission catalogue.</summary>
    public IPermissionCatalogue Catalogue { get; }

    /// <summary>Sign-in, refresh, sign-out and terminal authentication.</summary>
    public AuthenticationService Authentication { get; }

    /// <summary>Creates a harness over a fresh database with a tenant and a store already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<IdentityHarness> CreateAsync(PostgresFixture fixture)
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

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Harness Retail (Pty) Ltd", "Harness");
        seeded.Activate();
        context.Tenants.Add(seeded);

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        context.Stores.Add(store);

        await context.CommitAsync();

        // Arranged unfiltered, exercised filtered — a handler test that never leaves the bypass open
        // would prove nothing about the tenant filter it runs under in production.
        tenant.SetTenant(seeded.Id, store.Id);
        tenant.EndBypass();

        return new IdentityHarness(context, tenant, clock, seeded.Id, store.Id);
    }

    /// <summary>Creates a user with a password, and optionally a PIN and a role.</summary>
    /// <param name="userName">The sign-in name.</param>
    /// <param name="password">The password.</param>
    /// <param name="pin">A POS PIN, if the user works a till.</param>
    /// <param name="roleId">A role to assign.</param>
    /// <param name="storeId">The store to scope the assignment to, or <c>null</c> for tenant-wide.</param>
    public async Task<Guid> CreateUserAsync(
        string userName,
        string password = "CorrectHorseBattery1",
        string? pin = null,
        Guid? roleId = null,
        Guid? storeId = null)
    {
        Guid userId = await SendAsync(new CreateUserCommand(userName, userName, password));

        if (pin is not null)
        {
            await SendAsync(new SetUserPinCommand(userId, pin));
        }

        if (roleId is { } role)
        {
            await SendAsync(new AssignRoleCommand(userId, role, storeId));
        }

        return userId;
    }

    /// <summary>Sends a command through the real pipeline.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command.</param>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) => Dispatcher.SendAsync(command);

    /// <summary>Sends a query through the real pipeline.</summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="query">The query.</param>
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query) => Dispatcher.QueryAsync(query);

    /// <summary>Creates a role granting the given permissions.</summary>
    /// <param name="name">The role name.</param>
    /// <param name="permissions">The permissions it grants.</param>
    public Task<Guid> CreateRoleAsync(string name, params string[] permissions)
        => SendAsync(new CreateRoleCommand(name, permissions));

    /// <summary>Enrols and activates a terminal, returning it ready to authenticate.</summary>
    /// <param name="code">The terminal code.</param>
    /// <param name="thumbprint">The certificate thumbprint to pin.</param>
    public async Task<Guid> CreateActiveTerminalAsync(string code = "T01", string? thumbprint = null)
    {
        TerminalEnrolment enrolment = await EnrolTerminalAsync(code);

        return await SendAsync(new ActivateTerminalCommand(
            StoreId,
            enrolment.EnrolmentCode,
            thumbprint ?? Thumbprint(code),
            "board-serial-1"));
    }

    /// <summary>Enrols a terminal without activating it.</summary>
    /// <param name="code">The terminal code.</param>
    /// <param name="codeLifetime">How long the activation code lives.</param>
    public Task<TerminalEnrolment> EnrolTerminalAsync(string code = "T01", TimeSpan? codeLifetime = null)
        => SendAsync(new EnrolTerminalCommand(StoreId, code, $"Till {code}", codeLifetime));

    /// <summary>Resolves a user's effective permissions in a store.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="storeId">The store, or <c>null</c> for tenant-wide.</param>
    public Task<IReadOnlyCollection<string>> PermissionsAsync(Guid userId, Guid? storeId)
        => QueryAsync(new GetEffectivePermissionsQuery(userId, storeId));

    /// <summary>A deterministic 64-character hex thumbprint derived from a terminal code.</summary>
    /// <param name="seed">Anything unique per terminal.</param>
    public static string Thumbprint(string seed)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Context.DisposeAsync();
    }
}
