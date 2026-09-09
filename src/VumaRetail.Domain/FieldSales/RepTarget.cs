using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// A rep's target for one company and one calendar month. Versioned: changing a target never
/// rewrites what a closed month was measured against (FIELD_SALES.md §5).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class RepTarget : Entity
{
    private RepTarget(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RepTarget()
    {
    }

    /// <summary>The rep.</summary>
    public Guid RepId { get; private set; }

    /// <summary>First day of the measured month.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Target net sales for the month.</summary>
    public Money TargetNet { get; private set; }

    /// <summary>Monotonic version within (rep, company, period). Starts at 1.</summary>
    public int Version { get; private set; }

    /// <summary>Why this version exists.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>False once superseded. History keeps pointing at every version.</summary>
    public bool IsCurrent { get; private set; } = true;

    /// <summary>When this version was set, UTC.</summary>
    public DateTimeOffset SetAt { get; private set; }

    /// <summary>Sets the first version of a target.</summary>
    public static RepTarget Set(
        Guid tenantId,
        Guid? storeId,
        Guid repId,
        Guid companyId,
        DateOnly periodStart,
        Money targetNet,
        string reason,
        DateTimeOffset setAt)
    {
        if (tenantId == Guid.Empty || repId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("A target names its tenant, rep and company.");
        }

        if (periodStart.Day != 1)
        {
            throw new ArgumentException("A target period starts on the first of a month.", nameof(periodStart));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new RepTarget(tenantId, storeId)
        {
            RepId = repId,
            CompanyId = companyId,
            PeriodStart = periodStart,
            TargetNet = targetNet,
            Version = 1,
            Reason = reason.Trim(),
            SetAt = setAt,
        };
    }

    /// <summary>Supersedes with a new version. This row is frozen, never edited.</summary>
    public RepTarget Supersede(Money targetNet, string reason, DateTimeOffset setAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IsCurrent = false;
        return new RepTarget(TenantId, StoreId)
        {
            RepId = RepId,
            CompanyId = CompanyId,
            PeriodStart = PeriodStart,
            TargetNet = targetNet,
            Version = Version + 1,
            Reason = reason.Trim(),
            SetAt = setAt,
        };
    }
}
