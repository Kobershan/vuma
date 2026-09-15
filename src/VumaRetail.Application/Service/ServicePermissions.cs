#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Service;

/// <summary>Granular permissions for service coordination.</summary>
public sealed class ServicePermissions : IModulePermissions
{
    public const string View = "service.ticket.view";
    public const string Manage = "service.ticket.manage";
    public const string ApproveWarranty = "service.warranty.approve";
    public const string Export = "service.custody.export";
    public string Module => "service";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View service tickets and custody records."),
        new(PermissionKey.Parse(Manage), "Manage service tickets, repairs and parts.", IsHighRisk: true),
        new(PermissionKey.Parse(ApproveWarranty), "Approve warranty claims.", IsHighRisk: true),
        new(PermissionKey.Parse(Export), "Export customer custody records.", IsHighRisk: true),
    ];
}

/// <summary>Licensing manifest for service management.</summary>
public sealed class ServiceModuleManifest : IModuleManifest
{
    public string Module => "service";
    public string LicenceFlag => "service";
    public string Description => "Customer service, warranty, repairs and custody.";
    public bool IsCore => false;
}
