using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Licensing.Permissions;

/// <summary>
/// What the <c>licensing</c> module lets somebody do.
/// </summary>
/// <remarks>
/// <c>licence.view</c> is deliberately mild and deliberately separate from the rest. Everybody in a
/// back office should be able to see why the software is complaining and what the amount due is;
/// almost nobody should be able to type an emergency access code or answer a vendor's request to look
/// at the tenant's data.
/// </remarks>
public sealed class LicensingPermissions : IModulePermissions
{
    /// <summary>See the licence screen: plan, entitlements, usage, expiry, enforcement level.</summary>
    public const string LicenceView = "licensing.licence.view";

    /// <summary>Activate this installation, or move its binding to different hardware.</summary>
    public const string LicenceActivate = "licensing.licence.activate";

    /// <summary>Fetch a lease now — the licence screen's "retry now" button.</summary>
    public const string LicenceRefresh = "licensing.licence.refresh";

    /// <summary>Type in a vendor emergency access code.</summary>
    public const string LicenceRedeem = "licensing.licence.redeem";

    /// <summary>See what has been metered and sent to the vendor.</summary>
    public const string MeteringView = "licensing.metering.view";

    /// <summary>See vendor support-access requests and grants, past and present.</summary>
    public const string SupportView = "licensing.support.view";

    /// <summary>Approve, decline or end a vendor's access to this tenant's data.</summary>
    public const string SupportDecide = "licensing.support.decide";

    /// <inheritdoc />
    public string Module => "licensing";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(LicenceView), "See the licence, the plan and what is owed."),
        new(
            PermissionKey.Parse(LicenceActivate),
            "Activate Vuma on this machine, or move the licence to a new one.",
            IsHighRisk: true),
        new(PermissionKey.Parse(LicenceRefresh), "Check the licence service for an update now."),
        new(
            PermissionKey.Parse(LicenceRedeem),
            "Enter an emergency access code from Vuma support.",
            IsHighRisk: true),
        new(PermissionKey.Parse(MeteringView), "See the usage counts reported to Vuma."),
        new(PermissionKey.Parse(SupportView), "See when Vuma support has asked for, or had, access."),
        new(
            PermissionKey.Parse(SupportDecide),
            "Let Vuma support see this business's data, or end their access.",
            IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>licensing</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core: a tenant whose licence somehow failed to enable licensing would be a tenant who cannot see
/// why, cannot pay, and cannot be helped.
/// </remarks>
public sealed class LicensingModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "licensing";

    /// <inheritdoc />
    public string LicenceFlag => "licensing";

    /// <inheritdoc />
    public string Description => "Licence, subscription status and vendor support access.";

    /// <inheritdoc />
    public bool IsCore => true;
}
