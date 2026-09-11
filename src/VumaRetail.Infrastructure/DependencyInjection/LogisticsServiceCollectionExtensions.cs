using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Logistics;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class LogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddVumaLogistics(this IServiceCollection services)
    {
        services.AddScoped<ILogisticsRepository, LogisticsRepository>();
        return services;
    }
}
