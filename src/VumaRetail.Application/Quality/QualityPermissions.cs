#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Quality;

public sealed class QualityPermissions : IModulePermissions
{
    public const string View = "quality.view";
    public const string Manage = "quality.manage";
    public string Module => "quality";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View quality holds."),
        new(PermissionKey.Parse(Manage), "Place and dispose quality holds.", IsHighRisk: true),
    ];
}

public sealed class QualityModuleManifest : IModuleManifest
{
    public string Module => "quality";
    public string LicenceFlag => "quality";
    public string Description => "Inspection evidence, quarantine and quality disposition.";
    public bool IsCore => false;
}
