using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Projects;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class ProjectsServiceCollectionExtensions
{
    public static IServiceCollection AddVumaProjects(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IProjectRepository, ProjectRepository>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ProjectPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ProjectModuleManifest>());
        return services;
    }
}
