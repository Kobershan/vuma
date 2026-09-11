#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

public sealed record ListConnectConnectionsQuery : IQuery<IReadOnlyList<ConnectConnectionResult>>;
public sealed class ListConnectConnectionsQueryHandler(ITradingConnectionRepository connections, ITenantContext tenant) : IQueryHandler<ListConnectConnectionsQuery, IReadOnlyList<ConnectConnectionResult>>
{
    public async Task<IReadOnlyList<ConnectConnectionResult>> HandleAsync(ListConnectConnectionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return (await connections.ListForTenantAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false)).Select(RedeemConnectionCodeCommandHandler.ToResult).ToList();
    }
}

public sealed record GetConnectPublicationQuery(Guid Id) : IQuery<ConnectPublicationResult?>;
public sealed class GetConnectPublicationQueryHandler(ICataloguePublicationRepository publications, ITradingConnectionRepository connections, ITenantContext tenant) : IQueryHandler<GetConnectPublicationQuery, ConnectPublicationResult?>
{
    public async Task<ConnectPublicationResult?> HandleAsync(GetConnectPublicationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CataloguePublication? p = await publications.FindAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (p is null)
        {
            return null;
        }
        TradingConnection? c = await connections.FindForTenantAsync(p.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (c is null)
        {
            throw new UnauthorizedAccessException("Tenant is not a party to this connection.");
        }
        return new(p.Id, p.ConnectionId, p.Version, p.EffectiveFrom, p.RolledBackAt, p.Lines.Select(x => new ConnectPublicationLineResult(x.Id, x.SupplierSku, x.Description, x.Barcode, x.PackSize, x.MinimumOrderQuantity, x.LeadTimeDays)).ToList());
    }
}

public sealed record ListIncomingPriceProposalsQuery(Guid ConnectionId) : IQuery<IReadOnlyList<ConnectProposalResult>>;
public sealed class ListIncomingPriceProposalsQueryHandler(IPriceProposalRepository proposals, ITradingConnectionRepository connections, ITenantContext tenant) : IQueryHandler<ListIncomingPriceProposalsQuery, IReadOnlyList<ConnectProposalResult>>
{
    public async Task<IReadOnlyList<ConnectProposalResult>> HandleAsync(ListIncomingPriceProposalsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        TradingConnection? c = await connections.FindForTenantAsync(query.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (c is null || c.RetailerTenantId != tenant.TenantId)
        {
            throw new UnauthorizedAccessException("Only the connected retailer can view incoming proposals.");
        }
        return (await proposals.ListForConnectionAsync(query.ConnectionId, cancellationToken).ConfigureAwait(false)).Select(p => new ConnectProposalResult(p.Id, p.ConnectionId, p.EffectiveFrom, p.ExpiresAt, p.Status, p.Lines.Select(x => new ConnectProposalLineResult(x.Id, x.SupplierSku, x.UnitPrice, x.Currency, x.MinimumOrderQuantity, x.LeadTimeDays)).ToList())).ToList();
    }
}
