using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Inventory.Permissions;

/// <summary>
/// What the <c>inventory</c> module — locations, the stock ledger, balances, transfers and stocktakes —
/// lets somebody do.
/// </summary>
public sealed class InventoryPermissions : IModulePermissions
{
    /// <summary>See locations, balances and the ledger.</summary>
    public const string StockView = "inventory.stock.view";

    /// <summary>Create or retire a stock location.</summary>
    public const string LocationManage = "inventory.location.manage";

    /// <summary>Post a receipt.</summary>
    public const string StockReceive = "inventory.stock.receive";

    /// <summary>Post a sale issue — Stage 09 (POS) is this permission's real caller.</summary>
    public const string StockIssue = "inventory.stock.issue";

    /// <summary>Post a documented adjustment.</summary>
    public const string StockAdjust = "inventory.stock.adjust";

    /// <summary>Transfer stock between locations.</summary>
    public const string StockTransfer = "inventory.stock.transfer";

    /// <summary>Open a stocktake session and record counts against it.</summary>
    public const string StocktakeManage = "inventory.stocktake.manage";

    /// <summary>Finalize a stocktake session, posting every line's variance to the ledger.</summary>
    public const string StocktakeFinalize = "inventory.stocktake.finalize";

    /// <inheritdoc />
    public string Module => "inventory";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(StockView), "See locations, balances and the stock ledger."),
        new(PermissionKey.Parse(LocationManage), "Create or retire a stock location.", IsHighRisk: true),
        new(PermissionKey.Parse(StockReceive), "Post a stock receipt.", IsHighRisk: true),
        new(PermissionKey.Parse(StockIssue), "Post a stock issue for a sale.", IsHighRisk: true),
        new(PermissionKey.Parse(StockAdjust), "Post a documented stock adjustment.", IsHighRisk: true),
        new(PermissionKey.Parse(StockTransfer), "Transfer stock between locations.", IsHighRisk: true),
        new(PermissionKey.Parse(StocktakeManage), "Open a stocktake session and record counts.", IsHighRisk: true),
        new(PermissionKey.Parse(StocktakeFinalize), "Finalize a stocktake, posting its variances to the ledger.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>inventory</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. A retail ERP with no stock tracking is not a usable deployment — the same reasoning
/// <c>CatalogModuleManifest</c> and <c>PartnersModuleManifest</c> already record for the modules this
/// one builds directly on.
/// </remarks>
public sealed class InventoryModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "inventory";

    /// <inheritdoc />
    public string LicenceFlag => "inventory";

    /// <inheritdoc />
    public string Description => "Stock ledger, locations, adjustments, transfers and stocktakes.";

    /// <inheritdoc />
    public bool IsCore => true;
}
