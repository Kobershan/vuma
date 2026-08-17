using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A batch of outbound demand to fulfil from one location, moving through allocation, picking,
/// packing and shipping. Owns its <see cref="PickTask"/> lines by reference, not by collection — the
/// same header/line relationship Stage 08's <c>StocktakeSession</c>/<c>StocktakeLine</c> use.
/// </summary>
/// <remarks>
/// Order Management (Stage 14) does not exist yet, so a wave's demand is supplied directly by its
/// caller rather than read off a sales order — see the stage document's "what this stage does not own".
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class PickWave : Entity
{
    private PickWave(Guid tenantId, Guid? storeId, Guid locationId)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        Status = PickWaveStatus.Open;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PickWave()
    {
    }

    /// <summary>The location this wave picks from.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>Where this wave stands.</summary>
    public PickWaveStatus Status { get; private set; }

    /// <summary>When the wave was released for picking, or <c>null</c> while still open.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>When every line was picked or short-picked, or <c>null</c> if not yet.</summary>
    public DateTimeOffset? PickedAt { get; private set; }

    /// <summary>When the wave was packed, or <c>null</c> if not yet.</summary>
    public DateTimeOffset? PackedAt { get; private set; }

    /// <summary>When the wave shipped, or <c>null</c> if not yet.</summary>
    public DateTimeOffset? ShippedAt { get; private set; }

    /// <summary>Opens a new wave at a location.</summary>
    public static PickWave Open(Guid tenantId, Guid? storeId, Guid locationId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A pick wave must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A pick wave must name a location.", nameof(locationId));
        }

        return new PickWave(tenantId, storeId, locationId);
    }

    /// <summary>Releases the wave — its lines are allocated and picking may begin.</summary>
    public void Release(DateTimeOffset at)
    {
        EnsureStatus(PickWaveStatus.Open);
        Status = PickWaveStatus.Released;
        ReleasedAt = at;
    }

    /// <summary>Marks every line picked or short-picked.</summary>
    public void MarkPicked(DateTimeOffset at)
    {
        EnsureStatus(PickWaveStatus.Released);
        Status = PickWaveStatus.Picked;
        PickedAt = at;
    }

    /// <summary>Marks the wave packed.</summary>
    public void MarkPacked(DateTimeOffset at)
    {
        EnsureStatus(PickWaveStatus.Picked);
        Status = PickWaveStatus.Packed;
        PackedAt = at;
    }

    /// <summary>Marks the wave shipped — stock has left the location.</summary>
    public void MarkShipped(DateTimeOffset at)
    {
        EnsureStatus(PickWaveStatus.Packed);
        Status = PickWaveStatus.Shipped;
        ShippedAt = at;
    }

    /// <summary>Abandons the wave before it ships.</summary>
    public void Cancel()
    {
        if (Status is PickWaveStatus.Shipped or PickWaveStatus.Cancelled)
        {
            throw WarehouseRuleException.PickWaveWrongStatus(PickWaveStatus.Open, Status);
        }

        Status = PickWaveStatus.Cancelled;
    }

    private void EnsureStatus(PickWaveStatus expected)
    {
        if (Status != expected)
        {
            throw WarehouseRuleException.PickWaveWrongStatus(expected, Status);
        }
    }
}
