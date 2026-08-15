using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Pos;

/// <summary>
/// The one financial fact a completed sale raises: this sale, at this store, for this net, tax and
/// gross.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §7 rule 12: no module names a GL account. This event says <em>what happened</em> —
/// it has no field a GL account code could go in, and nothing in this module resolves one. Stage 07's
/// posting rules engine turns "R115 was tendered at the till, R100 of it net and R15 of it VAT" into a
/// debit and a credit, through a rule row a tenant owns.
/// </remarks>
/// <param name="TenantId">The tenant the sale belongs to.</param>
/// <param name="StoreId">The store it happened at.</param>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleNumber">Its receipt number, which is what a person reconciling the journal will search on.</param>
/// <param name="Net">The sale excluding tax.</param>
/// <param name="Tax">The tax.</param>
/// <param name="Gross">What the customer paid.</param>
/// <param name="OccurredAt">When the sale completed, UTC.</param>
public sealed record SaleTenderedEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid SaleId,
    string SaleNumber,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where POS raises the financial event a completed sale produces, for whatever turns it into a
/// balanced journal.
/// </summary>
/// <remarks>
/// The same shape Stage 08's <c>IInventoryValuationEventPublisher</c> has, and for the same two
/// reasons. First, <c>VumaRetail.Application</c> may not reference <c>VumaRetail.Finance</c>, so the
/// engine has to be reached through a port. Second, a host wired without a finance module — the
/// desktop app's offline cache, a mapping-probe test context — must still be able to sell; it gets the
/// logging fallback rather than a container that fails to build.
/// </remarks>
public interface ISaleFinancialEventPublisher
{
    /// <summary>Raises one sale event.</summary>
    /// <param name="saleEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(SaleTenderedEvent saleEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Closes a sale: freezes it, relieves the stock behind it, and raises the financial event the ledger
/// posts from.
/// </summary>
/// <remarks>
/// <para>
/// Extracted rather than written into <c>CompleteSaleCommandHandler</c> because completion is the one
/// piece of this module that other paths will need verbatim — a self-checkout, an offline sale
/// replayed by the sync receiver, and Stage 10b's lay-by settlement all end the same two ways.
/// <c>CONVENTIONS.md</c> §4: extract a domain service, do not call one handler from another.
/// </para>
/// <para>
/// <b>This class is where R1 is either true or false.</b> Both downstream steps are allowed to fail
/// without taking the sale with them, and neither failure is silent:
/// </para>
/// <list type="bullet">
/// <item>
/// A stock issue the ledger refuses marks the line <see cref="StockIssueStatus.Refused"/> with the
/// reason. Stage 08's rule 4 (stock never goes negative) holds, the customer still leaves with the
/// goods, and the discrepancy becomes a queryable reconciliation queue —
/// <c>ListRefusedStockIssuesQuery</c> — rather than a lost sale. See ADR-073. The recorded row is the
/// report: a log line would be weaker, because nobody reconciles from a log.
/// </item>
/// <item>
/// A missing posting rule is swallowed and logged by the publisher's infrastructure implementation,
/// exactly the call ADR-070 made for a stock movement, for the same reason: a tenant who has not
/// finished configuring their chart of accounts must not discover it by being unable to trade.
/// </item>
/// </list>
/// <para>
/// Anything else — a database failure, a cancellation — propagates and rolls the whole command back.
/// The two cases above are business outcomes; the rest are faults, and pretending a fault succeeded is
/// how a store's books quietly stop matching its shelves.
/// </para>
/// </remarks>
public interface ISaleCompletionService
{
    /// <summary>Completes a sale, relieves its stock and raises its financial event.</summary>
    /// <param name="sale">The open sale, with its lines and tenders loaded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="PosRuleException">The sale is not open, is empty, or is not covered by its tenders.</exception>
    Task CompleteAsync(Sale sale, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISaleCompletionService" />
/// <param name="locations">Resolves the sale's stock location.</param>
/// <param name="stockPoster">Stage 08's single posting path for a stock movement.</param>
/// <param name="financialEvents">Where the completed sale's financial event is raised.</param>
/// <param name="clock">The only source of time.</param>
public sealed class SaleCompletionService(
    IStockLocationRepository locations,
    IStockLedgerPoster stockPoster,
    ISaleFinancialEventPublisher financialEvents,
    IClock clock) : ISaleCompletionService
{
    /// <inheritdoc />
    public async Task CompleteAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        // Complete first. It is the step that can legitimately refuse — an empty or under-tendered sale
        // is the caller's error — and doing it before anything downstream means a refusal has moved no
        // stock and raised no event.
        sale.Complete(clock.UtcNow);

        StockLocation location = await locations.FindAsync(sale.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("stock location", sale.LocationId);

        foreach (SaleLine line in sale.LiveLines)
        {
            await IssueLineStockAsync(sale, line, location, cancellationToken).ConfigureAwait(false);
        }

        SaleTenderedEvent tendered = new(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            sale.SaleNumber,
            sale.Net,
            sale.Tax,
            sale.Gross,
            sale.CompletedAt ?? clock.UtcNow);

        await financialEvents.PublishAsync(tendered, cancellationToken).ConfigureAwait(false);
    }

    private async Task IssueLineStockAsync(
        Sale sale, SaleLine line, StockLocation location, CancellationToken cancellationToken)
    {
        try
        {
            StockLedgerEntry entry = await stockPoster
                .IssueForSaleAsync(location, line.ItemId, line.ItemVariantId, line.Quantity, sale.Id, cancellationToken)
                .ConfigureAwait(false);

            line.RecordStockIssued(entry.Id);
        }
        catch (InventoryRuleException refusal)
        {
            // The shelf and the system disagree, and the shelf wins — the customer is holding the item.
            // Recorded on the line rather than swallowed: this row is what ListRefusedStockIssuesQuery
            // reads, which is how a manager finds it and reconciles with a stocktake (ADR-073).
            line.RecordStockIssueRefused(refusal.Message);
        }
    }
}
