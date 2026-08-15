using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// A payment received from a customer, allocated against one or more <see cref="ArInvoice"/>s.
/// </summary>
/// <remarks>
/// Created once, fully formed with its allocations, and never amended — unlike an invoice, a receipt
/// has no draft phase, so it is <see cref="IImmutableRecord"/> like a <see cref="Journal"/>. A
/// misallocated receipt is corrected by reversing it and receipting again, not by editing it.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ArReceipt : Entity, IImmutableRecord
{
    private readonly List<ArReceiptAllocation> _allocations = [];

    private ArReceipt(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ArReceipt()
    {
    }

    /// <summary>The customer who paid.</summary>
    public PartnerId PartnerId { get; private set; }

    /// <summary>The per-tenant sequential receipt number.</summary>
    public string ReceiptNumber { get; private set; } = string.Empty;

    /// <summary>When the receipt was recorded, UTC.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The bank account the funds landed in, if known.</summary>
    public Guid? BankAccountId { get; private set; }

    /// <summary>The total amount received.</summary>
    public Money Amount { get; private set; }

    /// <summary>The GL journal this receipt posted.</summary>
    public Guid JournalId { get; private set; }

    /// <summary>How the amount was allocated across invoices.</summary>
    public IReadOnlyList<ArReceiptAllocation> Allocations => _allocations;

    /// <summary>
    /// Records a receipt and its allocations. The sum of the allocations must equal the amount
    /// received — a receipt with money left unallocated is a receipt that has to be allocated
    /// separately, which this type does not support; allocate all of it or reduce the amount.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the receipt was recorded at.</param>
    /// <param name="partnerId">The customer who paid.</param>
    /// <param name="receiptNumber">The per-tenant sequential receipt number.</param>
    /// <param name="receivedAt">When the receipt was recorded, UTC.</param>
    /// <param name="amount">The total amount received.</param>
    /// <param name="journalId">The GL journal this receipt posted.</param>
    /// <param name="allocations">Which invoices the amount is allocated to, and how much each.</param>
    /// <param name="bankAccountId">The bank account the funds landed in, if known.</param>
    /// <exception cref="ArgumentException">The allocations do not sum to the amount received.</exception>
    public static ArReceipt Record(
        Guid tenantId,
        Guid? storeId,
        PartnerId partnerId,
        string receiptNumber,
        DateTimeOffset receivedAt,
        Money amount,
        Guid journalId,
        IReadOnlyList<(Guid ArInvoiceId, Money Amount)> allocations,
        Guid? bankAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptNumber);
        ArgumentNullException.ThrowIfNull(allocations);

        Money allocated = allocations.Aggregate(Money.Zero(amount.Currency), (sum, a) => sum + a.Amount);

        if (allocated != amount)
        {
            throw new ArgumentException(
                $"Allocations total {allocated} but the receipt is for {amount}. Allocate the full amount.",
                nameof(allocations));
        }

        ArReceipt receipt = new(tenantId, storeId)
        {
            PartnerId = partnerId,
            ReceiptNumber = receiptNumber.Trim(),
            ReceivedAt = receivedAt,
            Amount = amount,
            JournalId = journalId,
            BankAccountId = bankAccountId,
        };

        foreach ((Guid arInvoiceId, Money allocatedAmount) in allocations)
        {
            receipt._allocations.Add(ArReceiptAllocation.Create(
                tenantId, receipt.Id, arInvoiceId, allocatedAmount));
        }

        return receipt;
    }
}
