using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Identity.Queries;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Identity;

/// <summary>
/// One tenant, one store, and the identity services wired over a real database.
/// </summary>
/// <remarks>
/// Built by hand rather than through a container. Stage 03 owns the dispatcher and its DI scan
/// (ADR-009); assembling one here would be building half of that stage's deliverable in a shape it
/// then has to unpick. Constructing the handlers directly also makes it obvious which collaborators
/// each one actually has.
/// </remarks>
public sealed class IdentityHarness : IAsyncDisposable
{
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
    }

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
        Guid userId = await new CreateUserCommandHandler(Users, PasswordHasher, TenantContext, Context)
            .HandleAsync(new CreateUserCommand(userName, userName, password));

        if (pin is not null)
        {
            await new SetUserPinCommandHandler(Users, PasswordHasher, Context)
                .HandleAsync(new SetUserPinCommand(userId, pin));
        }

        if (roleId is { } role)
        {
            await new AssignRoleCommandHandler(Users, Roles, TenantContext, Context)
                .HandleAsync(new AssignRoleCommand(userId, role, storeId));
        }

        return userId;
    }

    /// <summary>Creates a role granting the given permissions.</summary>
    /// <param name="name">The role name.</param>
    /// <param name="permissions">The permissions it grants.</param>
    public Task<Guid> CreateRoleAsync(string name, params string[] permissions)
        => new CreateRoleCommandHandler(Roles, Catalogue, TenantContext, Context)
            .HandleAsync(new CreateRoleCommand(name, permissions));

    /// <summary>Enrols and activates a terminal, returning it ready to authenticate.</summary>
    /// <param name="code">The terminal code.</param>
    /// <param name="thumbprint">The certificate thumbprint to pin.</param>
    public async Task<Guid> CreateActiveTerminalAsync(string code = "T01", string? thumbprint = null)
    {
        TerminalEnrolment enrolment = await EnrolTerminalAsync(code);

        return await new ActivateTerminalCommandHandler(Terminals, PasswordHasher, Clock, Context)
            .HandleAsync(new ActivateTerminalCommand(
                StoreId,
                enrolment.EnrolmentCode,
                thumbprint ?? Thumbprint(code),
                "board-serial-1"));
    }

    /// <summary>Enrols a terminal without activating it.</summary>
    /// <param name="code">The terminal code.</param>
    /// <param name="codeLifetime">How long the activation code lives.</param>
    public Task<TerminalEnrolment> EnrolTerminalAsync(string code = "T01", TimeSpan? codeLifetime = null)
        => new EnrolTerminalCommandHandler(Terminals, PasswordHasher, TokenHasher, TenantContext, Clock, Context)
            .HandleAsync(new EnrolTerminalCommand(StoreId, code, $"Till {code}", codeLifetime));

    /// <summary>Resolves a user's effective permissions in a store.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="storeId">The store, or <c>null</c> for tenant-wide.</param>
    public Task<IReadOnlyCollection<string>> PermissionsAsync(Guid userId, Guid? storeId)
        => new GetEffectivePermissionsQueryHandler(Roles, Catalogue)
            .HandleAsync(new GetEffectivePermissionsQuery(userId, storeId));

    /// <summary>A deterministic 64-character hex thumbprint derived from a terminal code.</summary>
    /// <param name="seed">Anything unique per terminal.</param>
    public static string Thumbprint(string seed)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
