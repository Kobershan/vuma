using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.FieldSales;
using VumaRetail.Infrastructure.FieldSales;
using VumaRetail.Application.FieldSales.Permissions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.FieldSales;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers field sales: reps, pro formas, approval and performance (Stage 14b).</summary>
public static class FieldSalesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the repositories, the approval saga, the availability probe and the module's
    /// permission declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaWorkflow</c>, <c>AddVumaFinance</c>, <c>AddVumaInventory</c>,
    /// <c>AddVumaOrders</c>, <c>AddVumaSales</c> and <c>AddVumaTradingSessions</c>.</b> Approval
    /// converts through the sourcing planner, the group credit service, reservations, orders and
    /// invoices — all contracts rather than schema references (CONVENTIONS.md §2).
    /// </remarks>
    public static IServiceCollection AddVumaFieldSales(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProFormaOrderRepository, ProFormaOrderRepository>();
        services.AddScoped<IProFormaCreditNoteRepository, ProFormaCreditNoteRepository>();
        services.AddScoped<IRepRepository, RepRepository>();
        services.AddScoped<IRepPerformanceRepository, RepPerformanceRepository>();
        services.AddScoped<VumaRetail.Application.Abstractions.FieldSales.IFieldSalesApprovalService, FieldSalesApprovalService>();
        services.AddScoped<VumaRetail.Application.Abstractions.FieldSales.IAvailabilityProbe, AvailabilityProbe>();
        services.AddScoped<IRepPerformanceCalculator, RepPerformanceCalculator>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, FieldSalesPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, FieldSalesModuleManifest>());

        return services;
    }

    /// <summary>
    /// Registers the scheduled passes: pro forma expiry and closed-period performance snapshots.
    /// Separated because the passes need the installation tenant.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="host">The tenant the passes run as.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaFieldSalesScheduling(
        this IServiceCollection services, FieldSalesHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHostedService<ProFormaExpiryHostedService>();
        services.AddHostedService<RepPerformanceSnapshotHostedService>();

        return services;
    }
}
