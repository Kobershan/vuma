using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Procurement;
using VumaRetail.Application.Procurement.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 12 <c>procurement</c> module — requisitions, RFQs, purchase orders, goods
/// receipts, the three-way match and supplier scorecards.
/// </summary>
public static class ProcurementServiceCollectionExtensions
{
    /// <summary>
    /// Registers the procurement repositories, the three services, the financial event publisher, the
    /// module's permission declaration and its manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="options">The tenant's tolerances, or <c>null</c> for the conservative defaults.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Same shape as <c>AddVumaSales</c> — see its remarks for why self-registering
    /// <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/> here is safe, and why this
    /// module needs no <c>AddVumaMessaging</c> call of its own.
    /// </para>
    /// <para>
    /// <b>Requires <c>AddVumaInventory</c>, <c>AddVumaPartners</c> and <c>AddVumaFinance</c>.</b> The
    /// first two are hard: a goods receipt posts stock through inventory's ledger poster, and every
    /// document validates its supplier through the partner repository. Finance is <em>almost</em> hard
    /// and deliberately not — <see cref="ITaxCalculator"/> is needed to author an order line
    /// (ADR-075), so a host without it cannot raise an order, but the journal publisher still falls
    /// back to the logging implementation rather than failing the container. That asymmetry is
    /// intentional: not being able to price tax is a configuration error worth failing on, while not
    /// being able to post a journal is ADR-070's documented degradation.
    /// </para>
    /// <para>
    /// <see cref="ProcurementOptions"/> is registered as a singleton object rather than through
    /// <c>IOptions&lt;T&gt;</c>, exactly as <c>ImportOptions</c> is, so <c>VumaRetail.Application</c>
    /// needs no dependency on the options package.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaProcurement(
        this IServiceCollection services, ProcurementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(options ?? new ProcurementOptions());

        services.AddScoped<IPurchaseRequisitionRepository, PurchaseRequisitionRepository>();
        services.AddScoped<IRfqRepository, RfqRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
        services.AddScoped<ISupplierInvoiceMatchRepository, SupplierInvoiceMatchRepository>();
        services.AddScoped<ISupplierScorecardRepository, SupplierScorecardRepository>();

        services.AddScoped<IThreeWayMatchEngine, ThreeWayMatchEngine>();
        services.AddScoped<IGoodsReceiptCompletionService, GoodsReceiptCompletionService>();
        services.AddScoped<ISupplierScorecardCalculator, SupplierScorecardCalculator>();

        // Resolved at build time rather than declared statically, exactly as AddVumaSales does it: a
        // host that wired finance gets real journals, and one that did not gets the logging fallback
        // instead of a container that fails to build at the first released invoice.
        services.TryAddScoped<IProcurementFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingProcurementEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialProcurementEventPublisher>(provider));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ProcurementPermissions>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModuleManifest, ProcurementModuleManifest>());

        return services;
    }
}
