#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Marketing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class MarketingServiceCollectionExtensions
{
    public static IServiceCollection AddVumaMarketing(this IServiceCollection services)
    { services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, MarketingPermissions>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, MarketingModuleManifest>()); services.AddScoped<IMarketingCampaignRepository, MarketingCampaignRepository>(); services.AddScoped<IOutboundMessageRepository, OutboundMessageRepository>(); return services; }
}
