#pragma warning disable CS1591, IDE0011, CA1062
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

public sealed record ConnectOrderLineResult(Guid Id, string SupplierSku, string Description, decimal RequestedQuantity,
    decimal ConfirmedQuantity, decimal DispatchedQuantity, string UnitOfMeasure, decimal UnitPrice, string Currency,
    Guid? PurchaseOrderLineId, string? BatchNumber, string? SerialNumbers, DateOnly? ExpiryDate, string? PackageReference);
public sealed record ConnectOrderResult(Guid Id, Guid ConnectionId, Guid RetailerTenantId, Guid SupplierTenantId,
    string OrderNumber, ConnectOrderStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? PromisedAt,
    DateTimeOffset? DispatchedAt, string? DispatchNoteNumber, IReadOnlyList<ConnectOrderLineResult> Lines);
public sealed record ListConnectOrdersQuery(Guid? ConnectionId = null) : IQuery<IReadOnlyList<ConnectOrderResult>>;
public sealed record GetConnectAsnQuery(Guid OrderId) : IQuery<ConnectOrderResult?>;

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
                x.RequestedQuantity.UnitOfMeasure, x.UnitPrice.Amount, x.UnitPrice.Currency, x.PurchaseOrderLineId,
                x.BatchNumber, x.SerialNumbers, x.ExpiryDate, x.PackageReference)).ToList());
}

public sealed class GetConnectAsnQueryHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : IQueryHandler<GetConnectAsnQuery, ConnectOrderResult?>
{
    public async Task<ConnectOrderResult?> HandleAsync(GetConnectAsnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ConnectOrder? order = await orders.FindForTenantAsync(query.OrderId, tenant.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        if (order.Status is not (ConnectOrderStatus.Dispatched or ConnectOrderStatus.Received))
        {
            return null;
        }

        return ListConnectOrdersQueryHandler.ToResult(order);
    }
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

public sealed record ListSupplierPortalGrantsQuery(Guid ConnectionId) : IQuery<IReadOnlyList<SupplierPortalGrantResult>>;
public sealed record SupplierPortalGrantResult(Guid Id, Guid ConnectionId, Guid RetailerTenantId,
    Guid SupplierTenantId, Guid ContactId, string AccessRole, DateTimeOffset GrantedAt, DateTimeOffset? RevokedAt);

public sealed class ListSupplierPortalGrantsQueryHandler(
    ISupplierPortalGrantRepository grants, ITradingConnectionRepository connections, ITenantContext tenant)
    : IQueryHandler<ListSupplierPortalGrantsQuery, IReadOnlyList<SupplierPortalGrantResult>>
{
    public async Task<IReadOnlyList<SupplierPortalGrantResult>> HandleAsync(ListSupplierPortalGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        TradingConnection connection = await connections.FindForTenantAsync(query.ConnectionId, tenant.TenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        IReadOnlyList<SupplierPortalGrant> rows = await grants.ListForConnectionAsync(query.ConnectionId,
            tenant.TenantId, cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new SupplierPortalGrantResult(x.Id, x.ConnectionId, x.RetailerTenantId,
            x.SupplierTenantId, x.ContactId, x.AccessRole, x.GrantedAt, x.RevokedAt)).ToList();
    }
}
