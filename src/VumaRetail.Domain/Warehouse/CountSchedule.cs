using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A scheduled count run targeting slow movers, zone, class, value band or supplier.
/// Generates Stage 13 <see cref="CycleCount"/>s — no new counting model (ADR-115).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class CountSchedule : Entity
{
    private CountSchedule(
        Guid tenantId,
        Guid? storeId,
        string name,
        CountCadence cadence,
        string scope,
        int slowMoverDays,
        int randomSampleSize,
        DateTimeOffset nextRunAt)
        : base(tenantId, storeId)
    {
        Name = name;
        Cadence = cadence;
        Scope = scope;
        SlowMoverDays = slowMoverDays;
        RandomSampleSize = randomSampleSize;
        NextRunAt = nextRunAt;
        IsActive = true;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private CountSchedule()
    {
    }

    /// <summary>The schedule's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>How often the schedule runs.</summary>
    public CountCadence Cadence { get; private set; }

    /// <summary>What the schedule targets: zone, class, value band, supplier.</summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>Items with no movement in this many days are slow movers.</summary>
    public int SlowMoverDays { get; private set; }

    /// <summary>Plus this many random items, so the schedule cannot be gamed.</summary>
    public int RandomSampleSize { get; private set; }

    /// <summary>When the schedule next runs.</summary>
    public DateTimeOffset NextRunAt { get; private set; }

    /// <summary>True while the schedule is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates a new count schedule.</summary>
    public static CountSchedule Create(
        Guid tenantId,
        Guid? storeId,
        string name,
        CountCadence cadence,
        string scope,
        int slowMoverDays,
        int randomSampleSize,
        DateTimeOffset nextRunAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A count schedule must belong to a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A count schedule has a name.", nameof(name));
        }

        if (slowMoverDays < 0)
        {
            throw new ArgumentException("Slow-mover days cannot be negative.", nameof(slowMoverDays));
        }

        if (randomSampleSize < 0)
        {
            throw new ArgumentException("Random sample size cannot be negative.", nameof(randomSampleSize));
        }

        return new CountSchedule(tenantId, storeId, name, cadence, scope, slowMoverDays, randomSampleSize, nextRunAt);
    }

    /// <summary>Deactivates the schedule.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Advances the next run time.</summary>
    public void Advance(DateTimeOffset nextRunAt) => NextRunAt = nextRunAt;
}

/// <summary>How often a count schedule runs.</summary>
public enum CountCadence
{
    /// <summary>Run every day.</summary>
    Daily = 0,

    /// <summary>Run every week.</summary>
    Weekly = 1,

    /// <summary>Run every month.</summary>
    Monthly = 2,

    /// <summary>Run on a custom schedule.</summary>
    Custom = 3,
}
