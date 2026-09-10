using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>How much of an <see cref="ArReceipt"/> was allocated to one <see cref="ArInvoice"/>.</summary>
/// <remarks>
/// A null <see cref="ArInvoiceId"/> is an on-account (unapplied) slice: money received from the
/// customer that is not yet applied to any invoice (Stage 07c group legs, and any future
/// on-account receipting). The receipt's full-allocation invariant still holds — the slices
/// sum to the receipt amount — but an on-account slice settles no invoice until it is applied.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ArReceiptAllocation : Entity, IImmutableRecord
{
    private ArReceiptAllocation(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ArReceiptAllocation()
    {
    }

    /// <summary>The receipt this allocation belongs to.</summary>
    public Guid ArReceiptId { get; private set; }

    /// <summary>The invoice the amount was allocated to, or null for an on-account slice.</summary>
    public Guid? ArInvoiceId { get; private set; }

    /// <summary>How much of the receipt was allocated to this invoice.</summary>
    public Money Amount { get; private set; }

    /// <summary>Builds one allocation. Internal — use <see cref="ArReceipt.Record"/>.</summary>
    internal static ArReceiptAllocation Create(Guid tenantId, Guid arReceiptId, Guid? arInvoiceId, Money amount)
        => new(tenantId)
        {
            ArReceiptId = arReceiptId,
            ArInvoiceId = arInvoiceId,
            Amount = amount,
        };
}
