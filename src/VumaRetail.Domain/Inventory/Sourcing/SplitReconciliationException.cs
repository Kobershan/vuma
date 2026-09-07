namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Exception thrown when split document builder's reconciliation fails.
/// Lists the first mismatching line for debugging.
/// </summary>
public sealed class SplitReconciliationException : InventoryRuleException
{
    public SplitReconciliationException(Guid orderLineId, string reason)
        : base("SOURCING_SPLIT_RECONCILIATION_FAILED",
            $"Split document reconciliation failed for order line {orderLineId}: {reason}")
    {
        OrderLineId = orderLineId;
        Reason = reason;
    }

    public Guid OrderLineId { get; }
    public string Reason { get; }
}
