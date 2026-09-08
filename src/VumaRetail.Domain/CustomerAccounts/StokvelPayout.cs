using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A member's draw-down: goods, a hamper, cash or store credit. Requested against available,
/// approved through Stage 05's gate, settled exactly once — goods and hampers as a normal sale.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StokvelPayout : Entity
{
    private StokvelPayout(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        StokvelPayoutKind kind,
        Money amount,
        Guid? hamperBasketId,
        DateTimeOffset requestedAt)
        : base(tenantId, storeId)
    {
        GroupId = groupId;
        MemberId = memberId;
        Kind = kind;
        Amount = amount;
        HamperBasketId = hamperBasketId;
        Status = StokvelPayoutStatus.Requested;
        RequestedAt = requestedAt;
    }

    private StokvelPayout()
    {
    }

    /// <summary>The group.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>The member drawing down.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>What the balance turns into.</summary>
    public StokvelPayoutKind Kind { get; private set; }

    /// <summary>How much. Must be positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>The hamper basket, for hamper payouts. Null otherwise.</summary>
    public Guid? HamperBasketId { get; private set; }

    /// <summary>Where the payout stands.</summary>
    public StokvelPayoutStatus Status { get; private set; }

    /// <summary>When it was requested, UTC.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When it was approved, UTC. Null until then.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>When it settled, UTC. Null until then.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>The sale it settled as, for goods/hamper payouts. Null otherwise.</summary>
    public Guid? SaleId { get; private set; }

    /// <summary>Requests a payout. Availability is checked by the command, not here.</summary>
    public static StokvelPayout Request(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        StokvelPayoutKind kind,
        Money amount,
        Guid? hamperBasketId,
        DateTimeOffset requestedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A payout must belong to a tenant.", nameof(tenantId));
        }

        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A payout must belong to a group.", nameof(groupId));
        }

        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A payout must belong to a member.", nameof(memberId));
        }

        if (amount.Amount <= 0m)
        {
            throw new StokvelExceptions("STOKVEL_PAYOUT_MUST_BE_POSITIVE", "A payout must be positive.");
        }

        if (kind == StokvelPayoutKind.Hamper && !hamperBasketId.HasValue)
        {
            throw StokvelExceptions.BasketRequired();
        }

        return new StokvelPayout(
            tenantId, storeId, groupId, memberId, kind, amount, hamperBasketId, requestedAt);
    }

    /// <summary>Approves a requested payout. Approval itself is enforced by the command.</summary>
    public void Approve(DateTimeOffset now)
    {
        if (Status != StokvelPayoutStatus.Requested)
        {
            throw StokvelExceptions.UnexpectedPayoutStatus(Status);
        }

        Status = StokvelPayoutStatus.Approved;
        ApprovedAt = now;
    }

    /// <summary>Settles an approved payout exactly once.</summary>
    public void Settle(DateTimeOffset now, Guid? saleId = null)
    {
        if (Status != StokvelPayoutStatus.Approved)
        {
            throw StokvelExceptions.UnexpectedPayoutStatus(Status);
        }

        if (Kind is StokvelPayoutKind.Goods or StokvelPayoutKind.Hamper && !saleId.HasValue)
        {
            throw new StokvelExceptions(
                "STOKVEL_PAYOUT_NEEDS_SALE", "A goods or hamper payout settles as a sale; a sale id is required.");
        }

        Status = StokvelPayoutStatus.Settled;
        SettledAt = now;
        SaleId = saleId;
    }
}
