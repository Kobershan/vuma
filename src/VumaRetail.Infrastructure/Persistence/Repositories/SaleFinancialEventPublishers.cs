using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns a completed sale into an <see cref="IFinancialEvent"/> and posts it through Stage 07's posting
/// rules engine.
/// </summary>
/// <remarks>
/// <para>
/// The same shape <c>FinancialInventoryValuationEventPublisher</c> has, and deliberately so — a
/// producer module raises a named event with named amounts, and this is the thin adapter that hands it
/// to the engine.
/// </para>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 12 is satisfied by construction: this class names an event type and three
/// named amounts, and has nowhere to put a GL account even if it wanted one. The rule
/// <c>DemoSeed</c> seeds debits bank and credits sales and VAT; a tenant who banks card takings
/// separately edits that rule rather than this class.
/// </para>
/// <para>
/// <b>One event type, not one per tender.</b> A sale is a single revenue event whatever it was paid
/// with, and splitting it here would push a POS concept — tender types — into the accounting model.
/// A tenant who needs the split gets it by raising per-tender event types later; the amounts are
/// already named, so it is a rule change plus a second event, not a schema change.
/// </para>
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialSaleEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialSaleEventPublisher> logger) : ISaleFinancialEventPublisher
{
    /// <summary>
    /// The event type a completed sale raises. <c>DemoSeed</c> has seeded a posting rule for it since
    /// Stage 07 — deliberately, as the demonstration that rule 12 works without Finance changing when
    /// its first real producer arrives. This is that producer.
    /// </summary>
    public const string SaleTenderedEventType = "pos.sale.tendered";

    /// <inheritdoc />
    public async Task PublishAsync(SaleTenderedEvent saleEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saleEvent);

        FinancialEvent financialEvent = new(
            SaleTenderedEventType,
            saleEvent.TenantId,
            saleEvent.StoreId,
            saleEvent.OccurredAt,
            saleEvent.SaleNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = saleEvent.Net,
                ["Tax"] = saleEvent.Tax,
                ["Gross"] = saleEvent.Gross,
            });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // ADR-070, applied to a sale rather than a stock movement, and the stakes are higher here:
            // a store that cannot ring up a customer because an accountant has not finished the chart
            // of accounts is a store that has stopped trading (R1). The sale, its tenders and its stock
            // movements are all recorded; only the journal is missing, and that is fixable afterwards
            // in a way that a queue of turned-away customers is not.
            logger.LogWarning(
                "No posting rule for {EventType}; sale {SaleNumber} completed but raised no journal. "
                + "Configure a posting rule for this event type and subsequent sales will post.",
                SaleTenderedEventType,
                saleEvent.SaleNumber);
        }
    }
}

/// <summary>
/// Records that a sale event was raised and does nothing else.
/// </summary>
/// <remarks>
/// The fallback for hosts that wire POS without finance — the same position
/// <c>LoggingInventoryValuationEventPublisher</c> holds for Stage 08. See <c>AddVumaPos</c> for how
/// one or the other is chosen at container build time.
/// </remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingSaleEventPublisher(ILogger<LoggingSaleEventPublisher> logger)
    : ISaleFinancialEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(SaleTenderedEvent saleEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saleEvent);

        logger.LogDebug(
            "Sale {SaleNumber} tendered for {Gross} ({Net} net, {Tax} tax). No financial poster is "
            + "registered, so this raised no journal.",
            saleEvent.SaleNumber,
            saleEvent.Gross,
            saleEvent.Net,
            saleEvent.Tax);

        return Task.CompletedTask;
    }
}
