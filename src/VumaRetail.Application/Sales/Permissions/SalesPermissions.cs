using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Sales.Permissions;

/// <summary>
/// What the <c>sales</c> module — price lists, promotions, returns and the override log — lets
/// somebody do.
/// </summary>
/// <remarks>
/// <para>
/// The split follows who actually does these things in a shop. <b>Reading a price is separated from
/// setting one</b>, because every cashier needs the first on every scan and nobody on the floor needs
/// the second. <b>Raising a return is separated from completing one</b>, because that is the control:
/// a cashier can build the credit slip while the customer waits and a supervisor releases the money.
/// </para>
/// <para>
/// <see cref="PriceOverride"/> is high risk and deliberately not folded into
/// <c>pos.sale.discount</c>. A discount is a reduction the shop configured; an override is a cashier
/// disagreeing with the shop's price, and a store that grants one may well not want to grant the other.
/// </para>
/// </remarks>
public sealed class SalesPermissions : IModulePermissions
{
    /// <summary>Ask what something should sell for, and see price lists and promotions.</summary>
    public const string PriceView = "sales.price.view";

    /// <summary>Create, amend, activate and deactivate price lists and their lines.</summary>
    public const string PriceManage = "sales.price.manage";

    /// <summary>Create, amend, activate and deactivate promotions.</summary>
    public const string PromotionManage = "sales.promotion.manage";

    /// <summary>Sell at something other than the resolved price, with a recorded reason.</summary>
    public const string PriceOverride = "sales.price.override";

    /// <summary>See returns and the price override log.</summary>
    public const string ReturnView = "sales.return.view";

    /// <summary>Raise a return and put lines on it.</summary>
    public const string ReturnRaise = "sales.return.raise";

    /// <summary>Complete a return: refund the money, put the stock back, raise the financial event.</summary>
    public const string ReturnComplete = "sales.return.complete";

    /// <inheritdoc />
    public string Module => "sales";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(PriceView), "Resolve a price and see price lists and promotions."),
        new(PermissionKey.Parse(PriceManage), "Create and amend price lists.", IsHighRisk: true),
        new(PermissionKey.Parse(PromotionManage), "Create and amend promotions.", IsHighRisk: true),
        new(PermissionKey.Parse(PriceOverride), "Sell off-price, with a recorded reason.", IsHighRisk: true),
        new(PermissionKey.Parse(ReturnView), "See returns and the price override log."),
        new(PermissionKey.Parse(ReturnRaise), "Raise a return against a completed sale."),
        new(PermissionKey.Parse(ReturnComplete), "Complete a return and refund the money.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>sales</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not core</b>, the same call <c>PosModuleManifest</c> made and for a related reason. A shop can
/// trade without a promotions engine: Stage 09 ships a till that sells at a price the caller supplies,
/// and a small hardware store that keys its prices off the shelf label needs a stock ledger and books,
/// not a specials engine. What it cannot do without this module is run a special as configuration, and
/// that is exactly the kind of thing a customer chooses to buy.
/// </para>
/// <para>
/// The returns half is the uncomfortable part of that argument — a shop that takes goods back needs
/// this module — and it is why the flag is one module rather than two. Splitting pricing from returns
/// would let a tenant buy the ability to refund without the ability to price the refund, and the refund
/// is priced off the original line either way.
/// </para>
/// </remarks>
public sealed class SalesModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "sales";

    /// <inheritdoc />
    public string LicenceFlag => "sales";

    /// <inheritdoc />
    public string Description => "Sales management — price lists, promotions, returns and price overrides.";

    /// <inheritdoc />
    public bool IsCore => false;
}
