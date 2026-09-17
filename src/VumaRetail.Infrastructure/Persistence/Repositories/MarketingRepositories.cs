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
    public Task<OutboundMessage?> FindByProviderEventIdAsync(Guid companyId, string providerEventId, CancellationToken cancellationToken = default)
        => db.OutboundMessages.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderEventId == providerEventId, cancellationToken);
    public async Task<IReadOnlyList<OutboundMessage>> ListQueuedAsync(Guid companyId, DateTimeOffset asAt, bool dueOnly,
        int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        IQueryable<OutboundMessage> query = db.OutboundMessages
            .Where(x => x.CompanyId == companyId && x.Status == OutboundMessageStatus.Queued);
        if (dueOnly) query = query.Where(x => x.ScheduledAt <= asAt && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= asAt));
        return await query.OrderBy(x => x.ScheduledAt).ThenBy(x => x.Id).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<OutboundMessage>> ListByStatusAsync(Guid companyId,
        IReadOnlyCollection<OutboundMessageStatus> statuses, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        if (statuses.Count == 0) return Array.Empty<OutboundMessage>();
        return await db.OutboundMessages
            .Where(x => x.CompanyId == companyId && statuses.Contains(x.Status))
            .OrderByDescending(x => x.ScheduledAt).ThenBy(x => x.Id).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
    public void Add(OutboundMessage message) => db.OutboundMessages.Add(message);
}
