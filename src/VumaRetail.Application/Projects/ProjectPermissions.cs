#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Projects;

public sealed class ProjectPermissions : IModulePermissions
{
    public const string Manage = "projects.project.manage";
    public string Module => "projects";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(Manage), "Create projects and approve project commitments.", IsHighRisk: true),
    ];
}

