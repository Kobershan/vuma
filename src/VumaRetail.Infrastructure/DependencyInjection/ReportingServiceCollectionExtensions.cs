using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Reporting;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddVumaReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IReportingRepository, ReportingRepository>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ReportingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ReportingModuleManifest>());
        return services;
    }
}
