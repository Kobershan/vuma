#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Connect;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Connect;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class ConnectServiceCollectionExtensions
{
    public static IServiceCollection AddVumaConnect(this IServiceCollection services)
    {
        services.AddScoped<ITradingConnectionRepository, TradingConnectionRepository>();
        services.AddScoped<IConnectionCodeRepository, ConnectionCodeRepository>();
        services.AddScoped<ICataloguePublicationRepository, CataloguePublicationRepository>();
        services.AddScoped<IPriceProposalRepository, PriceProposalRepository>();
        services.AddScoped<IConnectOrderRepository, ConnectOrderRepository>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ConnectPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ConnectModuleManifest>());
        services.TryAddScoped<IPaymentGateway, InMemoryConnectPaymentGateway>();
        services.TryAddScoped<ISettlementProvider, InMemoryConnectSettlementProvider>();
        services.TryAddScoped<IConnectLedgerPoster, InMemoryConnectLedgerPoster>();
        services.AddScoped<IConnectRemittanceRepository, ConnectRemittanceRepository>();
        services.AddScoped<IConnectClaimRepository, ConnectClaimRepository>();
        return services;
    }
}
