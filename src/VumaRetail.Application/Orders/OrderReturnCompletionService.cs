using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders;

/// <summary>
/// Closes an order return: freezes it, receives the stock back at the original shipment's unit cost,
/// and raises the financial event the ledger posts from.
/// </summary>
/// <remarks>
/// <para>
/// Extracted for the reason <c>CONVENTIONS.md</c> §4 gives and <c>SalesReturnCompletionService</c>
/// already demonstrated: closing always takes the same three steps, and a second handler calling the
/// first is how those three steps quietly become four different sequences.
/// </para>
/// <para>
/// <b>Neither downstream step may fail without taking the return with it, and neither failure is
/// silent</b> — ADR-070/ADR-073's precedent, applied to an order return the same way
/// <c>SalesReturnCompletionService</c> applies it to a till return. A ledger that refuses the receipt
/// marks the line <see cref="OrderStockReturnStatus.Refused"/> with the reason; a missing posting rule
/// is swallowed and logged by the publisher. Anything else propagates and rolls the command back.
/// </para>
/// </remarks>
public interface IOrderReturnCompletionService
{
    /// <summary>Completes a return, receives its stock back and raises its financial event.</summary>
    /// <param name="orderReturn">The open return, with its lines loaded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrdersRuleException">The return is not open, or has no lines.</exception>
    /// <exception cref="OrdersNotFoundException">The order the return names, or its fulfilling location, cannot be found.</exception>
    Task CompleteAsync(SalesOrderReturn orderReturn, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOrderReturnCompletionService" />
/// <param name="orders">Resolves the order the return is against — its fulfilling location and currency.</param>
/// <param name="locations">Resolves the location the goods come back to.</param>
/// <param name="fulfilment">Reads the original shipment's unit cost (ADR-093).</param>
/// <param name="stockPoster">Stage 08's single posting path for a stock movement.</param>
/// <param name="financialEvents">Where the completed return's financial event is raised.</param>
/// <param name="clock">The only source of time.</param>
public sealed class OrderReturnCompletionService(
    ISalesOrderRepository orders,
    IStockLocationRepository locations,
    IOrderFulfilmentReader fulfilment,
    IStockLedgerPoster stockPoster,
    IOrderReturnFinancialEventPublisher financialEvents,
    IClock clock) : IOrderReturnCompletionService
{
    /// <inheritdoc />
    public async Task CompleteAsync(SalesOrderReturn orderReturn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderReturn);

        // Complete first, for the reason SalesReturnCompletionService completes first: it is the step
        // that can legitimately refuse — an empty or already-completed return is the caller's error —
        // and doing it before anything downstream means a refusal has moved no stock and raised no
        // event.
        orderReturn.Complete(clock.UtcNow);

        SalesOrder order = await orders.FindAsync(orderReturn.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", orderReturn.SalesOrderId);

        StockLocation location = await locations
            .FindAsync(order.FulfillingLocationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("stock location", order.FulfillingLocationId);

        foreach (SalesOrderReturnLine line in orderReturn.Lines)
        {
            await ReceiveLineStockAsync(orderReturn, line, location, cancellationToken).ConfigureAwait(false);
        }

        OrderReturnCompletedEvent completed = new(
            orderReturn.TenantId,
            orderReturn.StoreId,
            orderReturn.Id,
            orderReturn.ReturnNumber,
            orderReturn.SalesOrderId,
            orderReturn.Net,
            orderReturn.Tax,
            orderReturn.Gross,
            orderReturn.CompletedAt ?? clock.UtcNow);

        await financialEvents.PublishAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLineStockAsync(
        SalesOrderReturn orderReturn, SalesOrderReturnLine line, StockLocation location, CancellationToken cancellationToken)
    {
        Money? unitCost = await fulfilment
            .GetShipmentUnitCostAsync(line.SalesOrderLineId, line.ItemId, line.ItemVariantId, line.UnitPrice.Currency, cancellationToken)
            .ConfigureAwait(false);

        if (unitCost is not { } cost)
        {
            // No shipment could be traced for this line — the demand was never actually picked and
            // shipped through Stage 13, so there is no cost basis to receive the goods back at.
            // Receiving at today's average would invent a cost basis for stock that, as far as the
            // ledger is concerned, never left. The refund still stands (ADR-070/073's precedent) and the
            // line says why, which puts it in the same reconciliation queue as a refused sale-line issue.
            line.RecordStockReturnRefused(
                "No shipment could be traced for this line's original quantity, so there is no cost to "
                + "receive the goods back at. Reconcile with a stocktake.");

            return;
        }

        try
        {
            StockLedgerEntry entry = await stockPoster
                .ReceiveForOrderReturnAsync(
                    location, line.ItemId, line.ItemVariantId, line.Quantity, cost, orderReturn.Id, cancellationToken)
                .ConfigureAwait(false);

            line.RecordStockReturned(entry.Id);
        }
        catch (InventoryRuleException refusal)
        {
            // A receipt is far less likely to be refused than an issue, but the ledger owns that
            // decision.
            line.RecordStockReturnRefused(refusal.Message);
        }
    }
}
