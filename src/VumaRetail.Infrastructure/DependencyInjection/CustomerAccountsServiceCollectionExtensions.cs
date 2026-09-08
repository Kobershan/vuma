using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.CustomerAccounts.Hosting;
using VumaRetail.Application.CustomerAccounts.Permissions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the <c>customer-accounts</c> module — Stage 10b credit, lay-by and stokvels.</summary>
public static class CustomerAccountsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the account and lay-by repositories, the expiry and interest hosted services, and
    /// the module's permission declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaFinance</c>, <c>AddVumaInventory</c> and <c>AddVumaPos</c>.</b> Handlers
    /// post journals through <c>IFinancialEventPoster</c>, hold stock through
    /// <c>IReservationService</c> and resolve prices through POS's catalogue — all contracts rather
    /// than schema references (CONVENTIONS.md §2). Unlike returns, an account payment or lay-by
    /// instalment with nowhere to post is not a trade that must survive misconfiguration, so there
    /// is no logging fallback here: a host without finance fails fast at the first posting.
    /// </remarks>
    public static IServiceCollection AddVumaCustomerAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IAccountHolderRepository, AccountHolderRepository>();
        services.AddScoped<ICustomerFinanceTermsRepository, CustomerFinanceTermsRepository>();
        services.AddScoped<ILayByAgreementRepository, LayByAgreementRepository>();

        services.AddHostedService<LayByExpiryHostedService>();
        services.AddHostedService<AccountInterestHostedService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, CustomerAccountsPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, CustomerAccountsModuleManifest>());

        return services;
    }
}
