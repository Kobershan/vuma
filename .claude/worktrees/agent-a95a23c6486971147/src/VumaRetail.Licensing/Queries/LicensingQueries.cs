using System.Globalization;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Contracts.Licensing;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.Licensing.Queries;

/// <summary>
/// The licence screen: plan, entitlements, limits against usage, expiry, level and how to fix it.
/// </summary>
/// <remarks>
/// A query, and therefore untouched by the read-only guard. That is not an oversight to be tightened
/// later — a tenant who is read-only is exactly the tenant who needs this screen, and a licence screen
/// that stopped working during a lapse would be a licence screen nobody could use to end one.
/// </remarks>
public sealed record GetLicenceStatusQuery : IQuery<LicenceStatusResponse>;

/// <summary>Assembles the licence screen.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="licences">The licences it has been issued.</param>
/// <param name="leases">The leases it has held.</param>
/// <param name="state">Emergency unlocks and the clock watermark.</param>
/// <param name="entitlements">
/// Where the tenant sits on the ladder — <see cref="IEnforcementStatusReader"/>, not
/// <see cref="IEntitlementService"/>, because this handler's whole job is to report the level rather
/// than gate on it (ADR-054).
/// </param>
/// <param name="manifests">Every declared module.</param>
/// <param name="counters">Current usage, for the limit rows.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GetLicenceStatusQueryHandler(
    IActivationRepository activations,
    ILicenceRepository licences,
    ILeaseRepository leases,
    ILicenceStateRepository state,
    IEnforcementStatusReader entitlements,
    IEnumerable<IModuleManifest> manifests,
    IUsageCounterSource counters,
    INodeIdentity node,
    IClock clock) : IQueryHandler<GetLicenceStatusQuery, LicenceStatusResponse>
{
    /// <inheritdoc />
    public async Task<LicenceStatusResponse> HandleAsync(
        GetLicenceStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        Activation? activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false);
        Licence? licence = await licences.FindCurrentAsync(cancellationToken).ConfigureAwait(false);
        Lease? lease = await leases.FindCurrentAsync(cancellationToken).ConfigureAwait(false);

        ClockWatermark? watermark = await state
            .FindWatermarkAsync(node.NodeId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = watermark?.Effective(clock.UtcNow) ?? clock.UtcNow;

        EmergencyUnlock? unlock = await state
            .FindActiveUnlockAsync(now, cancellationToken)
            .ConfigureAwait(false);

        EnforcementDecision decision = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

        ConfigurationCounts usage = await counters
            .CountConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<string> enabled = lease?.EntitlementSet()
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LicenceLimits limits = lease?.LimitSet() ?? LicenceLimits.Unlimited;

        List<ModuleEntitlementResponse> modules =
        [
            .. manifests
                .OrderBy(manifest => manifest.Module, StringComparer.Ordinal)
                .Select(manifest => new ModuleEntitlementResponse(
                    manifest.Module,
                    manifest.Description,
                    manifest.IsCore || enabled.Contains(manifest.LicenceFlag),
                    manifest.IsCore)),
        ];

        return new LicenceStatusResponse(
            activation is { State: not ActivationState.Deactivated },
            licence?.PlanCode ?? string.Empty,
            decision.Level.ToString(),
            decision.Reason.ToString(),
            decision.Notice.ToString(),
            decision.Messages ?? [],
            licence?.ExpiresAt,
            lease?.ExpiresAt,
            activation?.LastContactAt,
            decision.RestrictedSince,
            decision.NextEscalationAt,
            unlock?.ExpiresAt,
            decision.AmountDue?.Amount,
            decision.AmountDue?.Currency,
            decision.PayUrl,
            decision.UpdatePaymentMethodUrl,
            decision.SupportPhone,
            modules,
            [
                Limit(LimitKind.Stores, limits, usage.Stores),
                Limit(LimitKind.Terminals, limits, usage.Terminals),
                Limit(LimitKind.NamedUsers, limits, usage.NamedUsers),
            ]);
    }

    private static LimitUsageResponse Limit(LimitKind kind, LicenceLimits limits, long used)
    {
        long ceiling = limits.Ceiling(kind);

        // A ceiling at the type's maximum is "unlimited", and showing a customer 2 147 483 647 stores
        // is worse than showing them nothing.
        return new LimitUsageResponse(
            kind.ToString(),
            ceiling is int.MaxValue or long.MaxValue ? null : ceiling,
            used,
            LicenceLimits.IsHard(kind));
    }
}

/// <summary>
/// The sub-lease a till runs on (<c>LICENSING.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// The store server holds the licence and the terminals take sub-leases over the LAN, so a till never
/// needs internet of its own. The sub-lease is not separately signed: it is served on a connection the
/// terminal has already authenticated with its client certificate (Stage 02), to a terminal this store
/// server enrolled, and it expires no later than the store server's own lease. A signature would add
/// nothing a till could act on — it has to reach the store server to sell in the first place.
/// </para>
/// <para>
/// A query, so a till whose store is read-only can still ask what it may do and show the right screen.
/// </para>
/// </remarks>
/// <param name="TerminalId">The terminal asking.</param>
public sealed record GetSubLeaseQuery(Guid TerminalId) : IQuery<SubLeaseResponse>;

/// <summary>Issues a sub-lease from the store server's own lease.</summary>
/// <param name="leases">The leases this store server has held.</param>
/// <param name="entitlements">Where the tenant sits on the ladder (ADR-054).</param>
/// <param name="options">How long a lease lives.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GetSubLeaseQueryHandler(
    ILeaseRepository leases,
    IEnforcementStatusReader entitlements,
    LicensingOptions options,
    IClock clock) : IQueryHandler<GetSubLeaseQuery, SubLeaseResponse>
{
    /// <summary>How long a sub-lease lives before a till must ask again.</summary>
    /// <remarks>
    /// An hour, against the store server's 72. A till is on the same LAN as the thing it is asking, so
    /// re-asking is free — and an hour means a store that goes read-only at 09:00 has every till
    /// agreeing with it by 10:00 at the latest, even if one was mid-session and never asked.
    /// </remarks>
    public static readonly TimeSpan SubLeaseLifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<SubLeaseResponse> HandleAsync(
        GetSubLeaseQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        EnforcementDecision decision = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);
        Lease? lease = await leases.FindCurrentAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset expires = now + SubLeaseLifetime;

        // Capped by the parent. A till must never hold authority the store server has itself run out
        // of — that is how a terminal keeps trading after its store has stopped.
        DateTimeOffset parentExpiry = lease?.ExpiresAt ?? now + options.LeaseLifetime;

        return new SubLeaseResponse(
            query.TerminalId,
            decision.Level.ToString(),
            [.. (lease?.EntitlementSet() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)],
            now,
            expires < parentExpiry ? expires : parentExpiry);
    }
}

/// <summary>Vendor support-access requests and grants, newest first.</summary>
/// <param name="Limit">How many to take.</param>
public sealed record ListSupportGrantsQuery(int Limit = 50) : IQuery<IReadOnlyList<SupportGrantResponse>>;

/// <summary>Reads the tenant's own record of who asked to look at their data, and when.</summary>
/// <param name="grants">Vendor support-access grants.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ListSupportGrantsQueryHandler(ISupportGrantRepository grants, IClock clock)
    : IQueryHandler<ListSupportGrantsQuery, IReadOnlyList<SupportGrantResponse>>
{
    /// <summary>The most that will be returned in one page.</summary>
    public const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportGrantResponse>> HandleAsync(
        ListSupportGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<SupportGrant> found = await grants
            .ListAsync(Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. found.Select(grant => new SupportGrantResponse(
                grant.Id,
                grant.RequestedBy,
                grant.Reason,
                grant.Scope,
                grant.State.ToString(),
                grant.RequestedAt,
                grant.DecidedBy,
                grant.DecidedAt,
                grant.ExpiresAt,
                grant.IsActiveAt(now))),
        ];
    }
}

/// <summary>The daily metering rollups, newest first.</summary>
/// <param name="Limit">How many to take.</param>
public sealed record ListMeteringQuery(int Limit = 30) : IQuery<IReadOnlyList<MeteringRecordResponse>>;

/// <summary>
/// Shows the tenant exactly what has been reported about them.
/// </summary>
/// <remarks>
/// The payload is returned verbatim rather than summarised. <c>LICENSING.md</c> §9 makes a promise
/// about what leaves a tenant's premises; a tenant who can read the actual document is a tenant who
/// can check it, and a promise that can be checked is worth more than one that has to be believed.
/// </remarks>
/// <param name="metering">The queued and delivered rollups.</param>
public sealed class ListMeteringQueryHandler(IMeteringRepository metering)
    : IQueryHandler<ListMeteringQuery, IReadOnlyList<MeteringRecordResponse>>
{
    /// <summary>The most that will be returned in one page.</summary>
    public const int MaxLimit = 400;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeteringRecordResponse>> HandleAsync(
        ListMeteringQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<MeteringRecord> found = await metering
            .ListRecentAsync(Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. found.Select(record => new MeteringRecordResponse(
                record.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                record.NodeId,
                record.State.ToString(),
                record.SentAt,
                record.Payload)),
        ];
    }
}
