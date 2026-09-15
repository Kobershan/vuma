#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Assets;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Service;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Security;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 23 service-management persistence and application seams.</summary>
public static class ServiceServiceCollectionExtensions
{
    public static IServiceCollection AddVumaServiceManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IServiceRepository, ServiceRepository>();
        services.TryAddScoped<IAssetRepository>(provider => provider.GetRequiredService<ServiceRepository>());
        services.TryAddScoped<IAssetDepreciationFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingAssetDepreciationEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialAssetDepreciationEventPublisher>(provider));
        services.TryAddScoped<IChecklistRepository>(provider => provider.GetRequiredService<ServiceRepository>());
        services.TryAddScoped<IChecklistEvidenceAuthorizer, ChecklistEvidenceAuthorizer>();
        services.TryAddSingleton<IServiceSlaClock>(_ =>
            new BusinessHoursServiceSlaClock(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        services.TryAddScoped<IServiceSlaWorker, ServiceSlaWorker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, AssetPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, AssetModuleManifest>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ServicePermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ServiceModuleManifest>());
        return services;
    }
}
