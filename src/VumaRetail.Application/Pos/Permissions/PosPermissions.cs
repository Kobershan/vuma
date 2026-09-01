using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Pos.Permissions;

/// <summary>
/// What the <c>pos</c> module — the till, its tenders, its receipts and its cash-up — lets somebody do.
/// </summary>
/// <remarks>
/// The split here is the one a shop floor actually needs: a cashier rings up and tenders, and a
/// supervisor voids, discounts, reprints and counts the drawer. Those are the four things a till
/// operator is not trusted with alone, and they are separate permissions rather than one
/// <c>pos.manage</c> so a store can grant them separately.
/// </remarks>
public sealed class PosPermissions : IModulePermissions
{
    /// <summary>See sales, receipts and till sessions.</summary>
    public const string SaleView = "pos.sale.view";

    /// <summary>Open a sale, ring lines up and park or resume it.</summary>
    public const string SaleRing = "pos.sale.ring";

    /// <summary>Take a payment and complete a sale.</summary>
    public const string SaleTender = "pos.sale.tender";

    /// <summary>Take a line off a sale, or abandon the whole sale.</summary>
    public const string SaleVoid = "pos.sale.void";

    /// <summary>Apply a manual discount to a line.</summary>
    public const string SaleDiscount = "pos.sale.discount";

    /// <summary>Print a receipt for the first time.</summary>
    public const string ReceiptPrint = "pos.receipt.print";

    /// <summary>Print a receipt again. Separate from the first print, and audited (see <c>ReceiptPrint</c>).</summary>
    public const string ReceiptReprint = "pos.receipt.reprint";

    /// <summary>Open a shift with a float.</summary>
    public const string TillOpen = "pos.till.open";

    /// <summary>Count the drawer and close a shift.</summary>
    public const string TillClose = "pos.till.close";

    /// <inheritdoc />
    public string Module => "pos";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(SaleView), "See sales, receipts and till sessions."),
        new(PermissionKey.Parse(SaleRing), "Open a sale and ring lines up."),
        new(PermissionKey.Parse(SaleTender), "Take a payment and complete a sale.", IsHighRisk: true),
        new(PermissionKey.Parse(SaleVoid), "Void a sale line or a whole sale.", IsHighRisk: true),
        new(PermissionKey.Parse(SaleDiscount), "Apply a manual discount to a sale line.", IsHighRisk: true),
        new(PermissionKey.Parse(ReceiptPrint), "Print a receipt."),
        new(PermissionKey.Parse(ReceiptReprint), "Reprint a receipt, with a recorded reason.", IsHighRisk: true),
        new(PermissionKey.Parse(TillOpen), "Open a till session with a float.", IsHighRisk: true),
        new(PermissionKey.Parse(TillClose), "Count the drawer and close a till session.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>pos</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not core</b>, unlike <c>catalog</c>, <c>partners</c>, <c>inventory</c> and <c>finance</c>
/// (ADR-064). Those four are the floor a deployment cannot run below — a tenant with no chart of
/// accounts or no stock ledger is not a working install. A till genuinely is optional: a wholesaler
/// invoicing from the back office, or a warehouse-only site in a multi-store tenant, has an inventory
/// and books and no cash drawer at all.
/// </para>
/// <para>
/// It is therefore the first real test of the entitlement gate on a module that a customer would
/// plausibly not buy. <c>DemoSeed</c> grants every declared flag, so the demo tenant is unaffected.
/// </para>
/// </remarks>
public sealed class PosModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "pos";

    /// <inheritdoc />
    public string LicenceFlag => "pos";

    /// <inheritdoc />
    public string Description => "Point of sale — sales, tenders, receipts, cash-up and till hardware.";

    /// <inheritdoc />
    public bool IsCore => false;
}
