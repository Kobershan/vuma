using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Permissions exposed by BOM setup and Stage 17 manufacturing execution.</summary>
public sealed class ManufacturingPermissions : IModulePermissions
{
    /// <summary>Read BOM definitions, production orders, genealogy, and capacity.</summary>
    public const string View = "manufacturing.bom.view";

    /// <summary>Create, publish, and execute manufacturing orders.</summary>
    public const string Manage = "manufacturing.production.manage";

    /// <inheritdoc />
    public string Module => "manufacturing";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View bills of materials."),
        new(PermissionKey.Parse(Manage), "Create and publish bills of materials.", IsHighRisk: true),
    ];
}
