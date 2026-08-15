using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Partners;
using VumaRetail.Application.Partners.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the Stage 06 <c>partners</c> module — suppliers, customers, and partners who are both.</summary>
public static class PartnersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the partner repository, its permission declaration and its core module manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>Same shape as <c>AddVumaCatalog</c> — see its remarks for why self-registration is safe.</remarks>
    public static IServiceCollection AddVumaPartners(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPartnerRepository, PartnerRepository>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, PartnerPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, PartnersModuleManifest>());

        return services;
    }
}
