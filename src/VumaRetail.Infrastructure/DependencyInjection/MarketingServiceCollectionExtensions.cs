#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;
using VumaRetail.Infrastructure.Marketing;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class MarketingServiceCollectionExtensions
{
    public static IServiceCollection AddVumaMarketing(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, MarketingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, MarketingModuleManifest>());
        services.AddScoped<IMarketingCampaignRepository, MarketingCampaignRepository>();
        services.AddScoped<IOutboundMessageRepository, OutboundMessageRepository>();
        services.AddScoped<IJourneyDefinitionRepository, JourneyDefinitionRepository>();
        services.AddScoped<IJourneyEnrollmentRepository, JourneyEnrollmentRepository>();
        services.AddScoped<IAttributionEventRepository, AttributionEventRepository>();
        services.AddScoped<MarketingDeliveryPolicy>();
        services.AddScoped<MarketingDeliveryService>();
        services.AddScoped<IMarketingDeliveryWorker, MarketingDeliveryWorker>();
        services.AddOptions<MarketingTransportOptions>()
            .BindConfiguration(MarketingTransportOptions.SectionName);
        services.AddHttpClient<IMarketingTransport, HttpMarketingTransport>();
        return services;
    }

    public static IServiceCollection AddVumaMarketingScheduling(
        this IServiceCollection services, MarketingHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        services.AddSingleton(host);
        services.AddOptions<MarketingDeliveryOptions>()
            .BindConfiguration(MarketingDeliveryOptions.SectionName);
        services.AddHostedService<MarketingDeliveryHostedService>();
        return services;
    }
}
