using VumaRetail.Domain.Orders;

namespace VumaRetail.Application.Orders;

/// <summary>
/// The one place <c>RefreshOrderFulfilmentCommand</c> and <c>CompleteOrderCommand</c> share: reconcile
/// every active line's status from what Stage 13 currently reports (business rule 4).
/// </summary>
internal static class OrderFulfilmentRefresh
{
    /// <summary>
    /// Reads <see cref="IOrderFulfilmentReader.GetLineFulfilmentAsync"/> for every line that has not
    /// been cancelled and records the outcome, then recomputes the order's own status.
    /// </summary>
    /// <param name="order">The order, with its lines loaded.</param>
    /// <param name="reader">Stage 14's one read seam into Stage 13's state.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    internal static async Task ApplyAsync(
        SalesOrder order, IOrderFulfilmentReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(reader);

        foreach (SalesOrderLine line in order.Lines)
        {
            if (line.LineStatus == SalesOrderLineStatus.Cancelled)
            {
                continue;
            }

            OrderLineFulfilmentSnapshot snapshot = await reader
                .GetLineFulfilmentAsync(line.Id, line.RequestedQuantity.UnitOfMeasure, cancellationToken)
                .ConfigureAwait(false);

            line.RecordFulfilmentOutcome(snapshot.AllocatedQuantity, snapshot.FulfilledQuantity);
        }

        order.RecomputeStatus();
    }
}
