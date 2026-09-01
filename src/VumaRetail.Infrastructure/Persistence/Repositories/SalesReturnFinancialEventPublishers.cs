using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Sales;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns a completed sales return into an <see cref="IFinancialEvent"/> and posts it through Stage 07's
/// posting rules engine.
/// </summary>
/// <remarks>
/// <para>
/// The same thin adapter <c>FinancialSaleEventPublisher</c> is, in the other direction. <c>CLAUDE.md</c>
/// §7 rule 12 is satisfied by construction: this class names an event type and three named amounts, and
/// has nowhere to put a GL account.
/// </para>
/// <para>
/// <b>The amounts are positive and the rule decides the sign.</b> A return raises <c>Net</c>,
/// <c>Tax</c> and <c>Gross</c> as the magnitudes that came back, exactly as a sale raises the
/// magnitudes that went out, and the posting rule for <c>sales.return.completed</c> puts them on the
/// opposite sides. Negating them here would encode a debit-and-credit decision in a sales module, which
/// is the thing rule 12 exists to prevent — and it would double-negate for any tenant whose rule
/// already reverses the sides.
/// </para>
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialSalesReturnEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialSalesReturnEventPublisher> logger) : ISalesReturnFinancialEventPublisher
{
    /// <summary>The event type a completed return raises.</summary>
    public const string SalesReturnCompletedEventType = "sales.return.completed";

    /// <inheritdoc />
    public async Task PublishAsync(
        SalesReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(returnEvent);

        FinancialEvent financialEvent = new(
            SalesReturnCompletedEventType,
            returnEvent.TenantId,
            returnEvent.StoreId,
            returnEvent.OccurredAt,
            returnEvent.ReturnNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = returnEvent.Net,
                ["Tax"] = returnEvent.Tax,
                ["Gross"] = returnEvent.Gross,
            });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // ADR-070. A customer standing at the counter with a faulty kettle is not going to wait for
            // an accountant to finish configuring a contra-revenue account, and refusing the refund
            // would cost the shop the customer as well as the sale. The return, its lines and its stock
            // movements are all recorded; only the journal is missing, and that is fixable afterwards.
            logger.LogWarning(
                "No posting rule for {EventType}; return {ReturnNumber} completed but raised no journal. "
                + "Configure a posting rule for this event type and subsequent returns will post.",
                SalesReturnCompletedEventType,
                returnEvent.ReturnNumber);
        }
    }
}

/// <summary>
/// Records that a return event was raised and does nothing else.
/// </summary>
/// <remarks>
/// The fallback for hosts that wire sales without finance — the same position
/// <c>LoggingSaleEventPublisher</c> holds for Stage 09. See <c>AddVumaSales</c> for how one or the
/// other is chosen at container build time.
/// </remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingSalesReturnEventPublisher(ILogger<LoggingSalesReturnEventPublisher> logger)
    : ISalesReturnFinancialEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(
        SalesReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(returnEvent);

        logger.LogDebug(
            "Return {ReturnNumber} completed for {Gross} ({Net} net, {Tax} tax). No financial poster is "
            + "registered, so this raised no journal.",
            returnEvent.ReturnNumber,
            returnEvent.Gross,
            returnEvent.Net,
            returnEvent.Tax);

        return Task.CompletedTask;
    }
}
