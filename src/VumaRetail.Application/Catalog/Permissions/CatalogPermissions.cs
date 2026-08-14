using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Catalog.Permissions;

/// <summary>What the <c>catalog</c> module — items, variants, barcodes, units of measure — lets somebody do.</summary>
public sealed class CatalogPermissions : IModulePermissions
{
    /// <summary>See items, variants and barcodes.</summary>
    public const string ItemView = "catalog.item.view";

    /// <summary>Create an item or a variant.</summary>
    public const string ItemCreate = "catalog.item.create";

    /// <summary>Edit an item's details.</summary>
    public const string ItemUpdate = "catalog.item.update";

    /// <summary>Retire an item from sale and receipt.</summary>
    public const string ItemDeactivate = "catalog.item.deactivate";

    /// <summary>Create, rename or retire a unit of measure.</summary>
    public const string UnitOfMeasureManage = "catalog.uom.manage";

    /// <summary>Attach, promote or remove a barcode.</summary>
    public const string BarcodeManage = "catalog.barcode.manage";

    /// <inheritdoc />
    public string Module => "catalog";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(ItemView), "See items, variants and barcodes."),
        new(PermissionKey.Parse(ItemCreate), "Create an item or a variant.", IsHighRisk: true),
        new(PermissionKey.Parse(ItemUpdate), "Edit an item's details.", IsHighRisk: true),
        new(PermissionKey.Parse(ItemDeactivate), "Retire an item from sale and receipt.", IsHighRisk: true),
        new(PermissionKey.Parse(UnitOfMeasureManage), "Create, rename or retire a unit of measure.", IsHighRisk: true),
        new(PermissionKey.Parse(BarcodeManage), "Attach, promote or remove a barcode.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>catalog</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. No retail deployment functions without a product to sell — the same reasoning
/// <c>PlatformModuleManifest</c> and <c>IdentityModuleManifest</c> already record.
/// </remarks>
public sealed class CatalogModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "catalog";

    /// <inheritdoc />
    public string LicenceFlag => "catalog";

    /// <inheritdoc />
    public string Description => "Items, variants, barcodes and units of measure.";

    /// <inheritdoc />
    public bool IsCore => true;
}
