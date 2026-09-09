using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Planning;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Planning;
using VumaRetail.Application.Planning.Commands;
using VumaRetail.Application.Planning.Forecasting;
using VumaRetail.Application.Planning.Hosting;
using VumaRetail.Application.Planning.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Planning;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers merchandise planning: forecasting, replenishment and markdowns (Stage 15).</summary>
public static class PlanningServiceCollectionExtensions
{
    /// <summary>
    /// Registers the repositories, the pure engines, the downstream writers, the scheduled-run
    /// building blocks and the module's permission declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaInventory</c>, <c>AddVumaProcurement</c>, <c>AddVumaSales</c>,
    /// <c>AddVumaWorkflow</c> and the registry services.</b> Suggestions raise requisitions and
    /// transfers, markdowns activate promotions, approvals go through Stage 05, availability and
    /// links come from 08c/06e — all through published ports and commands, never schema references
    /// (CONVENTIONS.md §2). Usage metering needs no registration: the daily rollup counts the
    /// <c>planning</c> schema's audit trail by itself (R10: counts only, no business data).
    /// </remarks>
    public static IServiceCollection AddVumaPlanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDemandHistoryRepository, DemandHistoryRepository>();
        services.AddScoped<IDemandForecastRepository, DemandForecastRepository>();
        services.AddScoped<IReplenishmentParameterRepository, ReplenishmentParameterRepository>();
        services.AddScoped<IAbcXyzClassificationRepository, AbcXyzClassificationRepository>();
        services.AddScoped<ISafetyStockCalculationRepository, SafetyStockCalculationRepository>();
        services.AddScoped<IOpenToBuyBudgetRepository, OpenToBuyBudgetRepository>();
        services.AddScoped<IReplenishmentSuggestionRepository, ReplenishmentSuggestionRepository>();
        services.AddScoped<IMarkdownPlanRepository, MarkdownPlanRepository>();
        services.AddScoped<IDemandHistorySource, DemandHistorySource>();

        services.AddSingleton<IForecastStrategy, MovingAverageForecastStrategy>();
        services.AddSingleton<IForecastStrategy, SeasonalNaiveForecastStrategy>();
        services.AddSingleton<IForecastStrategy, ExponentialSmoothingForecastStrategy>();
        services.AddSingleton<IForecastEngine, ForecastEngine>();
        services.AddSingleton<ISafetyStockCalculator, SafetyStockCalculator>();
        services.AddSingleton<IReplenishmentEngine, ReplenishmentEngine>();
        services.AddSingleton<IMarkdownPlanner, MarkdownPlanner>();

        services.AddScoped<IOtbCommitmentReader, OtbCommitmentReader>();
        services.AddScoped<SuggestionAcceptor>();
        services.AddScoped<MarkdownActivator>();
        services.AddScoped<IPlanningProcurementWriter, PlanningProcurementWriter>();
        services.AddScoped<IPlanningTransferWriter, PlanningTransferWriter>();
        services.AddScoped<IPlanningPromotionWriter, PlanningPromotionWriter>();
        services.AddScoped<IPlanningPriceReader, PlanningPriceReader>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, PlanningPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, PlanningModuleManifest>());

        return services;
    }

    /// <summary>
    /// Registers the scheduled passes: nightly rollup, weekly forecast, daily replenishment and
    /// daily markdown activation. Separated because the passes need the installation tenant.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="host">The tenant the passes run as.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaPlanningScheduling(
        this IServiceCollection services, PlanningHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHostedService<DemandHistoryRollupHostedService>();
        services.AddHostedService<ForecastRunHostedService>();
        services.AddHostedService<ReplenishmentRunHostedService>();
        services.AddHostedService<MarkdownActivationHostedService>();

        return services;
    }
}
