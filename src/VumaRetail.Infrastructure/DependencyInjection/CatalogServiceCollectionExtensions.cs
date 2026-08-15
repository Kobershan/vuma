using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Catalog.Permissions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the Stage 06 <c>catalog</c> module — items, variants, barcodes, units of measure.</summary>
public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalog repositories, its permission declaration and its core module manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Self-registers <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/>, the same shape
    /// <c>AddVumaLicensing</c> uses for its own module — a module's DI extension is where its permission
    /// and manifest declarations live, rather than a central list every later stage has to remember to
    /// edit. Both catalogues are resolved lazily (<c>IPermissionCatalogue</c> and the entitlement
    /// service's manifest list are built from <c>GetServices</c> the first time something asks), so
    /// registration order relative to <c>AddVumaIdentity</c>/<c>AddVumaLicensing</c> does not matter.
    /// </remarks>
    public static IServiceCollection AddVumaCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IItemVariantRepository, ItemVariantRepository>();
        services.AddScoped<IBarcodeRepository, BarcodeRepository>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, CatalogPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, CatalogModuleManifest>());

        return services;
    }
}
