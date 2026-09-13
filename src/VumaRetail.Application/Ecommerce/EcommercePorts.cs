#pragma warning disable CS1591
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Application.Ecommerce;

/// <summary>Persistence boundary for storefront channel registrations.</summary>
public interface IChannelConnectionRepository
{
    Task<ChannelConnection?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChannelConnection?> FindByHostAsync(string host, CancellationToken cancellationToken = default);
    void Add(ChannelConnection connection);
}

/// <summary>Persistence boundary for published storefront products.</summary>
public interface IPublishedProductRepository
{
    Task<IReadOnlyList<PublishedProduct>> ListAsync(Guid channelConnectionId, int limit, CancellationToken cancellationToken = default);
    void Add(PublishedProduct product);
}

/// <summary>Public sell-facing catalogue result; never exposes domain persistence objects.</summary>
public sealed record StorefrontProductResult(Guid Id, string Sku, string Name, string? Description,
    decimal Price, string Currency, decimal Available, DateTimeOffset AvailabilityAsAt, int Version);

public sealed record ListStorefrontProductsQuery(string Host, int Limit = 50)
    : IQuery<IReadOnlyList<StorefrontProductResult>>;

public sealed class ListStorefrontProductsQueryHandler(
    IChannelConnectionRepository channels, IPublishedProductRepository products)
    : IQueryHandler<ListStorefrontProductsQuery, IReadOnlyList<StorefrontProductResult>>
{
    public async Task<IReadOnlyList<StorefrontProductResult>> HandleAsync(
        ListStorefrontProductsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Host);
        int limit = Math.Clamp(query.Limit, 1, 200);
        ChannelConnection channel = await channels.FindByHostAsync(query.Host, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active.");
        }
        return (await products.ListAsync(channel.Id, limit, cancellationToken).ConfigureAwait(false))
            .Select(x => new StorefrontProductResult(x.Id, x.Sku, x.Name, x.Description, x.Price, x.Currency,
                x.Available, x.AvailabilityAsAt, x.Version)).ToList();
    }
}
