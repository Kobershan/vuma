using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Warehouse.Permissions;

/// <summary>
/// What the <c>warehouse</c> module — zones, bins, putaway, pick/pack/ship and cycle counts — lets
/// somebody do.
/// </summary>
public sealed class WarehousePermissions : IModulePermissions
{
    /// <summary>See zones, bins, bin stock and cycle counts.</summary>
    public const string View = "warehouse.stock.view";

    /// <summary>Create or retire a zone or bin.</summary>
    public const string LayoutManage = "warehouse.layout.manage";

    /// <summary>Open a putaway task.</summary>
    public const string PutawayManage = "warehouse.putaway.manage";

    /// <summary>Confirm a putaway task, shelving stock into a bin.</summary>
    public const string PutawayConfirm = "warehouse.putaway.confirm";

    /// <summary>Open and release a pick wave, and allocate its lines.</summary>
    public const string PickManage = "warehouse.pick.manage";

    /// <summary>Confirm a pick against its allocation.</summary>
    public const string PickConfirm = "warehouse.pick.confirm";

    /// <summary>Pack a picked wave.</summary>
    public const string PackConfirm = "warehouse.pack.confirm";

    /// <summary>Confirm a wave's shipment — stock leaves the location.</summary>
    public const string ShipConfirm = "warehouse.ship.confirm";

    /// <summary>Open a cycle count, record counts against it, and finalize it.</summary>
    public const string CycleCountManage = "warehouse.cyclecount.manage";

    /// <inheritdoc />
    public string Module => "warehouse";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "See zones, bins, bin stock and cycle counts."),
        new(PermissionKey.Parse(LayoutManage), "Create or retire a zone or bin.", IsHighRisk: true),
        new(PermissionKey.Parse(PutawayManage), "Open a putaway task.", IsHighRisk: true),
        new(PermissionKey.Parse(PutawayConfirm), "Confirm a putaway task, shelving stock into a bin.", IsHighRisk: true),
        new(PermissionKey.Parse(PickManage), "Open and release a pick wave, and allocate its lines.", IsHighRisk: true),
        new(PermissionKey.Parse(PickConfirm), "Confirm a pick against its allocation.", IsHighRisk: true),
        new(PermissionKey.Parse(PackConfirm), "Pack a picked wave.", IsHighRisk: true),
        new(PermissionKey.Parse(ShipConfirm), "Confirm a wave's shipment — stock leaves the location.", IsHighRisk: true),
        new(PermissionKey.Parse(CycleCountManage), "Open a cycle count, record counts, and finalize it.", IsHighRisk: true),
    ];
}

/// <summary>The <c>warehouse</c> module's manifest (R7).</summary>
/// <remarks>
/// Not core — a single-location shop with no bin discipline is a legitimate deployment (the stage
/// document's own reasoning). A tenant that enables it gets bin-level putaway, picking and cycle
/// counts on top of Stage 08's location-level ledger, which keeps working unchanged either way.
/// </remarks>
public sealed class WarehouseModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "warehouse";

    /// <inheritdoc />
    public string LicenceFlag => "warehouse";

    /// <inheritdoc />
    public string Description => "Zones, bins, putaway, pick/pack/ship and cycle counts.";

    /// <inheritdoc />
    public bool IsCore => false;
}
