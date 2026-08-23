using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Licensing;
using VumaRetail.Licensing.Enforcement;

namespace VumaRetail.Licensing.Entitlements;

/// <summary>
/// The single choke point every gate in the system goes through (<c>LICENSING.md</c> §6).
/// </summary>
/// <remarks>
/// <para>
/// Scoped, and it memoises. A request may ask three or four times — the read-only guard on the way in,
/// an endpoint's module gate, a handler's limit check — and every one of those answers has to be the
/// same answer. Re-reading the lease per question would also mean a database round trip per rejected
/// sale, which is precisely what putting the guard in pipeline slot 50 was meant to avoid.
/// </para>
/// <para>
/// The memo lasts one scope and no longer. A singleton cache would be faster and would also mean a
/// tenant who has just paid waits for a process restart to find out — and <c>LICENSING.md</c> §4
/// promises sixty seconds.
/// </para>
/// </remarks>
/// <param name="activations">This installation's binding.</param>
/// <param name="leases">The leases it has held.</param>
/// <param name="state">Emergency unlocks and the clock watermark.</param>
/// <param name="policy">The ladder.</param>
/// <param name="manifests">Every module's declared licence flag.</param>
/// <param name="clock">Wall-clock time.</param>
/// <param name="node">This node's identity, for the clock watermark.</param>
public sealed class EntitlementService(
    IActivationRepository activations,
    ILeaseRepository leases,
    ILicenceStateRepository state,
    IEnforcementPolicy policy,
    IEnumerable<IModuleManifest> manifests,
    IClock clock,
    INodeIdentity node) : IEntitlementService, IEnforcementStatusReader
{
    private EnforcementDecision? _decision;
    private IReadOnlySet<string>? _entitlements;
    private LicenceLimits? _limits;

    /// <inheritdoc />
    public async Task<bool> IsModuleEnabledAsync(string module, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        IModuleManifest? manifest = manifests
            .FirstOrDefault(entry => string.Equals(entry.Module, module, StringComparison.OrdinalIgnoreCase));

        // A module nobody declared is not enabled. Answering "yes" to an unknown name would make the
        // gate useless the first time somebody mistyped one; answering "no" makes the mistake visible
        // in the one place it is cheap to find.
        if (manifest is null)
        {
            return false;
        }

        // Identity, sync, backup and licensing are the platform. A licence that somehow omitted
        // identity would be a licence nobody could sign in to replace.
        if (manifest.IsCore)
        {
            return true;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);

        // No lease to consult means permit, not deny (§4.12) — the same asymmetry CheckLimitAsync
        // already gets right via LicenceLimits.Unlimited below. "The lease says you don't have this
        // module" and "there is no lease to ask" are different facts, and only the first is a real
        // denial; a fresh DR restore before rebind, a migration, or a corrupted state directory all
        // produce the second. RequireModule sits on GETs with no other gate behind it (rule 15's
        // read-only carve-out is a command-pipeline concern, ReadOnlyGuardBehaviour at order 50, and
        // never runs for a query), so failing closed here would 403 reads the enforcement ladder never
        // meant to restrict. Whether writes are actually blocked is ReadOnlyGuardBehaviour's question,
        // not this one's — RequireModule may consult only IEntitlementService (the doc comment on it
        // says so, and an architecture test holds it), so this stays deliberately blind to
        // EnforcementDecision.Level rather than duplicating that check.
        return _entitlements is null || _entitlements.Contains(manifest.LicenceFlag);
    }

    /// <inheritdoc />
    public async Task<LimitCheck> CheckLimitAsync(
        LimitKind kind,
        long proposedValue,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);

        long ceiling = _limits!.Ceiling(kind);

        return new LimitCheck(
            kind,
            ceiling,
            proposedValue,
            LicenceLimits.IsHard(kind),
            proposedValue > ceiling);
    }

    /// <inheritdoc />
    public async Task<EnforcementDecision> CurrentLevel(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);

        return _decision!;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_decision is not null)
        {
            return;
        }

        Activation? activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false);
        Lease? lease = await leases.FindCurrentAsync(cancellationToken).ConfigureAwait(false);

        // The effective instant, never earlier than the highest this install has ever seen. Setting
        // the clock back therefore extends nothing (LICENSING.md §7) — and it is read here rather
        // than written, because a read path must never write.
        ClockWatermark? watermark = await state
            .FindWatermarkAsync(node.NodeId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = watermark?.Effective(clock.UtcNow) ?? clock.UtcNow;

        EmergencyUnlock? unlock = await state
            .FindActiveUnlockAsync(now, cancellationToken)
            .ConfigureAwait(false);

        _decision = policy.Evaluate(new EnforcementInputs(
            now,
            activation is { State: not ActivationState.Deactivated },
            activation?.LastContactAt,
            lease,
            unlock?.ExpiresAt));

        // Null, not an empty set, when there is no lease — IsModuleEnabledAsync's null check is what
        // makes the fallback permit rather than deny (§4.12). An empty set here would be
        // indistinguishable from a real lease that genuinely grants nothing, which is a fact worth
        // denying on; "no lease at all" is not that fact.
        _entitlements = lease?.EntitlementSet();
        _limits = lease?.LimitSet() ?? LicenceLimits.Unlimited;
    }
}
