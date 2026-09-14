using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Quality;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class QualityServiceCollectionExtensions
{
    public static IServiceCollection AddVumaQuality(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInspectionPlanRepository, InspectionPlanRepository>();
        services.AddScoped<IQualityCertificateRepository, QualityCertificateRepository>();
        services.AddScoped<IRecallCaseRepository, RecallCaseRepository>();
        services.AddScoped<IQualityHoldRepository, QualityHoldRepository>();
        services.AddScoped<IQualityDispatchGate, QualityDispatchGate>();
        services.AddScoped<IInspectionResultRepository, InspectionResultRepository>();
        services.AddScoped<INonConformanceRepository, NonConformanceRepository>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, QualityPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, QualityModuleManifest>());
        return services;
    }
}
