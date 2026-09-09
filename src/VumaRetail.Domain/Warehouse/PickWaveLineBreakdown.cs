using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// One order's share of a grouped wave line. Carries the per-order breakdown so consolidation
/// can split it back without a second query (Stage 13b).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class PickWaveLineBreakdown : Entity
{
    private PickWaveLineBreakdown(
        Guid tenantId,
        Guid? storeId,
        Guid pickWaveLineId,
        Guid orderId,
        Guid orderLineId,
        decimal quantity)
        : base(tenantId, storeId)
    {
        PickWaveLineId = pickWaveLineId;
        OrderId = orderId;
        OrderLineId = orderLineId;
        Quantity = quantity;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PickWaveLineBreakdown()
    {
    }

    /// <summary>The grouped wave line this belongs to.</summary>
    public Guid PickWaveLineId { get; private set; }

    /// <summary>The order that contributed this quantity.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The line within that order.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The quantity this order contributes — sums exactly to the grouped quantity.</summary>
    public decimal Quantity { get; private set; }
}
