using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 08 <c>inventory</c> module — locations, the stock ledger, balances, transfers
/// and stocktakes.
/// </summary>
public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the inventory repositories, the ledger poster, the stock-keeping-unit resolver, the
    /// valuation event publisher, the module's permission declaration and its core module manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Same shape as <c>AddVumaCatalog</c> — see its remarks for why self-registering
    /// <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/> here is safe.
    /// </para>
    /// <para>
    /// Does <em>not</em> call <c>AddVumaMessaging</c> with its own assembly, unlike
    /// <c>AddVumaFinance</c>: this module's handlers live in <c>VumaRetail.Application</c>, which the
    /// default scan already covers. The moment an inventory handler moves into an assembly of its own
    /// that stops being true, and the symptom is a 500 from every endpoint in the module rather than a
    /// compile error — so if you move one, add the scan here at the same time.
    /// </para>
    /// <para>
    /// Requires <c>AddVumaCatalog</c>: <see cref="StockKeepingUnitResolver"/> resolves an item or
    /// variant to the unit of measure a movement must be posted in, through catalog's published
    /// repository ports. That is the one place inventory reaches into another module, and it goes
    /// through a contract rather than a schema reference (<c>CONVENTIONS.md</c> §2).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaInventory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IStockLocationRepository, StockLocationRepository>();
        services.AddScoped<IStockLedgerRepository, StockLedgerRepository>();
        services.AddScoped<IStockBalanceRepository, StockBalanceRepository>();
        services.AddScoped<IStockTransferRepository, StockTransferRepository>();
        services.AddScoped<IStocktakeRepository, StocktakeRepository>();

        services.AddScoped<IStockKeepingUnitResolver, StockKeepingUnitResolver>();
        services.AddScoped<IStockLedgerPoster, StockLedgerPoster>();

        // Resolved at build time rather than declared statically: a host that wired finance gets real
        // journals, and one that did not gets the logging fallback instead of a container that fails
        // to build. The alternative — always registering the financial publisher — would turn "this
        // host has no finance module" into an unresolved-dependency exception at the first stock
        // receipt, which is a worse way to find out.
        services.TryAddScoped<IInventoryValuationEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingInventoryValuationEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialInventoryValuationEventPublisher>(provider));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, InventoryPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, InventoryModuleManifest>());

        return services;
    }
}
