using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A member of a stokvel group: their role, when they joined and left, and what they owe per
/// cycle. The balance is projected from that member's rows only — never stored.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StokvelMember : Entity
{
    private StokvelMember(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid partnerId,
        MemberRole role,
        DateTimeOffset joinedAt,
        Money contributionObligation)
        : base(tenantId, storeId)
    {
        GroupId = groupId;
        PartnerId = partnerId;
        Role = role;
        JoinedAt = joinedAt;
        ContributionObligation = contributionObligation;
    }

    private StokvelMember()
    {
    }

    /// <summary>The group.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>The customer partner. A bare id, never a cross-schema key.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>What this member may do.</summary>
    public MemberRole Role { get; private set; }

    /// <summary>When they joined, UTC.</summary>
    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>When they left, UTC. Null while active.</summary>
    public DateTimeOffset? LeftAt { get; private set; }

    /// <summary>What they owe per cycle.</summary>
    public Money ContributionObligation { get; private set; }

    /// <summary>True while the member may transact.</summary>
    public bool IsActive => LeftAt is null;

    /// <summary>Joins a member to a group.</summary>
    public static StokvelMember Join(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid partnerId,
        MemberRole role,
        Money obligation,
        DateTimeOffset joinedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A member must belong to a tenant.", nameof(tenantId));
        }

        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A member must belong to a group.", nameof(groupId));
        }

        if (partnerId == Guid.Empty)
        {
            throw new ArgumentException("A member must be a customer partner.", nameof(partnerId));
        }

        if (obligation.Amount < 0m)
        {
            throw new StokvelExceptions("STOKVEL_OBLIGATION_NEGATIVE", "The contribution obligation may not be negative.");
        }

        return new StokvelMember(tenantId, storeId, groupId, partnerId, role, joinedAt, obligation);
    }

    /// <summary>Marks the member as left. Rows stay queryable; vesting freezes at <paramref name="now"/>.</summary>
    public void Leave(DateTimeOffset now)
    {
        if (LeftAt is not null)
        {
            throw StokvelExceptions.MemberLeft();
        }

        LeftAt = now;
    }

    /// <summary>Whether the member was active at an instant (vesting window).</summary>
    public bool IsActiveAt(DateTimeOffset instant)
        => instant >= JoinedAt && (LeftAt is null || instant < LeftAt.Value);

    /// <summary>
    /// Projects this member's available balance: paid in plus vested benefits less settled payouts.
    /// </summary>
    public Money Available(
        IEnumerable<StokvelContribution> contributions,
        IEnumerable<StokvelPayout> payouts,
        IEnumerable<StokvelBenefitAllocation> benefits,
        string currency)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(payouts);
        ArgumentNullException.ThrowIfNull(benefits);

        var mine = contributions.Where(c => c.MemberId == Id).ToList();
        var myBenefits = benefits.Where(b => b.MemberId == Id).ToList();
        var mySettled = payouts.Where(p => p.MemberId == Id && p.Status == StokvelPayoutStatus.Settled).ToList();

        Money total = Money.Zero(currency);
        foreach (var c in mine)
        {
            total += c.Amount;
        }

        foreach (var b in myBenefits)
        {
            total += b.Amount;
        }

        foreach (var p in mySettled)
        {
            total -= p.Amount;
        }

        return total;
    }
}
