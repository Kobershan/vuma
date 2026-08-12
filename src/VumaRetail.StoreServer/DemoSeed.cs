using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Domain.Workflow;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;
using VumaRetail.Workflow.Approvals;

namespace VumaRetail.StoreServer;

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

        VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();
        ITenantContext tenantContext = provider.GetRequiredService<ITenantContext>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        Tenant tenant = await EnsureTenantAsync(context, tenantContext, unitOfWork, cancellationToken).ConfigureAwait(false);
        tenantContext.SetTenant(tenant.Id);

        Store johannesburg = await EnsureStoreAsync(context, unitOfWork, tenant.Id, "JHB01", "Vuma Sandton", cancellationToken)
            .ConfigureAwait(false);
        await EnsureStoreAsync(context, unitOfWork, tenant.Id, "CPT02", "Vuma Claremont", cancellationToken)
            .ConfigureAwait(false);

        // Before anything else goes through the pipeline. Stage 04b's read-only guard refuses every
        // business write on an unactivated installation, which is correct in production and would
        // otherwise mean the demo seeder produced a tenant with no users in it.
        await EnsureActivationAsync(provider, tenant.Id, johannesburg.Id, cancellationToken)
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

        // Stage 05. docs/stages/STAGE-05-workflow.md's exit checklist: the module is demonstrable with
        // one approval policy and one notification, the same way every stage since 02 has extended
        // this seed rather than leaving its own module invisible in the demo tenant.
        await EnsureApprovalPolicyAsync(provider, cancellationToken).ConfigureAwait(false);
        await EnsureNotificationAsync(provider, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A gate on a plausible high-value action no module builds until later in the roadmap
    /// (inventory's stock adjustments are Stage 08) — Stage 05's whole point is that a module
    /// configures against the engine rather than building its own, and a policy is data, so it needs
    /// no inventory module to exist yet to be demonstrable.
    /// </summary>
    private static async Task EnsureApprovalPolicyAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        IApprovalPolicyRepository policies = provider.GetRequiredService<IApprovalPolicyRepository>();

        if (await policies.FindActiveAsync("inventory", "stock-adjustment", "post", cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new DefineApprovalPolicyCommand(
                    "inventory",
                    "stock-adjustment",
                    "post",
                    PlatformPermissions.StoreManage,
                    1000m,
                    "ZAR",
                    MinApprovals: 1,
                    AllowSelfApproval: false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One in-app notification for the seeded manager, so the notification inbox is not empty the
    /// first time anybody looks at the demo tenant.
    /// </summary>
    private static async Task EnsureNotificationAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        CancellationToken cancellationToken)
    {
        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        User? manager = await users.FindByUserNameAsync("manager", cancellationToken).ConfigureAwait(false);

        if (manager is null)
        {
            return;
        }

        bool alreadySeeded = await context.Notifications
            .AnyAsync(
                notification => notification.RecipientUserId == manager.Id
                    && notification.Category == "workflow.demo.welcome",
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadySeeded)
        {
            return;
        }

        await provider
            .GetRequiredService<INotificationDispatcher>()
            .NotifyAsync(
                new NotificationRequest(
                    manager.Id,
                    "workflow.demo.welcome",
                    "Approvals are configured",
                    "Stock adjustments of ZAR 1,000 or more now need a manager's approval before they "
                        + "post. Configure more policies from Approval Policies in the back office.",
                    NotificationSeverity.Info,
                    [NotificationChannel.InApp],
                    "workflow",
                    "approval-policy",
                    null,
                    "/workflow/approval-policies"),
                cancellationToken)
            .ConfigureAwait(false);

        await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Activates the demo installation against whichever control plane is wired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Development that is the in-process one, which signs real documents with the development key
    /// — so a demonstration is a fully licensed store with no vendor service anywhere, which is the
    /// difference between a demo that works on a laptop on a plane and one that does not.
    /// </para>
    /// <para>
    /// Against a real control plane there is nothing to register, and the seeder leaves activation to
    /// whoever holds the licence key. It says so rather than failing: a seeded demo against a
    /// production control plane is not a scenario anybody should be in by accident.
    /// </para>
    /// </remarks>
    private static async Task EnsureActivationAsync(
        IServiceProvider provider,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        IActivationRepository activations = provider.GetRequiredService<IActivationRepository>();

        if (await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (provider.GetService<InProcessControlPlane>() is not { } controlPlane)
        {
            Console.WriteLine(
                "Not activated: no in-process control plane is wired. Activate with a real licence "
                + "key through POST /api/v1/licence/activations.");

            return;
        }

        LicenceKey key = controlPlane.Register(new ControlPlaneTenant
        {
            TenantId = tenantId,
            StoreId = storeId,
            PlanCode = "demo",
            Entitlements = [.. provider.GetServices<IModuleManifest>().Select(manifest => manifest.LicenceFlag)],
            Limits = new LicenceLimits(2, 10, 25, 500_000, 50L * 1024 * 1024 * 1024, 1_000_000),
        });

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new ActivateInstallationCommand(key.Value, "Vuma Sandton", "owner@vuma.example"),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Activated the demo installation. Licence key: {key}");
    }

    private static IReadOnlyCollection<string> AllPermissions(IServiceProvider provider)
        => [.. provider.GetRequiredService<IPermissionCatalogue>().All.Select(descriptor => descriptor.Key.Value)];

    private static async Task<Tenant> EnsureTenantAsync(
        VumaRetailDbContext context,
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

        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Demo Retail (Pty) Ltd", "Vuma Demo");
        SetId(tenant, DemoTenantId);
        tenant.Activate();

        context.Tenants.Add(tenant);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return tenant;
    }

    private static async Task<Store> EnsureStoreAsync(
        VumaRetailDbContext context,
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
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateRoleCommand(name, permissions), cancellationToken)
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

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        Guid userId = await dispatcher
            .SendAsync(new CreateUserCommand(userName, displayName, password), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher
            .SendAsync(new AssignRoleCommand(userId, roleId, storeId), cancellationToken)
            .ConfigureAwait(false);

        if (pin is not null)
        {
            await dispatcher
                .SendAsync(new SetUserPinCommand(userId, pin), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsureTerminalAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
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
            .GetRequiredService<IDispatcher>()
            .SendAsync(new EnrolTerminalCommand(storeId, code, name), cancellationToken)
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
