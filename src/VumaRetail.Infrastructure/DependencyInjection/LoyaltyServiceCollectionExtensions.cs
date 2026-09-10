using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Loyalty;
using VumaRetail.Application.Loyalty.Hosting;
using VumaRetail.Application.Loyalty.Permissions;
using VumaRetail.Infrastructure.Loyalty;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers loyalty: members, earn/burn, caches, reconciliation and retry (Stage 20).</summary>
public static class LoyaltyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the repositories, the loyalty engine client, the module's permission declaration
    /// and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="useFakeOrbit">True (default) while no Proxima credentials exist.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaCrm</c></b> (marketing-gated earns check consent through Stage 19's
    /// <c>IConsentService</c>). The engine boundary defaults to the in-memory fake: deterministic,
    /// idempotent, and the gate the burn tests prove against. Real Orbit credentials are deferred
    /// (PROGRESS.md) — <c>HttpOrbitClient</c> takes the slot when they exist. Usage metering needs
    /// no registration: the daily rollup counts the <c>loyalty</c> schema's audit trail by itself
    /// (R10: counts only, no business data).
    /// </remarks>
    public static IServiceCollection AddVumaLoyalty(this IServiceCollection services, bool useFakeOrbit = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ILoyaltyMemberRepository, LoyaltyMemberRepository>();
        services.AddScoped<ILoyaltyTransactionRepository, LoyaltyTransactionRepository>();
        services.AddScoped<ILoyaltyTierRepository, LoyaltyTierRepository>();
        services.AddScoped<ILoyaltyRewardRepository, LoyaltyRewardRepository>();
        services.AddScoped<ILoyaltySettingsRepository, LoyaltySettingsRepository>();

        services.AddScoped<LoyaltyCacheWriter>();

        if (useFakeOrbit)
        {
            services.AddSingleton<IOrbitClient, InMemoryOrbitClient>();
        }
        else
        {
            services.AddHttpClient<IOrbitClient, HttpOrbitClient>();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, LoyaltyPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, LoyaltyModuleManifest>());

        return services;
    }

    /// <summary>
    /// Registers the retry pass. Separated because the pass needs the installation tenant.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="host">The tenant the pass runs as.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaLoyaltyScheduling(
        this IServiceCollection services, LoyaltyHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHostedService<LoyaltyRetryHostedService>();

        return services;
    }
}
