using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement;

/// <summary>
/// Closes a goods receipt: freezes it, puts the stock into inventory at the order's cost, and advances
/// the order's own received totals.
/// </summary>
/// <remarks>
/// <para>
/// Extracted for the reason <c>CONVENTIONS.md</c> §4 gives and <c>SalesReturnCompletionService</c>
/// already demonstrated. Stage 13's putaway and Stage 17's production receipt end the same three ways,
/// and a second handler calling the first is how those three steps quietly become four sequences.
/// </para>
/// <para>
/// <b>The order is advanced before the stock is posted, and that ordering is deliberate.</b> Advancing
/// the order is the step that can legitimately refuse — an over-receipt beyond tolerance is the caller's
/// error (business rule 6) — so doing it first means a refusal has moved no stock. Posting the stock
/// first and then discovering the order will not take it would leave inventory holding goods the
/// paperwork says were never delivered.
/// </para>
/// <para>
/// <b>A refused stock movement does not fail the receipt</b> (ADR-073, business rule 9). The truck has
/// gone. The line records <see cref="GoodsReceiptPostingStatus.Refused"/> with the ledger's own reason,
/// the document counts them, and <c>GET /procurement/reconciliation/stock-issues</c> lists them. Anything
/// else — a database fault, a cancellation — propagates and rolls the command back.
/// </para>
/// </remarks>
public interface IGoodsReceiptCompletionService
{
    /// <summary>Completes a receipt, posts its stock and advances its order.</summary>
    /// <param name="receipt">The draft receipt, with its lines loaded.</param>
    /// <param name="order">The order it is against, with its lines loaded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ProcurementRuleException">
    /// The receipt is not a draft or has no lines, or a line exceeds what was ordered beyond tolerance.
    /// </exception>
    Task CompleteAsync(
        GoodsReceipt receipt, PurchaseOrder order, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGoodsReceiptCompletionService" />
/// <param name="locations">Resolves the location the goods landed at.</param>
/// <param name="stockPoster">Stage 08's single posting path for a stock movement.</param>
/// <param name="options">The tenant's over-receipt tolerance.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GoodsReceiptCompletionService(
    IStockLocationRepository locations,
    IStockLedgerPoster stockPoster,
    ProcurementOptions options,
    IClock clock) : IGoodsReceiptCompletionService
{
    /// <inheritdoc />
    public async Task CompleteAsync(
        GoodsReceipt receipt, PurchaseOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);

        if (receipt.PurchaseOrderId != order.Id)
        {
            throw new ProcurementNotFoundException("purchase order for this receipt", receipt.PurchaseOrderId);
        }

        decimal tolerance = options.OverReceiptTolerancePercentage;

        // Freeze first — an empty or already-completed receipt is the caller's error and must move
        // nothing — then advance the order, which is the other step allowed to refuse.
        receipt.Complete(clock.UtcNow);

        foreach (GoodsReceiptLine line in receipt.Lines)
        {
            order.RecordReceipt(line.PurchaseOrderLineId, line.AcceptedQuantity, tolerance);

            if (!line.RejectedQuantity.IsZero)
            {
                // Only what was accepted counts as received; the rejection is carried separately because
                // it is the raw material of a supplier scorecard (business rule 7).
                order.RecordRejection(line.PurchaseOrderLineId, line.RejectedQuantity);
            }
        }

        StockLocation location = await locations
            .FindAsync(receipt.LocationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("stock location", receipt.LocationId);

        foreach (GoodsReceiptLine line in receipt.Lines)
        {
            await PostLineStockAsync(receipt, line, location, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PostLineStockAsync(
        GoodsReceipt receipt, GoodsReceiptLine line, StockLocation location, CancellationToken cancellationToken)
    {
        try
        {
            StockLedgerEntry entry = await stockPoster
                .ReceiveForPurchaseAsync(
                    location,
                    line.ItemId,
                    line.ItemVariantId,
                    line.AcceptedQuantity,
                    line.UnitCost,
                    receipt.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            line.RecordStockPosted(entry.Id);
        }
        catch (InventoryRuleException refusal)
        {
            // A receipt adds stock, so it is far less likely to be refused than an issue — but the ledger
            // owns that decision, and the pallets are in the yard either way.
            line.RecordStockPostingRefused(refusal.Message);
        }
    }
}
