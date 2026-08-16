using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales;

/// <summary>
/// The one financial fact a completed return raises: this return, at this store, for this net, tax
/// and gross.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §7 rule 12, exactly as <c>SaleTenderedEvent</c> honours it: there is no field a GL
/// account code could go in. The event says money went back out of the shop and how much of it was
/// tax; whether that lands on a sales-returns contra account or straight against revenue, and which
/// tax control account it reverses, is a posting rule a tenant owns (ADR-016).
/// </remarks>
/// <param name="TenantId">The tenant the return belongs to.</param>
/// <param name="StoreId">The store it happened at.</param>
/// <param name="SalesReturnId">The return.</param>
/// <param name="ReturnNumber">Its credit slip number — what a person reconciling the journal searches on.</param>
/// <param name="SaleId">The sale it reverses part of.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back, derived from the original lines' stored tax (ADR-075).</param>
/// <param name="Gross">What the customer got back.</param>
/// <param name="OccurredAt">When the return completed, UTC.</param>
public sealed record SalesReturnCompletedEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid SalesReturnId,
    string ReturnNumber,
    Guid SaleId,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where sales raises the financial event a completed return produces, for whatever turns it into a
/// balanced journal.
/// </summary>
/// <remarks>
/// The same port shape, and the same two reasons, as <c>ISaleFinancialEventPublisher</c>:
/// <c>VumaRetail.Application</c> may not reference <c>VumaRetail.Finance</c>, and a host wired without
/// a finance module must still be able to take goods back rather than fail to build a container.
/// </remarks>
public interface ISalesReturnFinancialEventPublisher
{
    /// <summary>Raises one return event.</summary>
    /// <param name="returnEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(SalesReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Closes a return: freezes it, puts the stock back, and raises the financial event the ledger posts
/// from.
/// </summary>
/// <remarks>
/// <para>
/// Extracted for the reason <c>CONVENTIONS.md</c> §4 gives and <c>SaleCompletionService</c> already
/// demonstrated: Stage 10b's lay-by cancellation and Stage 14's order returns end the same three ways,
/// and a second handler calling the first is how those three steps quietly become four different
/// sequences.
/// </para>
/// <para>
/// <b>Both downstream steps may fail without taking the refund with them, and neither failure is
/// silent.</b> ADR-073 was written about a sale and applies with more force here: the customer is at
/// the counter, the goods are already back over it and the money has been handed over. A ledger that
/// refuses the receipt marks the line <see cref="StockReturnStatus.Refused"/> with the reason, and a
/// missing posting rule is swallowed and logged by the publisher exactly as ADR-070 requires. Anything
/// else — a database fault, a cancellation — propagates and rolls the command back.
/// </para>
/// </remarks>
public interface ISalesReturnCompletionService
{
    /// <summary>Completes a return, puts its stock back and raises its financial event.</summary>
    /// <param name="salesReturn">The draft return, with its lines loaded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="SalesRuleException">The return is not a draft, or has no lines.</exception>
    Task CompleteAsync(SalesReturn salesReturn, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISalesReturnCompletionService" />
/// <param name="locations">Resolves the location the goods come back to.</param>
/// <param name="ledger">Reads the original issue entry, to receive the goods back at what they left at.</param>
/// <param name="stockPoster">Stage 08's single posting path for a stock movement.</param>
/// <param name="financialEvents">Where the completed return's financial event is raised.</param>
/// <param name="clock">The only source of time.</param>
public sealed class SalesReturnCompletionService(
    IStockLocationRepository locations,
    IStockLedgerRepository ledger,
    IStockLedgerPoster stockPoster,
    ISalesReturnFinancialEventPublisher financialEvents,
    IClock clock) : ISalesReturnCompletionService
{
    /// <inheritdoc />
    public async Task CompleteAsync(SalesReturn salesReturn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(salesReturn);

        // Complete first, for the reason SaleCompletionService completes first: it is the step that can
        // legitimately refuse — an empty or already-completed return is the caller's error — and doing
        // it before anything downstream means a refusal has moved no stock and raised no event.
        salesReturn.Complete(clock.UtcNow);

        StockLocation location = await locations
            .FindAsync(salesReturn.LocationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SalesNotFoundException("stock location", salesReturn.LocationId);

        foreach (SalesReturnLine line in salesReturn.Lines)
        {
            await ReceiveLineStockAsync(salesReturn, line, location, cancellationToken).ConfigureAwait(false);
        }

        SalesReturnCompletedEvent completed = new(
            salesReturn.TenantId,
            salesReturn.StoreId,
            salesReturn.Id,
            salesReturn.ReturnNumber,
            salesReturn.SaleId,
            salesReturn.Net,
            salesReturn.Tax,
            salesReturn.Gross,
            salesReturn.CompletedAt ?? clock.UtcNow);

        await financialEvents.PublishAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLineStockAsync(
        SalesReturn salesReturn, SalesReturnLine line, StockLocation location, CancellationToken cancellationToken)
    {
        Money? originalCost = await FindOriginalCostAsync(line, cancellationToken).ConfigureAwait(false);

        if (originalCost is not { } unitCost)
        {
            // The original sale never relieved this line's stock — its own issue was refused (ADR-073)
            // — so there is no cost the goods left at. Receiving them at today's average would invent a
            // cost basis for stock that, as far as the ledger is concerned, never went anywhere. The
            // refund stands and the line says why, which puts it in the same reconciliation queue as
            // the sale line that caused it.
            line.RecordStockReturnRefused(
                "The original sale line never relieved stock, so there is no cost to receive the goods "
                + "back at. Reconcile with a stocktake — see the refused stock issue on the sale.");

            return;
        }

        try
        {
            StockLedgerEntry entry = await stockPoster
                .ReceiveForSalesReturnAsync(
                    location, line.ItemId, line.ItemVariantId, line.Quantity, unitCost, salesReturn.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            line.RecordStockReturned(entry.Id);
        }
        catch (InventoryRuleException refusal)
        {
            // A receipt is far less likely to be refused than an issue — it adds stock rather than
            // removing it — but the ledger owns that decision and the customer is still standing there.
            line.RecordStockReturnRefused(refusal.Message);
        }
    }

    /// <summary>
    /// What one unit cost when it left, read off the ledger entry the original sale line produced.
    /// </summary>
    /// <remarks>
    /// The cost is not on the sale line — Stage 09 stored what the customer paid, which is the right
    /// thing for a receipt and the wrong thing for a stock receipt. The issue entry is where the
    /// weighted-average cost at the moment of sale was written down, and the sale line already carries
    /// its id, so this is a lookup rather than a reconstruction.
    /// </remarks>
    private async Task<Money?> FindOriginalCostAsync(SalesReturnLine line, CancellationToken cancellationToken)
    {
        if (line.OriginalStockLedgerEntryId is not { } entryId)
        {
            return null;
        }

        StockLedgerEntry? entry = await ledger.FindAsync(entryId, cancellationToken).ConfigureAwait(false);

        return entry?.UnitCost;
    }
}
