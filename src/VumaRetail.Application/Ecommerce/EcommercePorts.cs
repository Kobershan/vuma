#pragma warning disable CS1591
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;

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

public interface ICommerceBasketRepository
{
    void Add(CommerceBasket basket);
}

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenBasketCommand(Guid ChannelId, Guid CompanyId, string OwnerKey) : ICommand<Guid>;

public sealed class OpenBasketCommandHandler(
    IChannelConnectionRepository channels, ICommerceBasketRepository baskets, ITenantContext tenant, ICompanyContext company, IClock clock)
    : ICommandHandler<OpenBasketCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenBasketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "basket");
        ChannelConnection channel = await channels.FindAsync(command.ChannelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.CompanyId != command.CompanyId || channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active for the selected company.");
        }
        CommerceBasket basket = CommerceBasket.Open(tenant.TenantId, command.CompanyId, channel.Id, command.OwnerKey, clock.UtcNow);
        baskets.Add(basket);
        return basket.Id;
    }
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

[CommandSideEffect(SideEffect.Write)]
public sealed record RegisterChannelCommand(Guid CompanyId, string Code, string Host) : ICommand<Guid>;

public sealed class RegisterChannelCommandHandler(IChannelConnectionRepository channels, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<RegisterChannelCommand, Guid>
{
    public Task<Guid> HandleAsync(RegisterChannelCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCompany(company, command.CompanyId, "channel");
        ChannelConnection channel = ChannelConnection.Register(tenant.TenantId, command.CompanyId, command.Code, command.Host);
        channel.Activate();
        channels.Add(channel);
        return Task.FromResult(channel.Id);
    }

    internal static void EnsureCompany(ICompanyContext company, Guid expected, string resource)
    {
        if (company.CompanyId is not { } active || active != expected)
        {
            throw new InvalidOperationException($"The {resource} company is not the active company.");
        }
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishProductCommand(Guid ChannelId, Guid CompanyId, Guid? ItemId, Guid? ItemVariantId,
    string Sku, string Name, string? Description, decimal Price, string Currency, decimal Available,
    DateTimeOffset AvailabilityAsAt, int Version) : ICommand<Guid>;

public sealed class PublishProductCommandHandler(
    IChannelConnectionRepository channels, IPublishedProductRepository products, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<PublishProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(PublishProductCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "product");
        // The repository is tenant-filtered, and the company check prevents a caller from using an
        // otherwise valid channel id while another company is active.
        ChannelConnection channel = await channels.FindAsync(command.ChannelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.CompanyId != command.CompanyId || channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active for the selected company.");
        }
        PublishedProduct product = PublishedProduct.Publish(tenant.TenantId, command.CompanyId, channel.Id,
            command.ItemId, command.ItemVariantId, command.Sku, command.Name, command.Description, command.Price,
            command.Currency, command.Available, command.AvailabilityAsAt, command.Version);
        products.Add(product);
        return product.Id;
    }
}
