using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Sales;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns a posted invoice into an <see cref="IFinancialEvent"/> and posts it through Stage 07's
/// posting rules engine, in the invoice's own company database.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §7 rule 12 is satisfied by construction: this class names an event type and three
/// named amounts, and has nowhere to put a GL account even if it wanted one.
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialInvoiceEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialInvoiceEventPublisher> logger) : IInvoiceFinancialEventPublisher
{
    /// <summary>
    /// The event type a posted invoice raises. <c>DemoSeed</c> seeds a posting rule for it the way it
    /// already does for sales and returns.
    /// </summary>
    public const string InvoicePostedEventType = "sales.invoice.posted";

    /// <inheritdoc />
    public async Task PublishAsync(InvoicePostedEvent invoiceEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoiceEvent);

        FinancialEvent financialEvent = new(
            InvoicePostedEventType,
            invoiceEvent.TenantId,
            invoiceEvent.StoreId,
            invoiceEvent.OccurredAt,
            invoiceEvent.InvoiceNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = invoiceEvent.Net,
                ["Tax"] = invoiceEvent.Tax,
                ["Gross"] = invoiceEvent.Gross,
            });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // ADR-070, applied to an invoice: a shop that cannot document a fulfilled order because
            // an accountant has not finished the chart of accounts is a shop that has stopped
            // trading (R1). The invoice stands recorded and posted; only the journal is missing,
            // and that is fixable afterwards in a way an undocumented fulfilment is not.
            logger.LogWarning(
                "No posting rule for {EventType}; invoice {InvoiceNumber} posted but raised no journal. "
                + "Configure a posting rule for this event type and subsequent invoices will post.",
                InvoicePostedEventType,
                invoiceEvent.InvoiceNumber);
        }
    }
}

/// <summary>
/// Records that an invoice event was raised and does nothing else.
/// </summary>
/// <remarks>
/// The fallback for hosts that wire sales without finance — the same position
/// <c>LoggingSaleEventPublisher</c> holds for POS. See <c>AddVumaSales</c> for how one or the
/// other is chosen at container build time.
/// </remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingInvoiceEventPublisher(ILogger<LoggingInvoiceEventPublisher> logger)
    : IInvoiceFinancialEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(InvoicePostedEvent invoiceEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoiceEvent);

        logger.LogDebug(
            "Invoice {InvoiceNumber} posted for {Gross} ({Net} net, {Tax} tax) in company {CompanyId}. "
            + "No financial poster is registered, so this raised no journal.",
            invoiceEvent.InvoiceNumber,
            invoiceEvent.Gross,
            invoiceEvent.Net,
            invoiceEvent.Tax,
            invoiceEvent.CompanyId);

        return Task.CompletedTask;
    }
}
