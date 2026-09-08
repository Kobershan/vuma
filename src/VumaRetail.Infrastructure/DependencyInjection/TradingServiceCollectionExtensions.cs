using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the mixed-basket trading session (Stage 09b).</summary>
public static class TradingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the session repository, the company-scope gateway, the completion and return
    /// sagas, and the module's permission declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaFinance</c>, <c>AddVumaInventory</c>, <c>AddVumaPos</c> and
    /// <c>AddVumaSales</c>.</b> Completion legs post journals through a company-bound posting
    /// engine, hold stock through <c>IReservationService</c>, and write the till sale, the tax
    /// invoice and the receipt each segment needs — all contracts rather than schema
    /// references (CONVENTIONS.md §2).
    /// </remarks>
    public static IServiceCollection AddVumaTradingSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ITradingSessionRepository, TradingSessionRepository>();
        services.AddScoped<ITradingCompanyGateway, TradingCompanyGateway>();
        services.AddScoped<IMixedBasketCompletionService, MixedBasketCompletionService>();
        services.AddScoped<IMixedBasketReturnService, MixedBasketReturnService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, TradingSessionPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, TradingModuleManifest>());

        return services;
    }
}
