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

public sealed class CommerceBasketRepository(VumaRetailDbContext context) : ICommerceBasketRepository
{
    public Task<CommerceBasket?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.CommerceBaskets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public void Add(CommerceBasket basket) => context.CommerceBaskets.Add(basket);
}

public sealed class CommerceBasketLineRepository(VumaRetailDbContext context) : ICommerceBasketLineRepository
{
    public async Task<IReadOnlyList<CommerceBasketLine>> ListForBasketAsync(Guid basketId, CancellationToken cancellationToken = default)
        => await context.CommerceBasketLines.AsNoTracking().Where(x => x.BasketId == basketId).ToListAsync(cancellationToken).ConfigureAwait(false);
    public void Add(CommerceBasketLine line) => context.CommerceBasketLines.Add(line);
}

public sealed class CheckoutIntentRepository(VumaRetailDbContext context) : ICheckoutIntentRepository
{
    public Task<CheckoutIntent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.CheckoutIntents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<CheckoutIntent?> FindByIdempotencyKeyAsync(string ownerKey, string idempotencyKey, CancellationToken cancellationToken = default)
        => context.CheckoutIntents.FirstOrDefaultAsync(x => x.OwnerKey == ownerKey.Trim() && x.IdempotencyKey == idempotencyKey.Trim(), cancellationToken);

    public void Add(CheckoutIntent intent) => context.CheckoutIntents.Add(intent);
}

public sealed class PaymentAttemptRepository(VumaRetailDbContext context) : IPaymentAttemptRepository
{
    public Task<PaymentAttempt?> FindByEventIdAsync(string eventId, CancellationToken cancellationToken = default)
        => context.PaymentAttempts.FirstOrDefaultAsync(x => x.EventId == eventId.Trim(), cancellationToken);

    public void Add(PaymentAttempt attempt) => context.PaymentAttempts.Add(attempt);
}
