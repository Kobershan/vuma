#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 21 Ecommerce services as the vertical is introduced.</summary>
public static class EcommerceServiceCollectionExtensions
{
    public static IServiceCollection AddVumaEcommerce(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, EcommercePermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, EcommerceModuleManifest>());
        services.AddScoped<IChannelConnectionRepository, ChannelConnectionRepository>();
        services.AddScoped<IPublishedProductRepository, PublishedProductRepository>();
        services.AddScoped<ICommerceBasketRepository, CommerceBasketRepository>();
        services.AddScoped<ICommerceBasketLineRepository, CommerceBasketLineRepository>();
        return services;
    }
}
