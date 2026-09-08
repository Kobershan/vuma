using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// One benefit allocation to a member: their share of a bonus pool, time-weighted. Append-only —
/// a misallocation is corrected by a new allocation, never an edit.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StokvelBenefitAllocation : Entity
{
    private StokvelBenefitAllocation(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        Money amount,
        string basis,
        DateTimeOffset allocatedAt)
        : base(tenantId, storeId)
    {
        GroupId = groupId;
        MemberId = memberId;
        Amount = amount;
        Basis = basis;
        AllocatedAt = allocatedAt;
    }

    private StokvelBenefitAllocation()
    {
    }

    /// <summary>The group.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>The member.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>The allocated share. May be zero; never negative.</summary>
    public Money Amount { get; private set; }

    /// <summary>How it was computed, e.g. <c>time-weighted 2026 cycle, weight 300000/650000</c>.</summary>
    public string Basis { get; private set; } = string.Empty;

    /// <summary>When it was allocated, UTC.</summary>
    public DateTimeOffset AllocatedAt { get; private set; }

    /// <summary>Writes one member's share.</summary>
    public static StokvelBenefitAllocation Allocate(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        Money amount,
        string basis,
        DateTimeOffset allocatedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A benefit must belong to a tenant.", nameof(tenantId));
        }

        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A benefit must belong to a group.", nameof(groupId));
        }

        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A benefit must belong to a member.", nameof(memberId));
        }

        if (amount.Amount < 0m)
        {
            throw new StokvelExceptions("STOKVEL_BENEFIT_NEGATIVE", "A benefit allocation may not be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(basis);

        return new StokvelBenefitAllocation(
            tenantId, storeId, groupId, memberId, amount, basis.Trim(), allocatedAt);
    }
}
