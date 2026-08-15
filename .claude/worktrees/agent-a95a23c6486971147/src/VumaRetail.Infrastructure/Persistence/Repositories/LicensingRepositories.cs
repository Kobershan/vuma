using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementations of the Stage 04b licensing ports.
/// </summary>
/// <remarks>
/// None of these commit — the pipeline owns the transaction (ADR-044) — and none of them filter by
/// tenant or soft delete, because both are global query filters applied to every entity.
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="node">This node's identity, so the activation found is this machine's.</param>
public sealed class ActivationRepository(VumaRetailDbContext context, INodeIdentity node) : IActivationRepository
{
    /// <inheritdoc />
    public Task<Activation?> FindCurrentAsync(CancellationToken cancellationToken = default)
        => context.Activations
            .Where(activation => activation.NodeId == node.NodeId)
            .OrderByDescending(activation => activation.ActivatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Activation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        context.Activations.Add(activation);
    }
}

/// <summary>The signed licences this node has been issued.</summary>
/// <param name="context">The database context.</param>
public sealed class LicenceRepository(VumaRetailDbContext context) : ILicenceRepository
{
    /// <inheritdoc />
    public Task<Licence?> FindCurrentAsync(CancellationToken cancellationToken = default)
        // By issuance counter, not by issued_at. The counter is the vendor's own monotonic sequence;
        // two licences issued in the same second — a plan change applied immediately after an upgrade
        // — order correctly by one and arbitrarily by the other.
        => context.Licences
            .OrderByDescending(licence => licence.IssuanceCounter)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> HighestIssuanceCounterAsync(CancellationToken cancellationToken = default)
        => await context.Licences
            .Select(licence => (long?)licence.IssuanceCounter)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

    /// <inheritdoc />
    public void Add(Licence licence)
    {
        ArgumentNullException.ThrowIfNull(licence);

        context.Licences.Add(licence);
    }
}

/// <summary>The leases this node has held.</summary>
/// <param name="context">The database context.</param>
public sealed class LeaseRepository(VumaRetailDbContext context) : ILeaseRepository
{
    /// <inheritdoc />
    public Task<Lease?> FindCurrentAsync(CancellationToken cancellationToken = default)
        => context.Leases
            .OrderByDescending(lease => lease.IssuedAt)
            .ThenByDescending(lease => lease.IssuanceCounter)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        context.Leases.Add(lease);
    }
}

/// <summary>Redeemed emergency codes, the clock watermark and the tamper flags.</summary>
/// <param name="context">The database context.</param>
public sealed class LicenceStateRepository(VumaRetailDbContext context) : ILicenceStateRepository
{
    /// <inheritdoc />
    public Task<EmergencyUnlock?> FindActiveUnlockAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => context.EmergencyUnlocks
            .Where(unlock => unlock.ExpiresAt > at)
            .OrderByDescending(unlock => unlock.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasRedeemedAsync(Guid codeReference, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters, deliberately and narrowly. A soft-deleted redemption must still block a
        // replay — otherwise deleting the row is how a code becomes reusable, which is exactly what
        // single-use means it must not be. The tenant filter is *not* bypassed: this stays inside the
        // filter, so it can only ever see this tenant's redemptions.
        => context.EmergencyUnlocks
            .IgnoreQueryFilters()
            .Where(unlock => unlock.TenantId == context.CurrentTenantId)
            .AnyAsync(unlock => unlock.CodeReference == codeReference, cancellationToken);

    /// <inheritdoc />
    public void Add(EmergencyUnlock unlock)
    {
        ArgumentNullException.ThrowIfNull(unlock);

        context.EmergencyUnlocks.Add(unlock);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmergencyUnlock>> ListUnreportedUnlocksAsync(
        CancellationToken cancellationToken = default)
        => await context.EmergencyUnlocks
            .Where(unlock => unlock.ReportedAt == null)
            .OrderBy(unlock => unlock.RedeemedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<ClockWatermark?> FindWatermarkAsync(string nodeId, CancellationToken cancellationToken = default)
        => context.ClockWatermarks.FirstOrDefaultAsync(
            watermark => watermark.NodeId == nodeId,
            cancellationToken);

    /// <inheritdoc />
    public void Add(ClockWatermark watermark)
    {
        ArgumentNullException.ThrowIfNull(watermark);

        context.ClockWatermarks.Add(watermark);
    }

    /// <inheritdoc />
    public void Add(TamperFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        context.TamperFlags.Add(flag);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TamperFlag>> ListUnreportedFlagsAsync(
        CancellationToken cancellationToken = default)
        => await context.TamperFlags
            .Where(flag => flag.ReportedAt == null)
            .OrderBy(flag => flag.DetectedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>The daily metering rollups.</summary>
/// <param name="context">The database context.</param>
public sealed class MeteringRepository(VumaRetailDbContext context) : IMeteringRepository
{
    /// <inheritdoc />
    public Task<MeteringRecord?> FindAsync(
        string nodeId,
        DateOnly period,
        CancellationToken cancellationToken = default)
        => context.MeteringRecords.FirstOrDefaultAsync(
            record => record.NodeId == nodeId && record.Period == period,
            cancellationToken);

    /// <inheritdoc />
    public void Add(MeteringRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        context.MeteringRecords.Add(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeteringRecord>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => await context.MeteringRecords
            .Where(record => record.State == MeteringDeliveryState.Pending)
            .OrderBy(record => record.Period)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeteringRecord>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => await context.MeteringRecords
            .OrderByDescending(record => record.Period)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Vendor support-access grants.</summary>
/// <param name="context">The database context.</param>
public sealed class SupportGrantRepository(VumaRetailDbContext context) : ISupportGrantRepository
{
    /// <inheritdoc />
    public Task<SupportGrant?> FindAsync(Guid grantId, CancellationToken cancellationToken = default)
        => context.SupportGrants.FirstOrDefaultAsync(grant => grant.Id == grantId, cancellationToken);

    /// <inheritdoc />
    public Task<SupportGrant?> FindByReferenceAsync(
        Guid reference,
        CancellationToken cancellationToken = default)
        => context.SupportGrants.FirstOrDefaultAsync(
            grant => grant.GrantReference == reference,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportGrant>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => await context.SupportGrants
            .OrderByDescending(grant => grant.RequestedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(SupportGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        context.SupportGrants.Add(grant);
    }
}
