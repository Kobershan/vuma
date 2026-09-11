#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Connect;

public sealed class ConnectPermissions : IModulePermissions
{
    public const string View = "connect.network.view";
    public const string Manage = "connect.relationship.manage";
    public const string Publish = "connect.catalogue.publish";
    public const string Decide = "connect.proposal.decide";
    public const string Order = "connect.order.manage";
    public string Module => "connect";
    public IReadOnlyCollection<PermissionDescriptor> Permissions => [new(PermissionKey.Parse(View), "View Connect relationships and proposals."), new(PermissionKey.Parse(Manage), "Manage supplier relationships and codes.", true), new(PermissionKey.Parse(Publish), "Publish catalogue and prices.", true), new(PermissionKey.Parse(Decide), "Accept or reject supplier price proposals.", true), new(PermissionKey.Parse(Order), "Place and fulfil Connect orders.", true)];
}
public sealed class ConnectModuleManifest : IModuleManifest
{
    public string Module => "connect";
    public string LicenceFlag => "connect";
    public string Description => "Supplier and retailer trading network.";
    public bool IsCore => false;
}
