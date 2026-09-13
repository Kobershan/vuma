#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Assets;

public sealed class AssetPermissions : IModulePermissions
{
    public const string Manage = "assets.asset.manage";
    public string Module => "assets";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(Manage), "Create, maintain and depreciate fixed assets.", IsHighRisk: true),
    ];
}

public sealed class AssetModuleManifest : IModuleManifest
{
    public string Module => "assets";
    public string LicenceFlag => "assets";
    public string Description => "Fixed assets, depreciation and maintenance orders.";
    public bool IsCore => false;
}
