#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Marketing;

public sealed class MarketingPermissions : IModulePermissions
{
    public const string Manage = "marketing.campaign.manage";
    public string Module => "marketing";
    public IReadOnlyCollection<PermissionDescriptor> Permissions => [new(PermissionKey.Parse(Manage), "Manage marketing campaigns and outbound messages.", IsHighRisk: true)];
}
public sealed class MarketingModuleManifest : IModuleManifest
{ public string Module => "marketing"; public string LicenceFlag => "marketing"; public string Description => "Consent-aware marketing campaign delivery."; public bool IsCore => false; }
