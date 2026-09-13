using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 16 BOM setup and Stage 17 manufacturing execution.</summary>
public static class ManufacturingServiceCollectionExtensions
{
    /// <summary>Registers manufacturing repositories, module manifest, and permissions.</summary>
    public static IServiceCollection AddVumaManufacturing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IBillOfMaterialsRepository, BillOfMaterialsRepository>();
        services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
        services.TryAddScoped<IProductionAccountingEventPublisher>(provider =>
            provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingProductionAccountingEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialProductionAccountingEventPublisher>(provider));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ManufacturingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ManufacturingModuleManifest>());
        return services;
    }
}
