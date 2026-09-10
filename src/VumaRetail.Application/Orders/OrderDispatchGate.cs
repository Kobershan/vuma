using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Orders;

namespace VumaRetail.Application.Orders;

/// <summary>
/// The Orders-side implementation of <see cref="IOrderDispatchGate"/> (ADR-111): resolves a wave
/// task's outbound references back to their orders and runs each cash-on-delivery order's
/// <see cref="SalesOrder.ReleaseForDispatch"/> guard. Declared in the Warehouse namespace so Stage 13
/// never depends on Stage 14; implemented here where the orders live.
/// </summary>
/// <param name="orders">Order lookup.</param>
public sealed class OrderDispatchGate(ISalesOrderRepository orders) : IOrderDispatchGate
{
    /// <inheritdoc />
    public async Task EnsureDispatchAllowedAsync(
        IReadOnlyCollection<string> outboundReferences,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboundReferences);

        HashSet<Guid> checkedOrders = [];

        foreach (string reference in outboundReferences)
        {
            if (!Guid.TryParse(reference, out Guid lineId))
            {
                // Not an order line (transfer demand, unreferenced task) — not this gate's business.
                continue;
            }

            Guid? orderId = await orders
                .FindOrderIdByLineAsync(lineId, cancellationToken)
                .ConfigureAwait(false);

            if (orderId is null || !checkedOrders.Add(orderId.Value))
            {
                continue;
            }

            SalesOrder? order = await orders.FindAsync(orderId.Value, cancellationToken).ConfigureAwait(false);

            if (order is null || order.SettlementTerms != SettlementTerms.CashOnDelivery)
            {
                continue;
            }

            try
            {
                order.ReleaseForDispatch(releasedAt);
            }
            catch (OrdersRuleException failure) when (failure.Code == "ORDERS_COD_DISPATCH_BLOCKED")
            {
                throw Domain.Warehouse.WarehouseRuleException.CashOnDeliveryDispatchBlocked(order.OrderNumber);
            }
        }
    }
}
