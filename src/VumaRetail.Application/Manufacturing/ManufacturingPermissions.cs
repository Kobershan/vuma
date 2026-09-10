using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Permissions exposed by Stage 16 manufacturing definitions.</summary>
public sealed class ManufacturingPermissions : IModulePermissions
{
    /// <summary>Read BOM definitions.</summary>
    public const string View = "manufacturing.bom.view";

    /// <summary>Create and publish BOM definitions.</summary>
    public const string Manage = "manufacturing.bom.manage";

    /// <inheritdoc />
    public string Module => "manufacturing";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View bills of materials."),
        new(PermissionKey.Parse(Manage), "Create and publish bills of materials.", IsHighRisk: true),
    ];
}
