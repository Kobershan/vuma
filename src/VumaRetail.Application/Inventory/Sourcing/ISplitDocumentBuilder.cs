namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Orders;

/// <summary>
/// Builds split documents (one SalesOrder per supplying company) from a committed sourcing plan.
/// Reconciles line for line and cent for cent.
/// </summary>
public interface ISplitDocumentBuilder
{
    /// <summary>
    /// Build split orders from a committed plan.
    /// Each order carries GroupDocumentRef = source order number.
    /// Throws SplitReconciliationException if sums don't match.
    /// </summary>
    Task<IReadOnlyList<SalesOrder>> BuildSplitOrdersAsync(
        SourcingPlan plan,
        Guid groupDocumentRef,
        IReadOnlyList<SalesOrderLine> sourceOrderLines,
        CancellationToken ct);
}
