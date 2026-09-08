using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// One contribution to a stokvel group, receipted to the individual member. Append-only: there is
/// no update or delete path, so the member's balance and the receipt trail can never disagree.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StokvelContribution : Entity
{
    private StokvelContribution(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        Money amount,
        string receiptReference,
        DateTimeOffset paidAt,
        string channel,
        bool takenOffline)
        : base(tenantId, storeId)
    {
        GroupId = groupId;
        MemberId = memberId;
        Amount = amount;
        ReceiptReference = receiptReference;
        PaidAt = paidAt;
        Channel = channel;
        TakenOffline = takenOffline;
    }

    private StokvelContribution()
    {
    }

    /// <summary>The group.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>The member who paid.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>What was paid. Must be positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>The receipt handed to the member. Unique per member (offline replay idempotency).</summary>
    public string ReceiptReference { get; private set; } = string.Empty;

    /// <summary>When it was paid, UTC.</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>Where it was taken: till, EFT, debit order, storefront.</summary>
    public string Channel { get; private set; } = string.Empty;

    /// <summary>True when captured offline against the last-known balance.</summary>
    public bool TakenOffline { get; private set; }

    /// <summary>Records a contribution row.</summary>
    public static StokvelContribution Record(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        Guid memberId,
        Money amount,
        string receiptReference,
        DateTimeOffset paidAt,
        string channel,
        bool takenOffline = false)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A contribution must belong to a tenant.", nameof(tenantId));
        }

        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A contribution must belong to a group.", nameof(groupId));
        }

        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A contribution must belong to a member.", nameof(memberId));
        }

        if (amount.Amount <= 0m)
        {
            throw new StokvelExceptions("STOKVEL_CONTRIBUTION_MUST_BE_POSITIVE", "A contribution must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(receiptReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        return new StokvelContribution(
            tenantId, storeId, groupId, memberId, amount, receiptReference.Trim(),
            paidAt, channel.Trim(), takenOffline);
    }
}
