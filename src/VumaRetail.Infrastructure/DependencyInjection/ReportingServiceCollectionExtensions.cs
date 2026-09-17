using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Reporting;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Reporting;
using VumaRetail.Infrastructure.Security;
using VumaRetail.Infrastructure.Reporting;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddVumaReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IReportingRepository, ReportingRepository>();
        services.TryAddScoped<IReportExportDownloadAuthorizer, ReportExportDownloadAuthorizer>();
        services.TryAddSingleton<IReportExporter, CsvReportExporter>();
        services.TryAddSingleton<IReportArtifactStore, FileSystemReportArtifactStore>();
        services.TryAddSingleton<ReportArtifactStoreOptions>();
        services.TryAddScoped<ReportExportExecutor>();
        services.TryAddScoped<IReportScheduleRunner, ReportScheduleRunner>();
        services.TryAddScoped<IReportDataSource, DashboardReportDataSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ReportingPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ReportingModuleManifest>());
        return services;
    }

    public static IServiceCollection AddVumaReportingScheduling(
        this IServiceCollection services, ReportingHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        services.AddSingleton(host);
        services.AddOptions<ReportSchedulingOptions>()
            .BindConfiguration(ReportSchedulingOptions.SectionName);
        services.AddHostedService<ReportSchedulingHostedService>();
        return services;
    }
}
