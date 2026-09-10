using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 16 BOM setup.</summary>
public static class ManufacturingServiceCollectionExtensions
{
    /// <summary>Registers the BOM repository, module manifest, and permissions.</summary>
    public static IServiceCollection AddVumaManufacturing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IBillOfMaterialsRepository, BillOfMaterialsRepository>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ManufacturingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ManufacturingModuleManifest>());
        return services;
    }
}
