using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A physical count of one or more bins, ad hoc or scheduled — the bin-level mirror of Stage 08's
/// <c>StocktakeSession</c>.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class CycleCount : Entity
{
    private CycleCount(Guid tenantId, Guid? storeId, Guid locationId, Guid? zoneId, DateTimeOffset? scheduledAt)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ZoneId = zoneId;
        ScheduledAt = scheduledAt;
        Status = CycleCountStatus.Open;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private CycleCount()
    {
    }

    /// <summary>The location being counted.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The zone this count is narrowed to, or <c>null</c> for the whole location.</summary>
    public Guid? ZoneId { get; private set; }

    /// <summary>Where this count stands.</summary>
    public CycleCountStatus Status { get; private set; }

    /// <summary>When the count was scheduled for, or <c>null</c> for an ad-hoc count.</summary>
    public DateTimeOffset? ScheduledAt { get; private set; }

    /// <summary>When the count was finalized, or <c>null</c> while still open.</summary>
    public DateTimeOffset? FinalizedAt { get; private set; }

    /// <summary>True once <see cref="Finalize"/> has been called.</summary>
    public bool IsFinalized => Status == CycleCountStatus.Finalized;

    /// <summary>Opens a new cycle count.</summary>
    public static CycleCount Open(Guid tenantId, Guid? storeId, Guid locationId, Guid? zoneId, DateTimeOffset? scheduledAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A cycle count must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A cycle count must name a location.", nameof(locationId));
        }

        return new CycleCount(tenantId, storeId, locationId, zoneId, scheduledAt);
    }

    /// <summary>Refuses to proceed once the count is finalized.</summary>
    /// <exception cref="WarehouseRuleException">The count is already finalized.</exception>
    public void EnsureOpen()
    {
        if (IsFinalized)
        {
            throw WarehouseRuleException.CycleCountAlreadyFinalized();
        }
    }

    /// <summary>
    /// Closes the count. Called by the command handler after every line's variance has been posted.
    /// </summary>
    public void Finalize(DateTimeOffset at)
    {
        EnsureOpen();
        Status = CycleCountStatus.Finalized;
        FinalizedAt = at;
    }
}
