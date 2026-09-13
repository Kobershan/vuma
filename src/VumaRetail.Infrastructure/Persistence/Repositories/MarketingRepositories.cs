#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class MarketingCampaignRepository(VumaRetailDbContext db) : IMarketingCampaignRepository
{
    public Task<MarketingCampaign?> FindAsync(Guid id, CancellationToken cancellationToken = default) => db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public void Add(MarketingCampaign campaign) => db.MarketingCampaigns.Add(campaign);
}
public sealed class OutboundMessageRepository(VumaRetailDbContext db) : IOutboundMessageRepository
{
    public Task<OutboundMessage?> FindAsync(Guid id, CancellationToken cancellationToken = default) => db.OutboundMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<OutboundMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) => db.OutboundMessages.FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
    public void Add(OutboundMessage message) => db.OutboundMessages.Add(message);
}
