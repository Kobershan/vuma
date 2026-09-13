#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Service;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 23 service-management persistence and application seams.</summary>
public static class ServiceServiceCollectionExtensions
{
    public static IServiceCollection AddVumaServiceManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IServiceRepository, ServiceRepository>();
        return services;
    }
}
