using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Finance.Hosting;
using VumaRetail.Finance.Periods;
using VumaRetail.Finance.Permissions;
using VumaRetail.Finance.Posting;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the Stage 07 <c>finance</c> module — GL, AR, AP, banking, tax, posting rules.</summary>
public static class FinanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Finance repository, the posting rules engine, the tax engine, the period
    /// variance checker, the module's permission declaration and its core module manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Same shape as <c>AddVumaCatalog</c>/<c>AddVumaPartners</c> — see the remarks on
    /// <c>AddVumaCatalog</c> for why self-registering <see cref="IModulePermissions"/> and
    /// <see cref="IModuleManifest"/> here, rather than in a central list, is safe. Does not register
    /// <see cref="FinanceReconciliationHostedService"/> — that needs a host tenant the caller supplies,
    /// so it is <see cref="AddVumaFinanceReconciliation"/>, called separately once the host knows which
    /// tenant it is running as (the same split <c>AddVumaControlPlaneClient</c> uses for
    /// <c>LicensingHostTenant</c>).
    /// </remarks>
    public static IServiceCollection AddVumaFinance(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountingPeriodRepository, AccountingPeriodRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IPostingRuleRepository, PostingRuleRepository>();
        services.AddScoped<IArInvoiceRepository, ArInvoiceRepository>();
        services.AddScoped<IArReceiptRepository, ArReceiptRepository>();
        services.AddScoped<IApInvoiceRepository, ApInvoiceRepository>();
        services.AddScoped<IApPaymentRepository, ApPaymentRepository>();
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IBankStatementLineRepository, BankStatementLineRepository>();
        services.AddScoped<ITaxRuleRepository, TaxRuleRepository>();
        services.AddScoped<IReconciliationVarianceFlagRepository, ReconciliationVarianceFlagRepository>();
        services.AddScoped<IDocumentNumberSequence, DocumentNumberSequence>();

        // The posting rules engine is the single door onto the GL (CLAUDE.md §7 rule 12); every future
        // producer module depends on IFinancialEventPoster only, never on PostingRuleEngine itself.
        services.AddScoped<IFinancialEventPoster, PostingRuleEngine>();
        services.AddScoped<TaxEngine>();

        // ITaxCalculator is the same instance under its published name. Stage 07's own AR and AP
        // commands take the concrete TaxEngine because they live in this assembly; a module outside
        // Finance cannot, so Stage 09 added the port and this line is what makes it resolvable.
        // Forwarded rather than registered separately, so a host cannot end up with two tax engines
        // reading the same rules and no way to tell which one answered.
        services.AddScoped<ITaxCalculator>(provider => provider.GetRequiredService<TaxEngine>());

        services.AddScoped<PeriodVarianceChecker>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, FinancePermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, FinanceModuleManifest>());

        // The finance handlers and validators live in VumaRetail.Finance, which the Application-only
        // scan does not reach — the same reason AddVumaSync scans VumaRetail.Sync. Without this every
        // finance command and query resolves to "no handler is registered" at dispatch time, which
        // means a 500 from every endpoint in the module: the code is all present and simply
        // unreachable. Nothing below the host catches that, because a unit test constructs its
        // handler directly and the endpoint tests assert the route table rather than the container.
        services.AddVumaMessaging(typeof(VumaRetail.Finance.AssemblyMarker).Assembly);

        return services;
    }

    /// <summary>
    /// Registers <see cref="FinanceReconciliationHostedService"/> — ADR-016's "automated daily job"
    /// (ADR-063).
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="host">The tenant and store this host's background pass runs as.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaFinanceReconciliation(
        this IServiceCollection services, FinanceHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHostedService<FinanceReconciliationHostedService>();

        return services;
    }
}
