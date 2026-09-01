using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders.Queries;

/// <summary>One order line, with its allocation and fulfilment computed live from Stage 13 (business rule 1 and 4).</summary>
public sealed record SalesOrderLineResult(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity RequestedQuantity,
    Money UnitPrice,
    Money DiscountAmount,
    Money TaxAmount,
    Guid? PriceListId,
    string PromotionsSummary,
    Quantity BackorderedQuantity,
    SalesOrderLineStatus LineStatus,
    Quantity AllocatedQuantity,
    Quantity FulfilledQuantity);

/// <summary>An order and every line on it, as read back out.</summary>
public sealed record SalesOrderResult(
    Guid Id,
    string OrderNumber,
    Guid? PartnerId,
    SalesChannel Channel,
    OrderFulfilmentType FulfilmentType,
    Guid FulfillingLocationId,
    Address? DeliveryAddress,
    SalesOrderStatus Status,
    OrderPaymentStatus PaymentStatus,
    Guid? SettlingSaleId,
    Guid? SettlingCustomerAccountId,
    string Currency,
    DateTimeOffset OrderDate,
    DateTimeOffset? RequestedFulfilmentDate,
    bool IsRevenueRecognised,
    Money Net,
    Money Tax,
    Money Gross,
    IReadOnlyList<SalesOrderLineResult> Lines);

/// <summary>Reads an order, with each line's allocation and fulfilment computed live.</summary>
/// <param name="SalesOrderId">The order.</param>
public sealed record GetOrderQuery(Guid SalesOrderId) : IQuery<SalesOrderResult>;

/// <summary>Reads the order.</summary>
/// <param name="orders">Order lookup.</param>
/// <param name="fulfilment">Stage 14's one read seam into Stage 13's state.</param>
public sealed class GetOrderQueryHandler(ISalesOrderRepository orders, IOrderFulfilmentReader fulfilment)
    : IQueryHandler<GetOrderQuery, SalesOrderResult>
{
    /// <inheritdoc />
    public async Task<SalesOrderResult> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        SalesOrder order = await orders.FindAsync(query.SalesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order", query.SalesOrderId);

        return await ToResultAsync(order, fulfilment, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<SalesOrderResult> ToResultAsync(
        SalesOrder order, IOrderFulfilmentReader fulfilment, CancellationToken cancellationToken)
    {
        List<SalesOrderLineResult> lines = [];

        foreach (SalesOrderLine line in order.Lines)
        {
            OrderLineFulfilmentSnapshot snapshot = await fulfilment
                .GetLineFulfilmentAsync(line.Id, line.RequestedQuantity.UnitOfMeasure, cancellationToken)
                .ConfigureAwait(false);

            lines.Add(new SalesOrderLineResult(
                line.Id, line.ItemId, line.ItemVariantId, line.RequestedQuantity, line.UnitPrice, line.DiscountAmount,
                line.TaxAmount, line.PriceListId, line.PromotionsSummary, line.BackorderedQuantity, line.LineStatus,
                snapshot.AllocatedQuantity, snapshot.FulfilledQuantity));
        }

        return new SalesOrderResult(
            order.Id, order.OrderNumber, order.PartnerId, order.Channel, order.FulfilmentType, order.FulfillingLocationId,
            order.DeliveryAddress, order.Status, order.PaymentStatus, order.SettlingSaleId, order.SettlingCustomerAccountId,
            order.Currency, order.OrderDate, order.RequestedFulfilmentDate, order.IsRevenueRecognised, order.Net, order.Tax,
            order.Gross, lines);
    }
}

/// <summary>An order, summarised for a list screen — no per-line fulfilment read (that is <see cref="GetOrderQuery"/>'s job).</summary>
public sealed record SalesOrderSummaryResult(
    Guid Id,
    string OrderNumber,
    Guid? PartnerId,
    SalesChannel Channel,
    OrderFulfilmentType FulfilmentType,
    SalesOrderStatus Status,
    OrderPaymentStatus PaymentStatus,
    string Currency,
    DateTimeOffset OrderDate,
    Money Net,
    Money Tax,
    Money Gross);

/// <summary>A page of orders, newest first, optionally narrowed.</summary>
/// <param name="Status">The status to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Channel">The channel to narrow to, or <c>null</c> for all of them.</param>
/// <param name="PartnerId">The customer to narrow to, or <c>null</c> for all of them.</param>
/// <param name="FromDate">The earliest <c>OrderDate</c> to include, or <c>null</c>.</param>
/// <param name="ToDate">The latest <c>OrderDate</c> to include, or <c>null</c>.</param>
/// <param name="Limit">How many to return, clamped by <c>Paging</c>.</param>
/// <param name="After">The opaque cursor, or <c>null</c> for the first page.</param>
public sealed record ListOrdersQuery(
    SalesOrderStatus? Status,
    SalesChannel? Channel,
    Guid? PartnerId,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    int? Limit,
    string? After) : IQuery<PageResult<SalesOrderSummaryResult>>;

/// <summary>Reads the page.</summary>
/// <param name="orders">Order lookup.</param>
public sealed class ListOrdersQueryHandler(ISalesOrderRepository orders)
    : IQueryHandler<ListOrdersQuery, PageResult<SalesOrderSummaryResult>>
{
    /// <inheritdoc />
    public async Task<PageResult<SalesOrderSummaryResult>> HandleAsync(
        ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<SalesOrder> items, bool hasMore) = await orders
            .ListPageAsync(
                query.Status, query.Channel, query.PartnerId, query.FromDate, query.ToDate,
                KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return OrdersPages.From(items, hasMore, ToSummary);
    }

    internal static SalesOrderSummaryResult ToSummary(SalesOrder order) => new(
        order.Id, order.OrderNumber, order.PartnerId, order.Channel, order.FulfilmentType, order.Status,
        order.PaymentStatus, order.Currency, order.OrderDate, order.Net, order.Tax, order.Gross);
}

/// <summary>One backordered line, across every order — the queue <c>ReattemptBackorderedAllocationsCommand</c> processes.</summary>
public sealed record BackorderedLineResult(
    Guid SalesOrderId,
    string OrderNumber,
    DateTimeOffset OrderDate,
    Guid SalesOrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity BackorderedQuantity);

/// <summary>Every backordered line, across every order, oldest order first (business rule 3).</summary>
public sealed record ListBackorderedLinesQuery : IQuery<IReadOnlyList<BackorderedLineResult>>;

/// <summary>Lists them.</summary>
/// <param name="orders">Order lookup — oldest-first backordered order listing.</param>
public sealed class ListBackorderedLinesQueryHandler(ISalesOrderRepository orders)
    : IQueryHandler<ListBackorderedLinesQuery, IReadOnlyList<BackorderedLineResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BackorderedLineResult>> HandleAsync(
        ListBackorderedLinesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<SalesOrder> backordered = await orders.ListBackorderedOrdersAsync(cancellationToken).ConfigureAwait(false);

        List<BackorderedLineResult> results = [];

        foreach (SalesOrder order in backordered)
        {
            foreach (SalesOrderLine line in order.Lines.Where(candidate => !candidate.BackorderedQuantity.IsZero))
            {
                results.Add(new BackorderedLineResult(
                    order.Id, order.OrderNumber, order.OrderDate, line.Id, line.ItemId, line.ItemVariantId, line.BackorderedQuantity));
            }
        }

        return results;
    }
}

/// <summary>One line coming back, as read back out.</summary>
public sealed record SalesOrderReturnLineResult(
    Guid Id,
    Guid SalesOrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Quantity FulfilledQuantity,
    Money UnitPrice,
    Money Net,
    Money Tax,
    Money Gross,
    OrderStockReturnStatus StockReturn,
    Guid? StockLedgerEntryId,
    string? StockReturnNote);

/// <summary>An order return and every line on it, as read back out.</summary>
public sealed record SalesOrderReturnResult(
    Guid Id,
    Guid SalesOrderId,
    string ReturnNumber,
    string Reason,
    Guid AuthorisedByUserId,
    SalesOrderReturnStatus Status,
    OrderPaymentStatus RefundStatus,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset RaisedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<SalesOrderReturnLineResult> Lines);

/// <summary>Reads a return, with its lines.</summary>
/// <param name="SalesOrderReturnId">The return.</param>
public sealed record GetOrderReturnQuery(Guid SalesOrderReturnId) : IQuery<SalesOrderReturnResult>;

/// <summary>Reads the return.</summary>
/// <param name="returns">Return lookup.</param>
public sealed class GetOrderReturnQueryHandler(ISalesOrderReturnRepository returns)
    : IQueryHandler<GetOrderReturnQuery, SalesOrderReturnResult>
{
    /// <inheritdoc />
    public async Task<SalesOrderReturnResult> HandleAsync(GetOrderReturnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        SalesOrderReturn orderReturn = await returns.FindAsync(query.SalesOrderReturnId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrdersNotFoundException("sales order return", query.SalesOrderReturnId);

        return ToResult(orderReturn);
    }

    internal static SalesOrderReturnResult ToResult(SalesOrderReturn orderReturn) => new(
        orderReturn.Id, orderReturn.SalesOrderId, orderReturn.ReturnNumber, orderReturn.Reason, orderReturn.AuthorisedByUserId,
        orderReturn.Status, orderReturn.RefundStatus, orderReturn.Net, orderReturn.Tax, orderReturn.Gross, orderReturn.RaisedAt,
        orderReturn.CompletedAt,
        [.. orderReturn.Lines.Select(line => new SalesOrderReturnLineResult(
            line.Id, line.SalesOrderLineId, line.ItemId, line.ItemVariantId, line.Quantity, line.FulfilledQuantity, line.UnitPrice,
            line.Net, line.Tax, line.Gross, line.StockReturn, line.StockLedgerEntryId, line.StockReturnNote))]);
}

/// <summary>A page of returns raised within a period, newest first.</summary>
/// <param name="From">The period's start, inclusive.</param>
/// <param name="To">The period's end, inclusive.</param>
/// <param name="Limit">How many to return, clamped by <c>Paging</c>.</param>
/// <param name="After">The opaque cursor, or <c>null</c> for the first page.</param>
public sealed record ListOrderReturnsQuery(DateTimeOffset From, DateTimeOffset To, int? Limit, string? After)
    : IQuery<PageResult<SalesOrderReturnResult>>;

/// <summary>Reads the page.</summary>
/// <param name="returns">Return lookup.</param>
public sealed class ListOrderReturnsQueryHandler(ISalesOrderReturnRepository returns)
    : IQueryHandler<ListOrderReturnsQuery, PageResult<SalesOrderReturnResult>>
{
    /// <inheritdoc />
    public async Task<PageResult<SalesOrderReturnResult>> HandleAsync(
        ListOrderReturnsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<SalesOrderReturn> items, bool hasMore) = await returns
            .ListForPeriodAsync(query.From, query.To, KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return OrdersPages.From(items, hasMore, GetOrderReturnQueryHandler.ToResult);
    }
}

/// <summary>
/// Builds a keyset page for the orders module. Every list here sorts newest-<c>CreatedAt</c>-first with
/// the id as tiebreaker, so the cursor is always built the same way (the same reasoning
/// <c>ProcurementPages</c> gives).
/// </summary>
internal static class OrdersPages
{
    /// <summary>Builds the page, with a cursor when there is more to come.</summary>
    /// <typeparam name="TEntity">The entity paged.</typeparam>
    /// <typeparam name="TResult">The item type returned.</typeparam>
    /// <param name="items">The rows.</param>
    /// <param name="hasMore">Whether at least one more row exists.</param>
    /// <param name="project">How to turn a row into a returned item.</param>
    internal static PageResult<TResult> From<TEntity, TResult>(
        IReadOnlyList<TEntity> items, bool hasMore, Func<TEntity, TResult> project)
        where TEntity : Domain.Entities.Entity
    {
        string? cursor = hasMore && items.Count > 0
            ? new KeysetCursor(
                items[^1].CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                items[^1].Id).Encode()
            : null;

        return new PageResult<TResult>([.. items.Select(project)], cursor, hasMore);
    }
}
