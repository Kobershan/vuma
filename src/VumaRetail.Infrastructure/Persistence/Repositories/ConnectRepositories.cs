#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class TradingConnectionRepository(VumaRetailDbContext context) : ITradingConnectionRepository
{
    public Task<TradingConnection?> FindAsync(Guid id, CancellationToken cancellationToken = default) => context.TradingConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<TradingConnection?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => context.TradingConnections.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && (x.SupplierTenantId == tenantId || x.RetailerTenantId == tenantId) && x.DeletedAt == null, cancellationToken);
    public async Task<IReadOnlyList<TradingConnection>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => await context.TradingConnections.IgnoreQueryFilters().Where(x => (x.SupplierTenantId == tenantId || x.RetailerTenantId == tenantId) && x.DeletedAt == null).AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    public void Add(TradingConnection connection) => context.TradingConnections.Add(connection);
}

public sealed class ConnectionCodeRepository(VumaRetailDbContext context) : IConnectionCodeRepository
{
    public Task<ConnectionCode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) => context.ConnectionCodes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == code.Trim().ToUpper() && x.DeletedAt == null, cancellationToken);
    public void Add(ConnectionCode code) => context.ConnectionCodes.Add(code);
}

public sealed class CataloguePublicationRepository(VumaRetailDbContext context) : ICataloguePublicationRepository
{
    public void Add(CataloguePublication publication) => context.CataloguePublications.Add(publication);
    public Task<CataloguePublication?> FindAsync(Guid id, CancellationToken cancellationToken = default) => context.CataloguePublications.Include(x => x.Lines).IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
    public async Task<IReadOnlyList<CataloguePublication>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => await context.CataloguePublications.Include(x => x.Lines).IgnoreQueryFilters().Where(x => x.ConnectionId == connectionId && x.DeletedAt == null).OrderByDescending(x => x.Version).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class PriceProposalRepository(VumaRetailDbContext context) : IPriceProposalRepository
{
    public void Add(PriceProposal proposal) => context.PriceProposals.Add(proposal);
    public Task<PriceProposal?> FindAsync(Guid id, CancellationToken cancellationToken = default) => context.PriceProposals.Include(x => x.Lines).IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
    public async Task<IReadOnlyList<PriceProposal>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => await context.PriceProposals.Include(x => x.Lines).IgnoreQueryFilters().Where(x => x.ConnectionId == connectionId && x.DeletedAt == null).OrderByDescending(x => x.EffectiveFrom).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class ConnectOrderRepository(VumaRetailDbContext context) : IConnectOrderRepository
{
    public Task<ConnectOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ConnectOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ConnectOrder?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => context.ConnectOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == id && (x.RetailerTenantId == tenantId || x.SupplierTenantId == tenantId), cancellationToken);

    public async Task<IReadOnlyList<ConnectOrder>> ListForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
        => await context.ConnectOrders.Include(x => x.Lines).Where(x => x.ConnectionId == connectionId)
            .OrderByDescending(x => x.SubmittedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    public async Task<IReadOnlyList<ConnectOrder>> ListForTenantAsync(Guid tenantId, Guid? connectionId = null, CancellationToken cancellationToken = default)
        => await context.ConnectOrders.Include(x => x.Lines)
            .Where(x => (x.RetailerTenantId == tenantId || x.SupplierTenantId == tenantId) &&
                        (connectionId == null || x.ConnectionId == connectionId.Value))
            .OrderByDescending(x => x.SubmittedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(ConnectOrder order) => context.ConnectOrders.Add(order);
}

public sealed class ConnectRemittanceRepository(VumaRetailDbContext context) : IConnectRemittanceRepository
{
    public async Task<SettlementResult?> FindSettlementAsync(Guid paymentId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        ConnectRemittanceAdvice? row = await context.ConnectRemittances.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PaymentId == paymentId && x.TenantId == tenantId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new SettlementResult(ConnectPaymentStatus.Captured, row.RemittanceReference);
    }
    public void Add(ConnectRemittanceAdvice remittance) => context.ConnectRemittances.Add(remittance);
}

public sealed class ConnectClaimRepository(VumaRetailDbContext context) : IConnectClaimRepository
{
    public Task<ConnectDeliveryClaim?> FindForTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => context.ConnectClaims.SingleOrDefaultAsync(x => x.Id == id &&
            (x.RetailerTenantId == tenantId || x.SupplierTenantId == tenantId), cancellationToken);
    public void Add(ConnectDeliveryClaim claim) => context.ConnectClaims.Add(claim);
}
