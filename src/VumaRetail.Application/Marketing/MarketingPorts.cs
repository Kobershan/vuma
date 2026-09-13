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
    void Add(OutboundMessage message);
}
