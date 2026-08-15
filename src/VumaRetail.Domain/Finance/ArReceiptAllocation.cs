using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>How much of an <see cref="ArReceipt"/> was allocated to one <see cref="ArInvoice"/>.</summary>
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

    /// <summary>The invoice the amount was allocated to.</summary>
    public Guid ArInvoiceId { get; private set; }

    /// <summary>How much of the receipt was allocated to this invoice.</summary>
    public Money Amount { get; private set; }

    /// <summary>Builds one allocation. Internal — use <see cref="ArReceipt.Record"/>.</summary>
    internal static ArReceiptAllocation Create(Guid tenantId, Guid arReceiptId, Guid arInvoiceId, Money amount)
        => new(tenantId)
        {
            ArReceiptId = arReceiptId,
            ArInvoiceId = arInvoiceId,
            Amount = amount,
        };
}
