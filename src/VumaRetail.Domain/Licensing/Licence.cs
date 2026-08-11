using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// A signed monthly licence, exactly as the control plane issued it (<c>LICENSING.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// The row stores the signed document <em>and</em> its parsed fields. The document is the truth — it
/// is what the pinned Ed25519 public key verifies — and the columns are how the store queries what it
/// already verified without re-parsing a signature on every request. They are written in one place, at
/// the moment the signature checked out, so they cannot drift apart.
/// </para>
/// <para>
/// <see cref="IImmutableRecord"/>: a licence is never edited. A plan change, a suspension and a
/// reactivation each issue a new licence with a higher
/// <see cref="IssuanceCounter"/>, and the counter is what makes a replayed old document useless — a
/// restored backup carrying last month's licence cannot extend anything, because a counter at or below
/// the highest seen is refused and flagged (<c>LICENSING.md</c> §7).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.AppendOnly)]
public sealed class Licence : Entity, IImmutableRecord
{
    private Licence(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Licence()
    {
    }

    /// <summary>The activation this licence was issued against.</summary>
    public Guid ActivationId { get; private set; }

    /// <summary>The signed document, verbatim. Everything else on this row was read out of it.</summary>
    public string Document { get; private set; } = string.Empty;

    /// <summary>The vendor's plan code, for the licence screen and support calls.</summary>
    public string PlanCode { get; private set; } = string.Empty;

    /// <summary>The module flags this licence enables, as a JSON array.</summary>
    public string Entitlements { get; private set; } = "[]";

    /// <summary>The plan's quantities, as a JSON document.</summary>
    public string Limits { get; private set; } = "{}";

    /// <summary>When the control plane issued it, UTC.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>When it stops being current, UTC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The hardware digest the licence is bound to.</summary>
    public string FingerprintDigest { get; private set; } = string.Empty;

    /// <summary>The issuance nonce, reported at every heartbeat so a clone is visible to the vendor.</summary>
    public string Nonce { get; private set; } = string.Empty;

    /// <summary>
    /// The monotonic issuance counter. Strictly increasing per tenant, for the life of the tenant.
    /// </summary>
    public long IssuanceCounter { get; private set; }

    /// <summary>The entitlement flags, parsed.</summary>
    public IReadOnlySet<string> EntitlementSet() => FingerprintJson.ReadEntitlements(Entitlements);

    /// <summary>The plan's quantities, parsed.</summary>
    public LicenceLimits LimitSet() => FingerprintJson.ReadLimits(Limits);

    /// <summary>Records a licence whose signature has already been verified.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store this server runs, where it runs one.</param>
    /// <param name="activationId">The activation it was issued against.</param>
    /// <param name="document">The signed document.</param>
    /// <param name="planCode">The vendor's plan code.</param>
    /// <param name="entitlements">The module flags it enables.</param>
    /// <param name="limits">The plan's quantities.</param>
    /// <param name="issuedAt">When it was issued, UTC.</param>
    /// <param name="expiresAt">When it stops being current, UTC.</param>
    /// <param name="fingerprintDigest">The hardware digest it is bound to.</param>
    /// <param name="nonce">The issuance nonce.</param>
    /// <param name="issuanceCounter">The monotonic issuance counter.</param>
    /// <returns>The licence.</returns>
    public static Licence Record(
        Guid tenantId,
        Guid? storeId,
        Guid activationId,
        string document,
        string planCode,
        IEnumerable<string> entitlements,
        LicenceLimits limits,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string fingerprintDigest,
        string nonce,
        long issuanceCounter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(limits);

        return new Licence(tenantId, storeId)
        {
            ActivationId = activationId,
            Document = document,
            PlanCode = planCode,
            Entitlements = FingerprintJson.WriteEntitlements(entitlements),
            Limits = FingerprintJson.WriteLimits(limits),
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            FingerprintDigest = fingerprintDigest,
            Nonce = nonce,
            IssuanceCounter = issuanceCounter,
        };
    }
}
