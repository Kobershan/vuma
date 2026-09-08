using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Sales;
using VumaRetail.Application.Sales.Permissions;
using VumaRetail.Application.Sales.Pricing;
using VumaRetail.Application.Sales.Services;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the <c>sales</c> module — Stage 10 price lists, promotions, returns and price
/// overrides, plus Stage 10c quotes, invoices and sales analytics.
/// </summary>
public static class SalesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sales repositories, the price resolver, the return completion service, the
    /// financial event publisher, the quote/invoice/analytics repositories and services, and the
    /// module's permission declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Same shape as <c>AddVumaPos</c> — see its remarks for why self-registering
    /// <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/> here is safe, and why this
    /// module needs no <c>AddVumaMessaging</c> call of its own.
    /// </para>
    /// <para>
    /// <b>Requires <c>AddVumaInventory</c> and <c>AddVumaPos</c>.</b> A completed return puts stock back
    /// through inventory's ledger poster and reads the original sale through POS's repository. Both are
    /// contracts rather than schema references (<c>CONVENTIONS.md</c> §2).
    /// </para>
    /// <para>
    /// <b>Finance is optional here, unlike tax is for POS.</b> A return has no tax to compute — its tax
    /// is derived from the original line's stored tax (ADR-075), which is the whole point — so this
    /// module never calls <see cref="ITaxCalculator"/> and a host wired without Finance still takes
    /// goods back. Only the journal is missing, and the logging publisher says so.
    /// </para>
    /// <para>
    /// Stage 10c's quote/invoice/analytics repositories sit alongside rather than replacing anything:
    /// quotes snapshot <see cref="IPriceResolver"/> output (ADR-074) and invoices carry the pack size
    /// snapshot (ADR-112); neither re-resolves.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaSales(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPriceListRepository, PriceListRepository>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<ISalesReturnRepository, SalesReturnRepository>();
        services.AddScoped<IPriceOverrideLogRepository, PriceOverrideLogRepository>();

        services.AddScoped<IPriceResolver, PriceResolver>();
        services.AddScoped<ISalesReturnCompletionService, SalesReturnCompletionService>();

        // Resolved at build time rather than declared statically, exactly as AddVumaPos does it: a host
        // that wired finance gets real journals, and one that did not gets the logging fallback instead
        // of a container that fails to build at the first refund.
        services.TryAddScoped<ISalesReturnFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingSalesReturnEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialSalesReturnEventPublisher>(provider));

        // Stage 10c (ADR-074): quotes, invoices and sales analytics.
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ISalesAnalyticsRepository, SalesAnalyticsRepository>();

        services.AddScoped<QuoteService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<AnalyticsService>();

        services.AddScoped<IPackSizeResolver, Sales.PackSizeResolver>();
        services.AddScoped<IInvoiceIssuingService, Sales.InvoiceIssuingService>();

        // A host that wired finance gets real journals for posted invoices; one that did not gets
        // the logging fallback instead of a container that fails at the first finalize.
        services.TryAddScoped<IInvoiceFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingInvoiceEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialInvoiceEventPublisher>(provider));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, SalesPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, SalesModuleManifest>());

        return services;
    }
}
