using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Orders;
using VumaRetail.Domain.Orders;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ISalesOrderRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Every method that returns an aggregate a caller might write to loads its lines with it, for the
/// reason <c>PurchaseOrderRepository</c> gives: an aggregate whose children are missing quietly behaves
/// as though it had none.
/// </remarks>
public sealed class SalesOrderRepository(VumaRetailDbContext context) : ISalesOrderRepository
{
    /// <inheritdoc />
    public Task<SalesOrder?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
        => context.SalesOrders
            .Include(order => order.Lines)
            .FirstOrDefaultAsync(order => order.Id == salesOrderId, cancellationToken);

    /// <inheritdoc />
    public Task<SalesOrder?> FindByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        string normalized = orderNumber.Trim().ToUpperInvariant();

        return context.SalesOrders
            .Include(order => order.Lines)
            .FirstOrDefaultAsync(order => order.OrderNumber.ToUpper() == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<SalesOrder> Orders, bool HasMore)> ListPageAsync(
        SalesOrderStatus? status,
        SalesChannel? channel,
        Guid? partnerId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Lines deliberately not loaded on the list page — a list screen shows numbers, statuses and
        // totals, and loading every line for every order to render that is the classic way a list
        // endpoint becomes the slowest thing in a module.
        IQueryable<SalesOrder> query = context.SalesOrders.AsNoTracking();

        if (status is { } narrowedStatus)
        {
            query = query.Where(order => order.Status == narrowedStatus);
        }

        if (channel is { } narrowedChannel)
        {
            query = query.Where(order => order.Channel == narrowedChannel);
        }

        if (partnerId is { } narrowedPartner)
        {
            query = query.Where(order => order.PartnerId == narrowedPartner);
        }

        if (fromDate is { } from)
        {
            query = query.Where(order => order.OrderDate >= from);
        }

        if (toDate is { } to)
        {
            query = query.Where(order => order.OrderDate <= to);
        }

        query = OrdersPaging.ApplyNewestFirstCursor(query, after);

        return await OrdersPaging.TakePageAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesOrder>> ListBackorderedOrdersAsync(CancellationToken cancellationToken = default)
    {
        // A join against sales_order_lines rather than order.Lines.Any(...) — a correlated subquery
        // over a complex property inside a private-backing-field collection is more than EF Core 9's
        // translator needs to be asked to prove, and this is exactly as correct and easier to read.
        IQueryable<Guid> backorderedOrderIds = context.SalesOrderLines
            .AsNoTracking()
            .Where(line => line.BackorderedQuantity.Value > 0m)
            .Select(line => line.SalesOrderId)
            .Distinct();

        return await context.SalesOrders
            .Include(order => order.Lines)
            .Where(order => backorderedOrderIds.Contains(order.Id))
            .OrderBy(order => order.OrderDate)
            .ThenBy(order => order.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(SalesOrder order) => context.SalesOrders.Add(order);
}

/// <summary>EF Core implementation of <see cref="ISalesOrderReturnRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class SalesOrderReturnRepository(VumaRetailDbContext context) : ISalesOrderReturnRepository
{
    /// <inheritdoc />
    public Task<SalesOrderReturn?> FindAsync(Guid salesOrderReturnId, CancellationToken cancellationToken = default)
        => context.SalesOrderReturns
            .Include(orderReturn => orderReturn.Lines)
            .FirstOrDefaultAsync(orderReturn => orderReturn.Id == salesOrderReturnId, cancellationToken);

    /// <inheritdoc />
    public async Task<decimal> SumReturnedQuantityAsync(Guid salesOrderLineId, CancellationToken cancellationToken = default)
        => await context.SalesOrderReturnLines
            .AsNoTracking()
            .Where(line => line.SalesOrderLineId == salesOrderLineId)
            .SumAsync(line => (decimal?)line.Quantity.Value, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    /// <inheritdoc />
    public async Task<(IReadOnlyList<SalesOrderReturn> Returns, bool HasMore)> ListForPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, KeysetCursor? after, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<SalesOrderReturn> query = context.SalesOrderReturns
            .AsNoTracking()
            .Include(orderReturn => orderReturn.Lines)
            .Where(orderReturn => orderReturn.RaisedAt >= from && orderReturn.RaisedAt <= to);

        query = OrdersPaging.ApplyNewestFirstCursor(query, after);

        return await OrdersPaging.TakePageAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(SalesOrderReturn orderReturn) => context.SalesOrderReturns.Add(orderReturn);
}

/// <summary>
/// Keyset pagination shared by the orders module's list queries — the same shape
/// <c>ProcurementPaging</c> gives, restated here rather than shared across modules because the two
/// helpers operate on entirely different entity families and a shared generic helper would only save a
/// dozen lines at the cost of a cross-module reference.
/// </summary>
internal static class OrdersPaging
{
    /// <summary>Narrows a query to everything strictly older than the cursor.</summary>
    /// <typeparam name="TEntity">The entity being paged.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="after">The cursor, or <c>null</c> for the first page.</param>
    internal static IQueryable<TEntity> ApplyNewestFirstCursor<TEntity>(IQueryable<TEntity> query, KeysetCursor? after)
        where TEntity : Domain.Entities.Entity
    {
        if (after is not { } cursor
            || !DateTimeOffset.TryParse(
                cursor.SortKey, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset createdAt))
        {
            return query;
        }

        return query.Where(entity => entity.CreatedAt < createdAt
            || (entity.CreatedAt == createdAt && entity.Id.CompareTo(cursor.Id) < 0));
    }

    /// <summary>Orders newest first, takes one more than asked for, and reports whether there was one.</summary>
    /// <typeparam name="TEntity">The entity being paged.</typeparam>
    /// <param name="query">The query, already narrowed.</param>
    /// <param name="limit">How many the caller asked for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    internal static async Task<(IReadOnlyList<TEntity> Items, bool HasMore)> TakePageAsync<TEntity>(
        IQueryable<TEntity> query, int limit, CancellationToken cancellationToken)
        where TEntity : Domain.Entities.Entity
    {
        List<TEntity> page = await query
            .OrderByDescending(entity => entity.CreatedAt)
            .ThenByDescending(entity => entity.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return (page, hasMore);
    }
}
