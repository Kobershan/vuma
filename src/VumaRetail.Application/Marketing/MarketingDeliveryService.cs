#pragma warning disable CS1591
using VumaRetail.Domain.Marketing;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Application.Marketing;

public enum MarketingDispatchOutcome { Delivered, Suppressed, Failed }

public sealed class MarketingDeliveryService(
    IMarketingCampaignRepository campaigns,
    IOutboundMessageRepository messages,
    MarketingDeliveryPolicy policy,
    IMarketingTransport transport,
    IClock clock)
{
    public async Task<MarketingDispatchOutcome> DispatchAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        OutboundMessage message = await messages.FindAsync(messageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Outbound message was not found.");
        if (message.Status != OutboundMessageStatus.Queued)
        {
            return message.Status == OutboundMessageStatus.Suppressed
                ? MarketingDispatchOutcome.Suppressed
                : message.Status == OutboundMessageStatus.Sent
                    ? MarketingDispatchOutcome.Delivered
                    : MarketingDispatchOutcome.Failed;
        }

        MarketingCampaign campaign = await campaigns.FindAsync(message.CampaignId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Marketing campaign was not found.");
        if (campaign.Status != MarketingCampaignStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled campaign can deliver messages.");
        }
        DateTimeOffset now = clock.UtcNow;
        if (message.ScheduledAt > now)
        {
            // A worker may safely scan ahead.  The message remains queued until its due time.
            return MarketingDispatchOutcome.Failed;
        }
        if (!await policy.MaySendAsync(message.CustomerId, (MarketingChannel)message.Channel,
                (MessageClassification)message.Classification, now, cancellationToken).ConfigureAwait(false))
        {
            message.Suppress();
            return MarketingDispatchOutcome.Suppressed;
        }

        MarketingTransportResult result = await transport.SendAsync(message, campaign, cancellationToken).ConfigureAwait(false);
        message.ApplyProviderResult(result.ProviderEventId, result.PayloadFingerprint, result.Delivered);
        return result.Delivered ? MarketingDispatchOutcome.Delivered : MarketingDispatchOutcome.Failed;
    }
}
