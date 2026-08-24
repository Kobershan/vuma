using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Licensing.Signing;

namespace VumaRetail.Licensing;

/// <summary>
/// Everything about licensing a deployment can change (<c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// Both ladders are "vendor-configurable per plan and per tenant", and the shipped values here are the
/// documented defaults. They are configuration rather than constants for a reason that is easy to miss:
/// the tolerance window is the difference between a rural store with intermittent connectivity being a
/// customer and being a support burden, and the vendor has to be able to widen it for one tenant
/// without a release.
/// </para>
/// <para>
/// A per-tenant override arrives inside the signed lease, not from this file — a customer who could
/// edit their own tolerance window would have edited it. What is here is the deployment's default and
/// the floor a store falls back to when it has never held a lease.
/// </para>
/// </remarks>
public sealed class LicensingOptions : IOpenSessionWindows
{
    /// <summary>The configuration section: <c>Vuma:Licensing</c>.</summary>
    public const string SectionName = "Vuma:Licensing";

    /// <summary>
    /// The pinned Ed25519 public key, base64. Defaults to the development key.
    /// </summary>
    /// <remarks>
    /// The development key's private half is derived from a seed compiled into
    /// <see cref="LicenceSigner"/> — anybody can mint a licence with it. That is exactly why
    /// <see cref="UsesDevelopmentKey"/> exists and why the host refuses to start on it outside
    /// Development, the same arrangement Stage 02 made for the JWT signing key.
    /// </remarks>
    public string PublicKey { get; set; } = LicenceSigner.DevelopmentPublicKey;

    /// <summary>True when the pinned key is still the one that ships in the binaries.</summary>
    public bool UsesDevelopmentKey
        => string.Equals(PublicKey, LicenceSigner.DevelopmentPublicKey, StringComparison.Ordinal);

    /// <summary>Where the vendor's device API lives.</summary>
    public string ControlPlaneBaseAddress { get; set; } = string.Empty;

    /// <summary>How long a control-plane call may take before it counts as unreachable.</summary>
    /// <remarks>
    /// Short on purpose. Every one of these calls is off the UI thread and none of them is on a
    /// trading path, so a long timeout buys nothing and a heartbeat that hangs for two minutes is two
    /// minutes of a thread doing nothing on a shop's back-office PC.
    /// </remarks>
    public TimeSpan ControlPlaneTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a lease lives. <c>LICENSING.md</c> §2: 72 hours.</summary>
    public TimeSpan LeaseLifetime { get; set; } = TimeSpan.FromHours(72);

    /// <summary>How often a lease is refreshed while everything is fine.</summary>
    public TimeSpan LeaseRefreshInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How often a heartbeat goes out while everything is fine.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often a heartbeat goes out while the tenant is restricted or being warned.
    /// </summary>
    /// <remarks>
    /// Thirty seconds, because <c>LICENSING.md</c> §4 promises full access "within 60 seconds" of a
    /// payment landing and the heartbeat is what notices. A customer who has just paid is watching the
    /// screen; that is not the moment to be on a five-minute timer.
    /// </remarks>
    public TimeSpan RestrictedHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Path A: days of silence before a back-office banner appears. Default 4.</summary>
    public int OfflineBannerAfterDays { get; set; } = 4;

    /// <summary>Path A: days of silence before a POS session notice appears. Default 8.</summary>
    public int OfflinePosNoticeAfterDays { get; set; } = 8;

    /// <summary>
    /// Path A: days of silence before the tenant goes read-only. Default 15, configurable 1–45.
    /// </summary>
    /// <remarks>
    /// Measured from the last <em>successful</em> exchange with the control plane, not from lease
    /// expiry. A lease lives 72 hours; restricting on its expiry would put every customer in the world
    /// into read-only three days into a vendor outage, which is the disaster ADR-028's fourth rule
    /// exists to prevent.
    /// </remarks>
    public int OfflineReadOnlyAfterDays { get; set; } = 15;

    /// <summary>
    /// Path B: days after a <em>completed</em> dunning cycle before read-only. Default 0.
    /// </summary>
    /// <remarks>
    /// Zero is not harsh. By the time dunning has completed the customer has had 14 days of escalating
    /// warning with a working payment link at every step (<c>LICENSING.md</c> §4) — the grace already
    /// happened, out loud.
    /// </remarks>
    public int DunningGraceDays { get; set; } = 0;

    /// <summary>The longest an emergency write code may be issued for. <c>LICENSING.md</c> §5: 168 hours.</summary>
    public TimeSpan MaximumEmergencyCodeLifetime { get; set; } = TimeSpan.FromHours(168);

    /// <summary>
    /// Whether an in-progress sale or cash-up may be completed after read-only falls due.
    /// </summary>
    /// <remarks>
    /// On by default, and vendor-configurable off. It exists so a restriction does not leave a drawer
    /// of unrecorded cash and a customer holding goods; no <em>new</em> sale can start either way.
    /// </remarks>
    public bool OpenSessionCarveOut { get; set; } = true;

    /// <summary>
    /// ADR-135's first hard deadline: how long a sale that was open when read-only fell due may still be
    /// rung, tendered, completed or voided before the carve-out expires. Default 30 minutes.
    /// </summary>
    /// <remarks>
    /// This, not <see cref="OpenSessionCarveOut"/> alone, is what stops the carve-out being a trading
    /// allowance: without it, a tenant who never closes a sale keeps ringing new lines onto it
    /// indefinitely. Measured from the sale's own <c>OpenedAt</c>, evaluated against the watermarked
    /// clock so winding the system clock back buys nothing (<c>LICENSING.md</c> §7).
    /// </remarks>
    public TimeSpan InFlightSaleWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// ADR-135's second hard deadline: how long a till session that was open when read-only fell due may
    /// still be closed before the carve-out expires. Default 12 hours — long enough that a shift can
    /// always end.
    /// </summary>
    public TimeSpan InFlightCashUpWindow { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Whether an unactivated installation is read-only.
    /// </summary>
    /// <remarks>
    /// True in production: a store server that has never been activated has no subscription and may
    /// not trade. It is configurable because the DR path and the automated drills need a store server
    /// that starts and answers before a control plane exists to activate against.
    /// </remarks>
    public bool RequireActivation { get; set; } = true;

    /// <summary>How long a tenant-approved vendor support grant lasts by default.</summary>
    public TimeSpan DefaultSupportGrantDuration { get; set; } = TimeSpan.FromHours(4);

    /// <summary>How many metering rollups are attempted per delivery pass.</summary>
    public int MeteringBatchSize { get; set; } = 45;
}
