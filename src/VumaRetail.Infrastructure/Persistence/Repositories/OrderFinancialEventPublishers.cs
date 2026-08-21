using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Orders;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns a recognised-revenue event into an <see cref="IFinancialEvent"/> and posts it through Stage
/// 07's posting rules engine.
/// </summary>
/// <remarks>
/// The same thin adapter <c>FinancialSalesReturnEventPublisher</c> is. <c>CLAUDE.md</c> §7 rule 12 is
/// satisfied by construction: this class names an event type and three named amounts, and has nowhere
/// to put a GL account. The amounts are positive; the posting rule for <c>orders.order.fulfilled</c>
/// decides which side of the journal each lands on.
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialOrderFulfilmentEventPublisher(
    IFinancialEventPoster poster, ILogger<FinancialOrderFulfilmentEventPublisher> logger)
    : IOrderFulfilmentEventPublisher
{
    /// <summary>The event type completing an order raises.</summary>
    public const string OrderFulfilledEventType = "orders.order.fulfilled";

    /// <inheritdoc />
    public async Task PublishAsync(OrderFulfilledEvent fulfilledEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fulfilledEvent);

        FinancialEvent financialEvent = new(
            OrderFulfilledEventType,
            fulfilledEvent.TenantId,
            fulfilledEvent.StoreId,
            fulfilledEvent.OccurredAt,
            fulfilledEvent.OrderNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = fulfilledEvent.Net,
                ["Tax"] = fulfilledEvent.Tax,
                ["Gross"] = fulfilledEvent.Gross,
            });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // ADR-070's precedent. An order that has genuinely shipped does not un-ship because the
            // accountant has not configured a revenue account yet — the order, its lines and every
            // stock movement are all recorded; only the journal is missing, and that is fixable
            // afterwards.
            logger.LogWarning(
                "No posting rule for {EventType}; order {OrderNumber} completed but raised no journal. "
                + "Configure a posting rule for this event type and subsequent orders will post.",
                OrderFulfilledEventType,
                fulfilledEvent.OrderNumber);
        }
    }
}

/// <summary>
/// Records that an order-fulfilled event was raised and does nothing else.
/// </summary>
/// <remarks>The fallback for hosts that wire orders without finance — the same position <c>LoggingSalesReturnEventPublisher</c> holds.</remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingOrderFulfilmentEventPublisher(ILogger<LoggingOrderFulfilmentEventPublisher> logger)
    : IOrderFulfilmentEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(OrderFulfilledEvent fulfilledEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fulfilledEvent);

        logger.LogDebug(
            "Order {OrderNumber} completed for {Gross} ({Net} net, {Tax} tax). No financial poster is "
            + "registered, so this raised no journal.",
            fulfilledEvent.OrderNumber,
            fulfilledEvent.Gross,
            fulfilledEvent.Net,
            fulfilledEvent.Tax);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns a completed order return into an <see cref="IFinancialEvent"/> and posts it through Stage 07's
/// posting rules engine.
/// </summary>
/// <param name="poster">Stage 07's posting rules engine.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialOrderReturnEventPublisher(
    IFinancialEventPoster poster, ILogger<FinancialOrderReturnEventPublisher> logger)
    : IOrderReturnFinancialEventPublisher
{
    /// <summary>The event type completing an order return raises.</summary>
    public const string OrderReturnCompletedEventType = "orders.return.completed";

    /// <inheritdoc />
    public async Task PublishAsync(OrderReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(returnEvent);

        FinancialEvent financialEvent = new(
            OrderReturnCompletedEventType,
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
            logger.LogWarning(
                "No posting rule for {EventType}; return {ReturnNumber} completed but raised no journal. "
                + "Configure a posting rule for this event type and subsequent returns will post.",
                OrderReturnCompletedEventType,
                returnEvent.ReturnNumber);
        }
    }
}

/// <summary>Records that an order-return-completed event was raised and does nothing else.</summary>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingOrderReturnEventPublisher(ILogger<LoggingOrderReturnEventPublisher> logger)
    : IOrderReturnFinancialEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(OrderReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default)
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
