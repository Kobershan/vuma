#pragma warning disable CS1591
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Application.Marketing;

public interface IMarketingCampaignRepository
{
    Task<MarketingCampaign?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(MarketingCampaign campaign);
}

public interface IOutboundMessageRepository
{
    Task<OutboundMessage?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OutboundMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundMessage>> ListQueuedAsync(Guid companyId, DateTimeOffset asAt, bool dueOnly,
        int limit, CancellationToken cancellationToken = default);
    void Add(OutboundMessage message);
}

public sealed record MarketingTransportResult(string ProviderEventId, string PayloadFingerprint, bool Delivered);

public interface IMarketingTransport
{
    Task<MarketingTransportResult> SendAsync(OutboundMessage message, MarketingCampaign campaign,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs due marketing messages through the durable delivery boundary.</summary>
public interface IMarketingDeliveryWorker
{
    Task<int> DispatchDueAsync(Guid companyId, int limit = 100, CancellationToken cancellationToken = default);
}
