using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// One payment against a lay-by agreement. Append-only: there is no update or delete path, so the
/// paid-to-date total and the receipt trail can never disagree. A mistaken payment is corrected by
/// a reversing row, never an edit.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class LayByInstalment : Entity
{
    private LayByInstalment(
        Guid tenantId,
        Guid? storeId,
        Guid agreementId,
        int sequence,
        Money amount,
        string receiptReference,
        DateTimeOffset paidAt,
        string channel,
        bool takenOffline)
        : base(tenantId, storeId)
    {
        AgreementId = agreementId;
        Sequence = sequence;
        Amount = amount;
        ReceiptReference = receiptReference;
        PaidAt = paidAt;
        Channel = channel;
        TakenOffline = takenOffline;
    }

    private LayByInstalment()
    {
    }

    /// <summary>The agreement being paid off.</summary>
    public Guid AgreementId { get; private set; }

    /// <summary>One-based order within the agreement. Unique per agreement.</summary>
    public int Sequence { get; private set; }

    /// <summary>What was paid. Must be positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>The receipt handed to the customer. Unique per agreement.</summary>
    public string ReceiptReference { get; private set; } = string.Empty;

    /// <summary>When it was paid, UTC.</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>Where it was taken: till, EFT, debit order, storefront.</summary>
    public string Channel { get; private set; } = string.Empty;

    /// <summary>True when captured offline against the last-known balance.</summary>
    public bool TakenOffline { get; private set; }

    /// <summary>Records a payment row.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="agreementId">The agreement.</param>
    /// <param name="sequence">One-based order within the agreement.</param>
    /// <param name="amount">What was paid. Must be positive.</param>
    /// <param name="receiptReference">The receipt reference.</param>
    /// <param name="paidAt">When it was paid, UTC.</param>
    /// <param name="channel">Where it was taken.</param>
    /// <param name="takenOffline">Whether it was captured offline.</param>
    public static LayByInstalment Record(
        Guid tenantId,
        Guid? storeId,
        Guid agreementId,
        int sequence,
        Money amount,
        string receiptReference,
        DateTimeOffset paidAt,
        string channel,
        bool takenOffline = false)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An instalment must belong to a tenant.", nameof(tenantId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentException("The sequence must be positive.", nameof(sequence));
        }

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("An instalment must be positive.", nameof(amount));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(receiptReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        return new LayByInstalment(
            tenantId, storeId, agreementId, sequence, amount, receiptReference.Trim(),
            paidAt, channel.Trim(), takenOffline);
    }
}
