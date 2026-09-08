using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Registry.Trading;

/// <summary>
/// What the mixed-basket trading session lets somebody do (Stage 09b).
/// </summary>
/// <remarks>
/// Ringing a sister company's line is the high-risk act here: one scan writes toward another
/// company's books, so it is a separate permission from the till's own
/// <c>pos.sale.ring</c> rather than implied by it. Overriding the tender split moves money
/// between companies' receipts, so it is gated separately too.
/// </remarks>
public sealed class TradingSessionPermissions : IModulePermissions
{
    /// <summary>Add a sister company's line to a shared-till basket.</summary>
    public const string BasketMixed = "trading.basket.mixed";

    /// <summary>Override the proportional tender split with an exact one.</summary>
    public const string BasketAllocateOverride = "trading.basket.override";

    /// <summary>Abandon a trading session with a reason.</summary>
    public const string BasketVoid = "trading.basket.void";

    /// <inheritdoc />
    public string Module => "trading";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(BasketMixed), "Add a sister company's line to a shared-till basket.", IsHighRisk: true),
        new(PermissionKey.Parse(BasketAllocateOverride), "Override the proportional tender split.", IsHighRisk: true),
        new(PermissionKey.Parse(BasketVoid), "Abandon a trading session with a reason.", IsHighRisk: true),
    ];
}

/// <summary>
/// The trading-session module's manifest (R7).
/// </summary>
/// <remarks>
/// Gated behind the <c>multicompany</c> licence flag <b>and</b> the <c>SharedTill</c> link
/// scope: the flag says the tenant bought multi-company operation, the link says these two
/// companies may share a till. Either missing, and the basket refuses.
/// </remarks>
public sealed class TradingModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "trading";

    /// <inheritdoc />
    public string LicenceFlag => "multicompany";

    /// <inheritdoc />
    public string Description => "Mixed basket — one till selling for two companies, one tax invoice each.";

    /// <inheritdoc />
    public bool IsCore => false;
}
