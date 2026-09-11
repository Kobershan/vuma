#pragma warning disable CS1591, IDE0011, CA1062
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

public sealed record ConnectOrderLineResult(Guid Id, string SupplierSku, string Description, decimal RequestedQuantity,
    decimal ConfirmedQuantity, decimal DispatchedQuantity, string UnitOfMeasure, decimal UnitPrice, string Currency);
public sealed record ConnectOrderResult(Guid Id, Guid ConnectionId, Guid RetailerTenantId, Guid SupplierTenantId,
    string OrderNumber, ConnectOrderStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? PromisedAt,
    DateTimeOffset? DispatchedAt, string? DispatchNoteNumber, IReadOnlyList<ConnectOrderLineResult> Lines);
public sealed record ListConnectOrdersQuery(Guid? ConnectionId = null) : IQuery<IReadOnlyList<ConnectOrderResult>>;

public sealed class ListConnectOrdersQueryHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : IQueryHandler<ListConnectOrdersQuery, IReadOnlyList<ConnectOrderResult>>
{
    public async Task<IReadOnlyList<ConnectOrderResult>> HandleAsync(ListConnectOrdersQuery query, CancellationToken cancellationToken = default)
        => (await orders.ListForTenantAsync(tenant.TenantId, query.ConnectionId, cancellationToken).ConfigureAwait(false))
            .Select(ToResult).ToList();

    internal static ConnectOrderResult ToResult(ConnectOrder order)
        => new(order.Id, order.ConnectionId, order.RetailerTenantId, order.SupplierTenantId, order.OrderNumber,
            order.Status, order.SubmittedAt, order.PromisedAt, order.DispatchedAt, order.DispatchNoteNumber,
            order.Lines.Select(x => new ConnectOrderLineResult(x.Id, x.SupplierSku, x.Description,
                x.RequestedQuantity.Value, x.ConfirmedQuantity.Value, x.DispatchedQuantity.Value,
                x.RequestedQuantity.UnitOfMeasure, x.UnitPrice.Amount, x.UnitPrice.Currency)).ToList());
}

public sealed record ConnectSupplierDirectoryResult(Guid ConnectionId, Guid SupplierTenantId, string Currency,
    decimal CreditLimit, int LeadTimeDays, decimal MinimumOrderValue, IReadOnlyList<ConnectPublicationLineResult> Catalogue);
public sealed record ListConnectSuppliersQuery : IQuery<IReadOnlyList<ConnectSupplierDirectoryResult>>;

public sealed class ListConnectSuppliersQueryHandler(
    ITradingConnectionRepository connections, ICataloguePublicationRepository catalogues, ITenantContext tenant)
    : IQueryHandler<ListConnectSuppliersQuery, IReadOnlyList<ConnectSupplierDirectoryResult>>
{
    public async Task<IReadOnlyList<ConnectSupplierDirectoryResult>> HandleAsync(ListConnectSuppliersQuery query, CancellationToken cancellationToken = default)
    {
        List<ConnectSupplierDirectoryResult> result = [];
        foreach (TradingConnection connection in await connections.ListForTenantAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false))
        {
            if (connection.Status != TradingConnectionStatus.Active || connection.RetailerTenantId != tenant.TenantId) continue;
            CataloguePublication? publication = (await catalogues.ListForConnectionAsync(connection.Id, cancellationToken).ConfigureAwait(false))
                .Where(x => x.TenantId == connection.SupplierTenantId && x.RolledBackAt is null)
                .OrderByDescending(x => x.Version).FirstOrDefault();
            result.Add(new(connection.Id, connection.SupplierTenantId, connection.Currency, connection.CreditLimit,
                connection.LeadTimeDays, connection.MinimumOrderValue,
                publication?.Lines.Select(x => new ConnectPublicationLineResult(x.Id, x.SupplierSku, x.Description,
                    x.Barcode, x.PackSize, x.MinimumOrderQuantity, x.LeadTimeDays)).ToList() ?? []));
        }
        return result;
    }
}
