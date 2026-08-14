using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Partners.Permissions;

/// <summary>What the <c>partners</c> module lets somebody do.</summary>
public sealed class PartnerPermissions : IModulePermissions
{
    /// <summary>See partners.</summary>
    public const string PartnerView = "partners.partner.view";

    /// <summary>Create a partner.</summary>
    public const string PartnerCreate = "partners.partner.create";

    /// <summary>Edit a partner's details.</summary>
    public const string PartnerUpdate = "partners.partner.update";

    /// <summary>Retire a partner from new trading.</summary>
    public const string PartnerDeactivate = "partners.partner.deactivate";

    /// <inheritdoc />
    public string Module => "partners";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(PartnerView), "See suppliers and customers."),
        new(PermissionKey.Parse(PartnerCreate), "Create a supplier or customer record.", IsHighRisk: true),
        new(PermissionKey.Parse(PartnerUpdate), "Edit a partner's details.", IsHighRisk: true),
        new(PermissionKey.Parse(PartnerDeactivate), "Retire a partner from new trading.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>partners</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. No retail deployment functions without somebody to trade with — the same reasoning
/// <c>CatalogModuleManifest</c> records for having something to sell.
/// </remarks>
public sealed class PartnersModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "partners";

    /// <inheritdoc />
    public string LicenceFlag => "partners";

    /// <inheritdoc />
    public string Description => "Suppliers, customers and partners who are both.";

    /// <inheritdoc />
    public bool IsCore => true;
}
