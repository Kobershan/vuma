using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Orders.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 14 <c>orders</c> module — sales orders, allocation, backorders, click &amp;
/// collect and order returns.
/// </summary>
public static class OrdersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the orders repositories, the fulfilment reader, the return completion service, both
    /// financial event publishers, the module's permission declaration and its manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Same shape as <c>AddVumaSales</c> and <c>AddVumaWarehouse</c> — see their remarks for why
    /// self-registering <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/> here is safe.
    /// </para>
    /// <para>
    /// <b>Requires <c>AddVumaInventory</c>, <c>AddVumaWarehouse</c>, <c>AddVumaSales</c> and
    /// <c>AddVumaPos</c>.</b> Confirming an order validates a Stage 08 location, allocates through Stage
    /// 13's own repositories and pick-allocation strategy, prices through Stage 10's
    /// <c>IPriceResolver</c>, and resolves the item through Stage 09's <c>ISellableItemResolver</c> —
    /// all contracts rather than schema references (<c>CONVENTIONS.md</c> §2).
    /// </para>
    /// <para>
    /// <b>Finance is optional, as it is for Sales.</b> Both financial event publishers are resolved at
    /// build time rather than declared statically, so a host wired without Finance still completes an
    /// order and its return — only the journal is missing, and the logging publisher says so.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaOrders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<ISalesOrderReturnRepository, SalesOrderReturnRepository>();

        services.AddScoped<IOrderFulfilmentReader, OrderFulfilmentReader>();
        services.AddScoped<IOrderReturnCompletionService, OrderReturnCompletionService>();

        services.TryAddScoped<IOrderFulfilmentEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingOrderFulfilmentEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialOrderFulfilmentEventPublisher>(provider));

        services.TryAddScoped<IOrderReturnFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingOrderReturnEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialOrderReturnEventPublisher>(provider));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, OrdersPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, OrdersModuleManifest>());

        return services;
    }
}
