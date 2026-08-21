using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Orders.Permissions;

/// <summary>
/// What the <c>orders</c> module — sales orders, allocation, backorders, click &amp; collect and order
/// returns — lets somebody do.
/// </summary>
public sealed class OrdersPermissions : IModulePermissions
{
    /// <summary>See orders, their lines and their returns.</summary>
    public const string View = "orders.order.view";

    /// <summary>Raise a draft order and add lines to it.</summary>
    public const string Create = "orders.order.create";

    /// <summary>Price and accept an order.</summary>
    public const string Confirm = "orders.order.confirm";

    /// <summary>Reattempt backordered allocations.</summary>
    public const string Allocate = "orders.order.allocate";

    /// <summary>Cancel an order or one of its lines.</summary>
    public const string Cancel = "orders.order.cancel";

    /// <summary>Refresh fulfilment and complete an order, recognising its revenue.</summary>
    public const string Complete = "orders.order.complete";

    /// <summary>Raise an order return.</summary>
    public const string ReturnCreate = "orders.return.create";

    /// <summary>Complete an order return, receiving stock back.</summary>
    public const string ReturnComplete = "orders.return.complete";

    /// <inheritdoc />
    public string Module => "orders";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "See orders, their lines and their returns."),
        new(PermissionKey.Parse(Create), "Raise a draft order and add lines to it."),
        new(PermissionKey.Parse(Confirm), "Price and accept an order.", IsHighRisk: true),
        new(PermissionKey.Parse(Allocate), "Reattempt backordered allocations.", IsHighRisk: true),
        new(PermissionKey.Parse(Cancel), "Cancel an order or one of its lines.", IsHighRisk: true),
        new(PermissionKey.Parse(Complete), "Complete an order, recognising its revenue.", IsHighRisk: true),
        new(PermissionKey.Parse(ReturnCreate), "Raise an order return."),
        new(PermissionKey.Parse(ReturnComplete), "Complete an order return, receiving stock back.", IsHighRisk: true),
    ];
}

/// <summary>The <c>orders</c> module's manifest (R7).</summary>
/// <remarks>
/// Not core — a single-till shop with no counter/phone order-taking is a legitimate deployment (the
/// stage document's own reasoning). A tenant that enables it gets promise-to-fulfil orders, backorders
/// and click &amp; collect on top of Stage 09's till and Stage 13's warehouse, both of which keep
/// working unchanged either way.
/// </remarks>
public sealed class OrdersModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "orders";

    /// <inheritdoc />
    public string LicenceFlag => "orders";

    /// <inheritdoc />
    public string Description => "Sales orders, allocation, backorders, click & collect and order returns.";

    /// <inheritdoc />
    public bool IsCore => false;
}
