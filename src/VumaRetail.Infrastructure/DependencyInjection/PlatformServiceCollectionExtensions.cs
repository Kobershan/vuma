using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Platform;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the Stage 06 additions to the <c>platform</c> module.</summary>
/// <remarks>
/// Platform itself is wired piecemeal across stages rather than through one <c>AddVumaPlatform</c> —
/// <c>Tenant</c> and <c>Store</c> were seed-only until now (ADR-037). This is the first thing platform
/// needs a container registration for, so it gets the smallest extension that does that.
/// </remarks>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registers the store repository.</summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IStoreRepository, StoreRepository>();

        return services;
    }
}
