using VumaRetail.Application.Abstractions;

namespace VumaRetail.Application.Marketing;

/// <summary>
/// Bounded worker facade for durable queued delivery. Scheduling is deliberately outside the
/// domain: a hosted service or an operator job can invoke this method without gaining a second
/// transport or bypassing consent checks.
/// </summary>
public sealed class MarketingDeliveryWorker(
    IOutboundMessageRepository messages,
    MarketingDeliveryService delivery,
    IClock clock) : IMarketingDeliveryWorker
{
    /// <inheritdoc />
    public async Task<int> DispatchDueAsync(Guid companyId, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }

        IReadOnlyList<VumaRetail.Domain.Marketing.OutboundMessage> due = await messages
            .ListQueuedAsync(companyId, clock.UtcNow, dueOnly: true, limit, cancellationToken)
            .ConfigureAwait(false);
        int processed = 0;
        foreach (VumaRetail.Domain.Marketing.OutboundMessage message in due)
        {
            await delivery.DispatchAsync(message.Id, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }
}
