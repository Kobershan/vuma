#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Reporting;

public sealed class ReportingPermissions : IModulePermissions
{
    public const string View = "reporting.view";
    public const string Manage = "reporting.manage";
    public string Module => "reporting";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View scoped reports and dashboard freshness."),
        new(PermissionKey.Parse(Manage), "Manage report definitions and exports.", IsHighRisk: true),
    ];
}

public sealed class ReportingModuleManifest : IModuleManifest
{
    public string Module => "reporting";
    public string LicenceFlag => "reporting";
    public string Description => "Scoped reports, dashboard projections and exports.";
    public bool IsCore => false;
}
