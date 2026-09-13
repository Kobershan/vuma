using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Domain.Ecommerce;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class ChannelConnectionRepository(VumaRetailDbContext context) : IChannelConnectionRepository
{
    public Task<ChannelConnection?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ChannelConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ChannelConnection?> FindByHostAsync(string host, CancellationToken cancellationToken = default)
        => context.ChannelConnections.FirstOrDefaultAsync(x => x.Host == host.Trim().ToLower(), cancellationToken);

    public void Add(ChannelConnection connection) => context.ChannelConnections.Add(connection);
}

public sealed class PublishedProductRepository(VumaRetailDbContext context) : IPublishedProductRepository
{
    public async Task<IReadOnlyList<PublishedProduct>> ListAsync(Guid channelConnectionId, int limit, CancellationToken cancellationToken = default)
        => await context.PublishedProducts.AsNoTracking()
            .Where(x => x.ChannelConnectionId == channelConnectionId)
            .OrderBy(x => x.Sku).ThenByDescending(x => x.Version)
            .Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(PublishedProduct product) => context.PublishedProducts.Add(product);
}
