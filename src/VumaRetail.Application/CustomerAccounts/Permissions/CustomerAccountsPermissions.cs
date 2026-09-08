using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.CustomerAccounts.Permissions;

/// <summary>What the <c>customer-accounts</c> module lets somebody do.</summary>
public sealed class CustomerAccountsPermissions : IModulePermissions
{
    /// <summary>Open accounts, set limits, hold and release, authorise buyers, take payments.</summary>
    public const string AccountManage = "customeraccounts.account.manage";

    /// <summary>See accounts, statements, ageing and credit checks.</summary>
    public const string AccountView = "customeraccounts.account.view";

    /// <summary>Open lay-bys, take instalments, complete, cancel and expire them.</summary>
    public const string LayByManage = "customeraccounts.layby.manage";

    /// <inheritdoc />
    public string Module => "customer-accounts";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(AccountManage), "Open and run customer credit accounts.", IsHighRisk: true),
        new(PermissionKey.Parse(AccountView), "View accounts, statements and ageing."),
        new(PermissionKey.Parse(LayByManage), "Run lay-by agreements end to end.", IsHighRisk: true),
    ];
}

/// <summary>The <c>customer-accounts</c> module's manifest (R7). Not core: a cash-only shop trades
/// without credit, lay-by or stokvels.</summary>
public sealed class CustomerAccountsModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "customer-accounts";

    /// <inheritdoc />
    public string LicenceFlag => "customer-accounts";

    /// <inheritdoc />
    public string Description => "Customer credit accounts, lay-by and stokvels.";

    /// <inheritdoc />
    public bool IsCore => false;
}
