using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>How much of an <see cref="ApPayment"/> was allocated to one <see cref="ApInvoice"/>.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ApPaymentAllocation : Entity, IImmutableRecord
{
    private ApPaymentAllocation(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApPaymentAllocation()
    {
    }

    /// <summary>The payment this allocation belongs to.</summary>
    public Guid ApPaymentId { get; private set; }

    /// <summary>The invoice the amount was allocated to.</summary>
    public Guid ApInvoiceId { get; private set; }

    /// <summary>How much of the payment was allocated to this invoice.</summary>
    public Money Amount { get; private set; }

    /// <summary>Builds one allocation. Internal — use <see cref="ApPayment.Record"/>.</summary>
    internal static ApPaymentAllocation Create(Guid tenantId, Guid apPaymentId, Guid apInvoiceId, Money amount)
        => new(tenantId)
        {
            ApPaymentId = apPaymentId,
            ApInvoiceId = apInvoiceId,
            Amount = amount,
        };
}
