using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>One packing record for a <see cref="PickWave"/> — how many packages it became.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class PackTask : Entity
{
    private PackTask(Guid tenantId, Guid? storeId, Guid pickWaveId, int packageCount, string? note, DateTimeOffset packedAt)
        : base(tenantId, storeId)
    {
        PickWaveId = pickWaveId;
        PackageCount = packageCount;
        Note = note;
        PackedAt = packedAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PackTask()
    {
    }

    /// <summary>The wave this packing record belongs to.</summary>
    public Guid PickWaveId { get; private set; }

    /// <summary>How many physical packages the wave was packed into.</summary>
    public int PackageCount { get; private set; }

    /// <summary>An optional free-text note.</summary>
    public string? Note { get; private set; }

    /// <summary>When the wave was packed, UTC.</summary>
    public DateTimeOffset PackedAt { get; private set; }

    /// <summary>Records a wave's packing.</summary>
    public static PackTask Record(Guid tenantId, Guid? storeId, Guid pickWaveId, int packageCount, string? note, DateTimeOffset packedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A pack task must belong to a tenant.", nameof(tenantId));
        }

        if (pickWaveId == Guid.Empty)
        {
            throw new ArgumentException("A pack task must belong to a wave.", nameof(pickWaveId));
        }

        if (packageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packageCount), packageCount, "A wave must be packed into at least one package.");
        }

        return new PackTask(tenantId, storeId, pickWaveId, packageCount, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), packedAt);
    }
}
