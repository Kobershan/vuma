using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// This installation's binding to one licence key and one machine (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// <para>
/// One row per store server, and it is the anchor everything else in the module hangs off: the licence
/// is issued against it, the lease is derived from the licence, and the terminals take sub-leases from
/// the store server that holds it.
/// </para>
/// <para>
/// <see cref="ReplicationScope.NodeLocal"/>, and that is a decision rather than an omission. The
/// binding describes <em>this box</em> — its hardware, its install id, its rebind history. Replicating
/// it to the cloud would put every store's hardware profile in one database for no operational gain;
/// the control plane already holds what the vendor needs, and a <c>pg_dump</c> restore (R4) carries
/// node-local rows along with everything else.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class Activation : Entity
{
    private Activation(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Activation()
    {
    }

    /// <summary>SHA-256 of the licence key, lower-case hex. The key itself is never stored.</summary>
    public string LicenceKeyDigest { get; private set; } = string.Empty;

    /// <summary>
    /// The control plane's own id for this activation, quoted on every device-API call.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Entity.Id"/>, and it has to be. The row's id is generated here
    /// (ADR-004) and the vendor's is generated there; using one for the other would work exactly
    /// until the first machine that activated, restored and rebound, at which point the store would
    /// be quoting an id the vendor has never heard of.
    /// </remarks>
    public Guid ActivationReference { get; private set; }

    /// <summary>
    /// This installation's identity, stable across restarts and reinstalls of the same database.
    /// </summary>
    /// <remarks>
    /// Distinct from the boot id, which changes on every start. The control plane compares the pair to
    /// tell a restarted store from a cloned one (<c>LICENSING.md</c> §7).
    /// </remarks>
    public Guid InstallId { get; private set; }

    /// <summary>The sync node id this activation belongs to — <c>store:{id}</c>.</summary>
    public string NodeId { get; private set; } = string.Empty;

    /// <summary>The salt the component hashes were taken under, base64.</summary>
    public string FingerprintSalt { get; private set; } = string.Empty;

    /// <summary>The salted per-component hashes, as JSON. Raw identifiers are never persisted.</summary>
    public string FingerprintComponents { get; private set; } = "{}";

    /// <summary>The whole fingerprint's digest, which is what the licence payload is bound to.</summary>
    public string FingerprintDigest { get; private set; } = string.Empty;

    /// <summary>Where this activation stands.</summary>
    public ActivationState State { get; private set; } = ActivationState.Active;

    /// <summary>When the binding was first made, UTC.</summary>
    public DateTimeOffset ActivatedAt { get; private set; }

    /// <summary>How many times this activation has been rebound to different hardware.</summary>
    public int RebindCount { get; private set; }

    /// <summary>When the binding was last moved, UTC.</summary>
    public DateTimeOffset? LastRebindAt { get; private set; }

    /// <summary>
    /// The last time the control plane was successfully reached, UTC.
    /// </summary>
    /// <remarks>
    /// The one field the Path A ladder measures from (<c>LICENSING.md</c> §4). It advances on any
    /// successful exchange — heartbeat, lease refresh or metering — because all three prove the same
    /// thing: the vendor could have told this store its subscription had lapsed, and did not.
    /// </remarks>
    public DateTimeOffset? LastContactAt { get; private set; }

    /// <summary>
    /// Opens a binding between a licence key and this machine.
    /// </summary>
    /// <param name="tenantId">The tenant the control plane resolved the key to.</param>
    /// <param name="storeId">The store this server runs, where it runs one.</param>
    /// <param name="licenceKey">The key, hashed on the way in.</param>
    /// <param name="installId">This installation's stable id.</param>
    /// <param name="nodeId">The sync node id.</param>
    /// <param name="fingerprint">The machine reading taken at activation.</param>
    /// <param name="activationReference">The control plane's own id for this activation.</param>
    /// <param name="at">When, UTC.</param>
    /// <returns>The activation.</returns>
    public static Activation Open(
        Guid tenantId,
        Guid? storeId,
        LicenceKey licenceKey,
        Guid installId,
        string nodeId,
        HardwareFingerprint fingerprint,
        Guid activationReference,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        return new Activation(tenantId, storeId)
        {
            LicenceKeyDigest = licenceKey.Digest(),
            ActivationReference = activationReference,
            InstallId = installId,
            NodeId = nodeId,
            FingerprintSalt = fingerprint.Salt,
            FingerprintComponents = FingerprintJson.Write(fingerprint.ComponentHashes),
            FingerprintDigest = fingerprint.Digest(),
            State = ActivationState.Active,
            ActivatedAt = at,
            LastContactAt = at,
        };
    }

    /// <summary>The stored fingerprint, rebuilt.</summary>
    public HardwareFingerprint Fingerprint()
        => HardwareFingerprint.FromStored(FingerprintSalt, FingerprintJson.Read(FingerprintComponents));

    /// <summary>
    /// Scores a fresh machine reading against the bound one.
    /// </summary>
    /// <param name="components">The raw reading, hashed here under the stored salt.</param>
    /// <returns>0 to <see cref="HardwareFingerprint.MaxScore"/>.</returns>
    /// <remarks>
    /// The raw values are hashed and discarded inside this call. Nothing outside the fingerprint type
    /// ever sees them, which is what keeps "raw hardware identifiers are never persisted" a property of
    /// the code rather than a habit.
    /// </remarks>
    public int ScoreAgainst(IReadOnlyDictionary<FingerprintComponent, string> components)
        => Fingerprint().Score(HardwareFingerprint.Capture(FingerprintSalt, components));

    /// <summary>True when a fresh reading is still the same machine, within tolerance.</summary>
    /// <param name="components">The raw reading.</param>
    public bool StillTheSameMachine(IReadOnlyDictionary<FingerprintComponent, string> components)
    {
        HardwareFingerprint bound = Fingerprint();

        // Through the fingerprint's own rule rather than against the constant, so the threshold scales
        // with what this machine was ever able to report. A second comparison written against
        // MatchThreshold directly would quietly disagree with the first on any machine that cannot
        // read all five components — which is every virtual machine, and every Windows box until
        // Stage 31 supplies the WMI readings.
        return bound.Matches(HardwareFingerprint.Capture(FingerprintSalt, components));
    }

    /// <summary>
    /// Moves the binding to different hardware.
    /// </summary>
    /// <param name="fingerprint">The new machine's reading, under a new salt.</param>
    /// <param name="at">When, UTC.</param>
    /// <remarks>
    /// A rebind is a vendor-authorised act — the control plane decides whether it is within the
    /// self-service allowance, and auto-approves it for a disaster-recovery restore
    /// (<c>LICENSING.md</c> §3). This method records the decision; it does not make it.
    /// </remarks>
    public void RebindTo(HardwareFingerprint fingerprint, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        FingerprintSalt = fingerprint.Salt;
        FingerprintComponents = FingerprintJson.Write(fingerprint.ComponentHashes);
        FingerprintDigest = fingerprint.Digest();
        State = ActivationState.Active;
        RebindCount++;
        LastRebindAt = at;
    }

    /// <summary>Records that the control plane was reached and answered.</summary>
    /// <param name="at">When, UTC.</param>
    /// <remarks>
    /// <b>Only ever called after a real answer</b>, never after a timeout or a 500. The whole Path A
    /// ladder is measured from this column, so advancing it on a failed attempt would silently reset a
    /// tolerance window that exists precisely to survive failed attempts.
    /// </remarks>
    public void RecordContact(DateTimeOffset at)
    {
        if (LastContactAt is null || at > LastContactAt)
        {
            LastContactAt = at;
        }
    }

    /// <summary>Marks the binding as awaiting a vendor decision on a rebind.</summary>
    public void MarkRebindPending() => State = ActivationState.RebindPending;

    /// <summary>Releases the binding, freeing the licence for another machine.</summary>
    public void Deactivate() => State = ActivationState.Deactivated;
}
