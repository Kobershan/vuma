using Microsoft.EntityFrameworkCore;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Application.Identity;
using ZenithRetail.Application.Identity.Commands;
using ZenithRetail.Application.Identity.Permissions;
using ZenithRetail.Domain.Identity;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Infrastructure.Persistence;

namespace ZenithRetail.StoreServer;

/// <summary>
/// Builds a demonstrable tenant: two stores, three roles, staff with PINs, and an enrolled terminal.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/TESTING.md</c> §5 — every stage extends the seed so the whole system stays demonstrable,
/// and the seed doubles as the dataset for the Stage 31 DR drill. Stage 02 starts it with the only
/// things that exist: the platform root and identity.
/// </para>
/// <para>
/// Idempotent. Running it twice must not create a second copy of anything, because the first thing
/// anybody does with a seed script is run it again to see what it did.
/// </para>
/// </remarks>
public static class DemoSeed
{
    /// <summary>The demo tenant's fixed id, so a re-run finds what the last run made.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("01900000-0000-7000-8000-0000000000d0");

    /// <summary>Seeds the demo tenant.</summary>
    /// <param name="services">The host's service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        ZenithRetailDbContext context = provider.GetRequiredService<ZenithRetailDbContext>();
        ITenantContext tenantContext = provider.GetRequiredService<ITenantContext>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        Tenant tenant = await EnsureTenantAsync(context, tenantContext, unitOfWork, cancellationToken).ConfigureAwait(false);
        tenantContext.SetTenant(tenant.Id);

        Store johannesburg = await EnsureStoreAsync(context, unitOfWork, tenant.Id, "JHB01", "Zenith Sandton", cancellationToken)
            .ConfigureAwait(false);
        await EnsureStoreAsync(context, unitOfWork, tenant.Id, "CPT02", "Zenith Claremont", cancellationToken)
            .ConfigureAwait(false);

        Guid ownerRole = await EnsureRoleAsync(provider, "Owner", AllPermissions(provider), cancellationToken)
            .ConfigureAwait(false);

        Guid managerRole = await EnsureRoleAsync(
            provider,
            "Store Manager",
            [
                PlatformPermissions.StoreView,
                PlatformPermissions.AuditView,
                IdentityPermissions.UserView,
                IdentityPermissions.UserManage,
                IdentityPermissions.RoleView,
                IdentityPermissions.TerminalView,
                IdentityPermissions.TerminalEnrol,
            ],
            cancellationToken).ConfigureAwait(false);

        Guid cashierRole = await EnsureRoleAsync(
            provider,
            "Cashier",
            [PlatformPermissions.StoreView],
            cancellationToken).ConfigureAwait(false);

        await EnsureUserAsync(provider, "owner", "Thandi Mokoena", "ChangeMe-Owner-2026", ownerRole, null, null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "manager", "Riaan de Villiers", "ChangeMe-Manager-2026", managerRole, johannesburg.Id, "4821", cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "cashier1", "Naledi Dlamini", "ChangeMe-Cashier-2026", cashierRole, johannesburg.Id, "1174", cancellationToken)
            .ConfigureAwait(false);

        await EnsureTerminalAsync(provider, context, johannesburg.Id, "T01", "Front counter 1", cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyCollection<string> AllPermissions(IServiceProvider provider)
        => [.. provider.GetRequiredService<IPermissionCatalogue>().All.Select(descriptor => descriptor.Key.Value)];

    private static async Task<Tenant> EnsureTenantAsync(
        ZenithRetailDbContext context,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // The tenant row is the one read that legitimately happens before a tenant is resolved: the
        // seeder is asking whether the demo tenant exists at all.
        using IDisposable bypass = tenantContext.BypassTenantFilter("seeding the demo tenant");

        Tenant? existing = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Id == DemoTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Zenith Demo Retail (Pty) Ltd", "Zenith Demo");
        SetId(tenant, DemoTenantId);
        tenant.Activate();

        context.Tenants.Add(tenant);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return tenant;
    }

    private static async Task<Store> EnsureStoreAsync(
        ZenithRetailDbContext context,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        Store? existing = await context.Stores
            .FirstOrDefaultAsync(store => store.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Store store = Store.Create(tenantId, code, name);
        context.Stores.Add(store);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return store;
    }

    private static async Task<Guid> EnsureRoleAsync(
        IServiceProvider provider,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        IRoleRepository roles = provider.GetRequiredService<IRoleRepository>();

        if (await roles.FindByNameAsync(name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<ICommandHandler<CreateRoleCommand, Guid>>()
            .HandleAsync(new CreateRoleCommand(name, permissions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureUserAsync(
        IServiceProvider provider,
        string userName,
        string displayName,
        string password,
        Guid roleId,
        Guid? storeId,
        string? pin,
        CancellationToken cancellationToken)
    {
        IUserRepository users = provider.GetRequiredService<IUserRepository>();

        if (await users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        Guid userId = await provider
            .GetRequiredService<ICommandHandler<CreateUserCommand, Guid>>()
            .HandleAsync(new CreateUserCommand(userName, displayName, password), cancellationToken)
            .ConfigureAwait(false);

        await provider
            .GetRequiredService<ICommandHandler<AssignRoleCommand, Guid>>()
            .HandleAsync(new AssignRoleCommand(userId, roleId, storeId), cancellationToken)
            .ConfigureAwait(false);

        if (pin is not null)
        {
            await provider
                .GetRequiredService<ICommandHandler<SetUserPinCommand, Unit>>()
                .HandleAsync(new SetUserPinCommand(userId, pin), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsureTerminalAsync(
        IServiceProvider provider,
        ZenithRetailDbContext context,
        Guid storeId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        ITerminalRepository terminals = provider.GetRequiredService<ITerminalRepository>();

        if (await terminals.FindByCodeAsync(storeId, code, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        TerminalEnrolment enrolment = await provider
            .GetRequiredService<ICommandHandler<EnrolTerminalCommand, TerminalEnrolment>>()
            .HandleAsync(new EnrolTerminalCommand(storeId, code, name), cancellationToken)
            .ConfigureAwait(false);

        // Printed rather than stored: an activation code is shown once and never persisted in
        // plaintext, and a demo is no reason to make an exception to that.
        Console.WriteLine($"Terminal {code} enrolled. Activation code: {enrolment.EnrolmentCode}");
        Console.WriteLine($"  expires {enrolment.ExpiresAt:u}");

        _ = context;
    }

    /// <summary>
    /// Pins the demo tenant's id so a re-run finds the same tenant.
    /// </summary>
    /// <remarks>
    /// <see cref="Tenant"/> generates a UUID v7 on creation, which is right for real tenants and
    /// wrong for a fixture that has to be findable. Reflection rather than a public setter, because a
    /// public "change this entity's identity" method would be usable from a module.
    /// </remarks>
    private static void SetId(Tenant tenant, Guid id)
    {
        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.Id))!
            .SetValue(tenant, id);

        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.TenantId))!
            .SetValue(tenant, id);
    }
}
