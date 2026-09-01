using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 09 <c>pos</c> module — till sessions, sales, tenders, receipts and the cash-up.
/// </summary>
public static class PosServiceCollectionExtensions
{
    /// <summary>
    /// Registers the POS repositories, the sale completion service, the sellable-item resolver, the
    /// financial event publisher, the module's permission declaration and its manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Same shape as <c>AddVumaInventory</c> — see its remarks for why self-registering
    /// <see cref="IModulePermissions"/> and <see cref="IModuleManifest"/> here is safe, and why this
    /// module needs no <c>AddVumaMessaging</c> call of its own (its handlers live in
    /// <c>VumaRetail.Application</c>, which the default scan already covers).
    /// </para>
    /// <para>
    /// <b>Requires <c>AddVumaCatalog</c> and <c>AddVumaInventory</c>.</b> POS resolves what it is
    /// selling through catalog's published ports and relieves stock through inventory's ledger poster.
    /// Both are contracts rather than schema references (<c>CONVENTIONS.md</c> §2).
    /// </para>
    /// <para>
    /// <b>Requires <c>AddVumaFinance</c> for tax.</b> Unlike the financial event publisher below, the
    /// tax calculator has no meaningful fallback: a till that could not price VAT would have to either
    /// invent a rate — which <c>CLAUDE.md</c> §9 forbids outright — or sell everything zero-rated,
    /// which is worse than failing to start. So <see cref="ITaxCalculator"/> is a hard dependency and
    /// a host that forgets Finance finds out when the container is built rather than at the first sale.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaPos(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ITillSessionRepository, TillSessionRepository>();
        services.AddScoped<IReceiptPrintRepository, ReceiptPrintRepository>();

        // The receipt is a tax document and must carry the seller's VAT number, which lives on the
        // tenant. Registered here rather than in a platform extension because Stage 09 is what needed
        // it; TryAdd so a later platform registration wins without this one having to be removed.
        services.TryAddScoped<ITenantRepository, TenantRepository>();

        services.AddScoped<ISellableItemResolver, SellableItemResolver>();
        services.AddScoped<ISaleCompletionService, SaleCompletionService>();

        // Resolved at build time rather than declared statically, exactly as AddVumaInventory does it:
        // a host that wired finance gets real journals, and one that did not gets the logging fallback
        // instead of a container that fails to build at the first sale.
        services.TryAddScoped<ISaleFinancialEventPublisher>(provider
            => provider.GetService<IFinancialEventPoster>() is null
                ? ActivatorUtilities.CreateInstance<LoggingSaleEventPublisher>(provider)
                : ActivatorUtilities.CreateInstance<FinancialSaleEventPublisher>(provider));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, PosPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, PosModuleManifest>());

        return services;
    }
}
