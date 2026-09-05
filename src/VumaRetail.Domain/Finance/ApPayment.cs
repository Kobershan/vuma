using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>A payment made to a supplier, allocated against one or more <see cref="ApInvoice"/>s.</summary>
/// <remarks>The AP mirror of <see cref="ArReceipt"/>; see its remarks for the immutability design.</remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ApPayment : Entity, IImmutableRecord
{
    private readonly List<ApPaymentAllocation> _allocations = [];

    private ApPayment(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApPayment()
    {
    }

    /// <summary>The supplier paid.</summary>
    public PartnerId PartnerId { get; private set; }

    /// <summary>The per-tenant sequential payment number.</summary>
    public string PaymentNumber { get; private set; } = string.Empty;

    /// <summary>When the payment was recorded, UTC.</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>The bank account the funds were paid from, if known.</summary>
    public Guid? BankAccountId { get; private set; }

    /// <summary>The total amount paid.</summary>
    public Money Amount { get; private set; }

    /// <summary>The GL journal this payment posted.</summary>
    public Guid JournalId { get; private set; }

    /// <summary>
    /// The group payment run this AP payment was dispatched from, or null for direct payments.
    /// Set when this payment is created as a saga leg from a group payment allocation (Stage 07c).
    /// </summary>
    public Guid? GroupDocumentId { get; private set; }

    /// <summary>
    /// The inter-company clearing intent that created this payment, or null for direct payments.
    /// Clearing lines carry the intent id so an unmatched leg is identifiable by document (ADR-105).
    /// </summary>
    public Guid? IntentId { get; private set; }

    /// <summary>How the amount was allocated across invoices.</summary>
    public IReadOnlyList<ApPaymentAllocation> Allocations => _allocations;

    /// <summary>Records a payment and its allocations. See <see cref="ArReceipt.Record"/> for the shape.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the payment was recorded at.</param>
    /// <param name="partnerId">The supplier paid.</param>
    /// <param name="paymentNumber">The per-tenant sequential payment number.</param>
    /// <param name="paidAt">When the payment was recorded, UTC.</param>
    /// <param name="amount">The total amount paid.</param>
    /// <param name="journalId">The GL journal this payment posted.</param>
    /// <param name="allocations">Which invoices the amount is allocated to, and how much each.</param>
    /// <param name="bankAccountId">The bank account the funds were paid from, if known.</param>
    /// <exception cref="ArgumentException">The allocations do not sum to the amount paid.</exception>
    public static ApPayment Record(
        Guid tenantId,
        Guid? storeId,
        PartnerId partnerId,
        string paymentNumber,
        DateTimeOffset paidAt,
        Money amount,
        Guid journalId,
        IReadOnlyList<(Guid ApInvoiceId, Money Amount)> allocations,
        Guid? bankAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentNumber);
        ArgumentNullException.ThrowIfNull(allocations);

        Money allocated = allocations.Aggregate(Money.Zero(amount.Currency), (sum, a) => sum + a.Amount);

        if (allocated != amount)
        {
            throw new ArgumentException(
                $"Allocations total {allocated} but the payment is for {amount}. Allocate the full amount.",
                nameof(allocations));
        }

        ApPayment payment = new(tenantId, storeId)
        {
            PartnerId = partnerId,
            PaymentNumber = paymentNumber.Trim(),
            PaidAt = paidAt,
            Amount = amount,
            JournalId = journalId,
            BankAccountId = bankAccountId,
        };

        foreach ((Guid apInvoiceId, Money allocatedAmount) in allocations)
        {
            payment._allocations.Add(ApPaymentAllocation.Create(
                tenantId, payment.Id, apInvoiceId, allocatedAmount));
        }

        return payment;
    }

    /// <summary>
    /// Records a payment dispatched from a group payment allocation (Stage 07c).
    /// Sets <see cref="GroupDocumentId"/> and <see cref="IntentId"/> for traceability.
    /// </summary>
    public static ApPayment RecordFromGroup(
        Guid tenantId,
        Guid? storeId,
        PartnerId partnerId,
        string paymentNumber,
        DateTimeOffset paidAt,
        Money amount,
        Guid journalId,
        IReadOnlyList<(Guid ApInvoiceId, Money Amount)> allocations,
        Guid groupDocumentId,
        Guid? intentId,
        Guid? bankAccountId = null)
    {
        ApPayment payment = Record(tenantId, storeId, partnerId, paymentNumber, paidAt, amount, journalId, allocations, bankAccountId);
        payment.GroupDocumentId = groupDocumentId;
        payment.IntentId = intentId;
        return payment;
    }
}
