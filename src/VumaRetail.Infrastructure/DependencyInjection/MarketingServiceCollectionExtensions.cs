#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Marketing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class MarketingServiceCollectionExtensions
{
    public static IServiceCollection AddVumaMarketing(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, MarketingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, MarketingModuleManifest>());
        services.AddScoped<IMarketingCampaignRepository, MarketingCampaignRepository>();
        services.AddScoped<IOutboundMessageRepository, OutboundMessageRepository>();
        services.AddScoped<MarketingDeliveryPolicy>();
        services.AddScoped<MarketingDeliveryService>();
        services.AddScoped<IMarketingTransport, UnavailableMarketingTransport>();
        return services;
    }
}

internal sealed class UnavailableMarketingTransport : IMarketingTransport
{
    public Task<MarketingTransportResult> SendAsync(OutboundMessage message, MarketingCampaign campaign,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No marketing provider transport is configured.");
}
