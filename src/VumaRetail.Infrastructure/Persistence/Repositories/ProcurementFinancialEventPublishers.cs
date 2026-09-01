using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns a released three-way match into an <see cref="IFinancialEvent"/> and posts it through Stage
/// 07's posting rules engine.
/// </summary>
/// <remarks>
/// <para>
/// The same thin adapter <c>FinancialSalesReturnEventPublisher</c> is, on the buying side.
/// <c>CLAUDE.md</c> §7 rule 12 is satisfied by construction: this class names an event type and three
/// named amounts and has nowhere to put a GL account. That the seeded rule relieves
/// goods-received-not-invoiced and credits trade creditors is the tenant's configuration, not
/// procurement's code (ADR-085).
/// </para>
/// <para>
/// <b>Why the amounts are the supplier's claim rather than what the order supports.</b> A match that
/// reaches release has already been judged payable — either it agreed exactly, or its variance was
/// inside the tenant's tolerance and somebody with <c>procurement.invoice.release</c> accepted it. The
/// liability that crystallises is the amount the supplier will actually be paid, and posting the
/// order's figure instead would leave the difference sitting in the GRNI clearing account forever with
/// nothing to clear it.
/// </para>
/// <para>
/// <b>It returns the journal id</b>, unlike Stage 10's publisher. A supplier invoice is the document
/// accounts payable reconciles against the ledger by hand when something goes wrong, and being able to
/// go straight from the match to its journal is the difference between a five-minute answer and an
/// afternoon.
/// </para>
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialProcurementEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialProcurementEventPublisher> logger) : IProcurementFinancialEventPublisher
{
    /// <summary>The event type a released supplier invoice match raises.</summary>
    public const string SupplierInvoiceMatchedEventType = "procurement.invoice.matched";

    /// <inheritdoc />
    public async Task<Guid?> PublishAsync(
        SupplierInvoiceMatchedEvent matchedEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matchedEvent);

        FinancialEvent financialEvent = new(
            SupplierInvoiceMatchedEventType,
            matchedEvent.TenantId,
            matchedEvent.StoreId,
            matchedEvent.OccurredAt,

            // The supplier's invoice number, not the order's. This is the reference accounts payable and
            // the supplier both quote when they disagree about whether something was paid.
            matchedEvent.SupplierInvoiceNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = matchedEvent.Net,
                ["Tax"] = matchedEvent.Tax,
                ["Gross"] = matchedEvent.Gross,
            });

        try
        {
            return await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // ADR-070. The goods are in stock, the invoice has been checked against the delivery and a
            // supervisor has released it; refusing that because a clearing account has not been
            // configured would mean the release has to be done again later against a match that is
            // already frozen. The match, its comparison and the order's invoiced totals are all
            // recorded — only the journal is missing, and that is fixable afterwards.
            logger.LogWarning(
                "No posting rule for {EventType}; supplier invoice {InvoiceNumber} on order {OrderNumber} "
                + "was released but raised no journal. Configure a posting rule for this event type — it "
                + "should relieve goods-received-not-invoiced and credit trade creditors — and subsequent "
                + "releases will post.",
                SupplierInvoiceMatchedEventType,
                matchedEvent.SupplierInvoiceNumber,
                matchedEvent.OrderNumber);

            return null;
        }
    }
}

/// <summary>
/// Records that a matched-invoice event was raised and does nothing else.
/// </summary>
/// <remarks>
/// The fallback for hosts that wire procurement without finance — the same position
/// <c>LoggingSalesReturnEventPublisher</c> holds for Stage 10. See <c>AddVumaProcurement</c> for how
/// one or the other is chosen at container build time.
/// </remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingProcurementEventPublisher(ILogger<LoggingProcurementEventPublisher> logger)
    : IProcurementFinancialEventPublisher
{
    /// <inheritdoc />
    public Task<Guid?> PublishAsync(
        SupplierInvoiceMatchedEvent matchedEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matchedEvent);

        logger.LogDebug(
            "Supplier invoice {InvoiceNumber} on order {OrderNumber} released for {Gross} ({Net} net, "
            + "{Tax} tax). No financial poster is registered, so this raised no journal.",
            matchedEvent.SupplierInvoiceNumber,
            matchedEvent.OrderNumber,
            matchedEvent.Gross,
            matchedEvent.Net,
            matchedEvent.Tax);

        return Task.FromResult<Guid?>(null);
    }
}
