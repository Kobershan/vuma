using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity.Permissions;

/// <summary>
/// What the <c>identity</c> module lets somebody do.
/// </summary>
/// <remarks>
/// The first declaration in the system, and the template every module stage copies. Keep the
/// constants next to the declaration so an endpoint can name a permission without a magic string, and
/// so deleting the declaration breaks the endpoint at compile time rather than at run time.
/// </remarks>
public sealed class IdentityPermissions : IModulePermissions
{
    /// <summary>See the user list and a user's roles.</summary>
    public const string UserView = "identity.user.view";

    /// <summary>Create a user.</summary>
    public const string UserCreate = "identity.user.create";

    /// <summary>Edit a user, reset their password or set their POS PIN.</summary>
    public const string UserManage = "identity.user.manage";

    /// <summary>See the roles and what they grant.</summary>
    public const string RoleView = "identity.role.view";

    /// <summary>Create a role and decide what it grants.</summary>
    public const string RoleManage = "identity.role.manage";

    /// <summary>Give a user a role, in one store or across the tenant.</summary>
    public const string RoleAssign = "identity.role.assign";

    /// <summary>See the enrolled terminals.</summary>
    public const string TerminalView = "identity.terminal.view";

    /// <summary>Enrol a new terminal and issue its one-time activation code.</summary>
    public const string TerminalEnrol = "identity.terminal.enrol";

    /// <summary>Revoke a terminal, ending its ability to trade.</summary>
    public const string TerminalRevoke = "identity.terminal.revoke";

    /// <inheritdoc />
    public string Module => "identity";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(UserView), "See users and the roles they hold."),
        new(PermissionKey.Parse(UserCreate), "Create a user.", IsHighRisk: true),
        new(PermissionKey.Parse(UserManage), "Edit a user, reset a password, set a POS PIN.", IsHighRisk: true),
        new(PermissionKey.Parse(RoleView), "See roles and what they grant."),
        new(PermissionKey.Parse(RoleManage), "Create and change roles.", IsHighRisk: true),
        // The one that can grant every other one. If a tenant reviews a single permission, this is it.
        new(PermissionKey.Parse(RoleAssign), "Give a user a role.", IsHighRisk: true),
        new(PermissionKey.Parse(TerminalView), "See enrolled terminals."),
        new(PermissionKey.Parse(TerminalEnrol), "Enrol a terminal and issue its activation code.", IsHighRisk: true),
        new(PermissionKey.Parse(TerminalRevoke), "Revoke a terminal.", IsHighRisk: true),
    ];
}

/// <summary>
/// What the <c>platform</c> module — tenants, stores, the audit trail — lets somebody do.
/// </summary>
public sealed class PlatformPermissions : IModulePermissions
{
    /// <summary>See tenant settings: localisation, currency, timezone.</summary>
    public const string TenantView = "platform.tenant.view";

    /// <summary>Change tenant settings. Every one of these is per-tenant configuration (CLAUDE.md §9).</summary>
    public const string TenantManage = "platform.tenant.manage";

    /// <summary>See the stores.</summary>
    public const string StoreView = "platform.store.view";

    /// <summary>Create and configure a store.</summary>
    public const string StoreManage = "platform.store.manage";

    /// <summary>Read the audit trail. Reading who did what is itself an audited capability (R6).</summary>
    public const string AuditView = "platform.audit.view";

    /// <inheritdoc />
    public string Module => "platform";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(TenantView), "See tenant settings."),
        new(PermissionKey.Parse(TenantManage), "Change tenant settings.", IsHighRisk: true),
        new(PermissionKey.Parse(StoreView), "See stores."),
        new(PermissionKey.Parse(StoreManage), "Create and configure stores.", IsHighRisk: true),
        new(PermissionKey.Parse(AuditView), "Read the audit trail.", IsHighRisk: true),
    ];
}
