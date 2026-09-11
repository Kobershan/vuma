#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

public interface ITradingConnectionRepository
{
    Task<TradingConnection?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TradingConnection?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradingConnection>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    void Add(TradingConnection connection);
}

public interface IConnectionCodeRepository
{
    Task<ConnectionCode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
    void Add(ConnectionCode code);
}

public interface ICataloguePublicationRepository
{
    void Add(CataloguePublication publication);
    Task<CataloguePublication?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguePublication>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

public interface IPriceProposalRepository
{
    void Add(PriceProposal proposal);
    Task<PriceProposal?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceProposal>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

public interface IConnectOrderRepository
{
    Task<ConnectOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConnectOrder?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectOrder>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectOrder>> ListForTenantAsync(Guid tenantId, Guid? connectionId = null, CancellationToken cancellationToken = default);
    void Add(ConnectOrder order);
}

public interface IConnectClaimRepository
{
    Task<ConnectDeliveryClaim?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(ConnectDeliveryClaim claim);
}

public sealed record ConnectConnectionResult(Guid Id, Guid SupplierTenantId, Guid RetailerTenantId, TradingConnectionStatus Status, string Currency, decimal CreditLimit, int LeadTimeDays, decimal MinimumOrderValue);
public sealed record ConnectCodeResult(Guid Id, string Code, DateTimeOffset ExpiresAt, int MaxUses, string? PriceTier, string? Territory, bool GrantsPortalAccess);
public sealed record ConnectPublicationResult(Guid Id, Guid ConnectionId, int Version, DateTimeOffset EffectiveFrom, DateTimeOffset? RolledBackAt, IReadOnlyList<ConnectPublicationLineResult> Lines);
public sealed record ConnectPublicationLineResult(Guid Id, string SupplierSku, string Description, string? Barcode, int PackSize, decimal? MinimumOrderQuantity, int? LeadTimeDays);
public sealed record ConnectProposalResult(Guid Id, Guid ConnectionId, DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, ConnectProposalStatus Status, IReadOnlyList<ConnectProposalLineResult> Lines);
public sealed record ConnectProposalLineResult(Guid Id, string SupplierSku, decimal UnitPrice, string Currency, decimal? MinimumOrderQuantity, int? LeadTimeDays);
