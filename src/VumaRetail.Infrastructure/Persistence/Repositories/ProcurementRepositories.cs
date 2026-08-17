using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IPurchaseRequisitionRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Every method that returns an aggregate a caller might write to loads its lines with it, for the
/// reason <c>SaleRepository</c> gives: an aggregate whose children are missing quietly behaves as
/// though it had none — and here that would mean <c>Submit</c> refusing a requisition that has lines,
/// or worse, an order recomputing its totals from an empty collection.
/// </remarks>
public sealed class PurchaseRequisitionRepository(VumaRetailDbContext context)
    : IPurchaseRequisitionRepository
{
    /// <inheritdoc />
    public Task<PurchaseRequisition?> FindAsync(
        Guid requisitionId, CancellationToken cancellationToken = default)
        => context.PurchaseRequisitions
            .Include(requisition => requisition.Lines)
            .FirstOrDefaultAsync(requisition => requisition.Id == requisitionId, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequisition?> FindByLineAsync(
        Guid requisitionLineId, CancellationToken cancellationToken = default)
    {
        Guid? parentId = await context.PurchaseRequisitionLines
            .AsNoTracking()
            .Where(line => line.Id == requisitionLineId)
            .Select(line => (Guid?)line.PurchaseRequisitionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return parentId is { } id
            ? await FindAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PurchaseRequisition> Requisitions, bool HasMore)> ListPageAsync(
        PurchaseRequisitionStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PurchaseRequisition> query = context.PurchaseRequisitions
            .AsNoTracking()
            .Include(requisition => requisition.Lines);

        if (status is { } narrowed)
        {
            query = query.Where(requisition => requisition.Status == narrowed);
        }

        query = ProcurementPaging.ApplyNewestFirstCursor(query, after);

        return await ProcurementPaging
            .TakePageAsync(query, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(PurchaseRequisition requisition) => context.PurchaseRequisitions.Add(requisition);
}

/// <summary>EF Core implementation of <see cref="IRfqRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class RfqRepository(VumaRetailDbContext context) : IRfqRepository
{
    /// <inheritdoc />
    public Task<Rfq?> FindAsync(Guid rfqId, CancellationToken cancellationToken = default)
        => context.Rfqs
            .Include(rfq => rfq.Lines)
            .Include(rfq => rfq.Responses)
                .ThenInclude(response => response.Lines)
            .FirstOrDefaultAsync(rfq => rfq.Id == rfqId, cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Rfq> Rfqs, bool HasMore)> ListPageAsync(
        RfqStatus? status, KeysetCursor? after, int limit, CancellationToken cancellationToken = default)
    {
        // Lines and responses deliberately not loaded on the list page. A list screen shows numbers,
        // titles and statuses; loading every quote for every RFQ to render that is the classic way a
        // list endpoint becomes the slowest thing in a module.
        IQueryable<Rfq> query = context.Rfqs.AsNoTracking();

        if (status is { } narrowed)
        {
            query = query.Where(rfq => rfq.Status == narrowed);
        }

        query = ProcurementPaging.ApplyNewestFirstCursor(query, after);

        return await ProcurementPaging.TakePageAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(Rfq rfq) => context.Rfqs.Add(rfq);
}

/// <summary>EF Core implementation of <see cref="IPurchaseOrderRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class PurchaseOrderRepository(VumaRetailDbContext context) : IPurchaseOrderRepository
{
    /// <inheritdoc />
    public Task<PurchaseOrder?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
        => context.PurchaseOrders
            .Include(order => order.Lines)
            .FirstOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

    /// <inheritdoc />
    public Task<PurchaseOrder?> FindByNumberAsync(
        string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        string normalized = orderNumber.Trim();

        return context.PurchaseOrders
            .Include(order => order.Lines)
            .FirstOrDefaultAsync(order => order.OrderNumber == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PurchaseOrder> Orders, bool HasMore)> ListPageAsync(
        Guid? partnerId,
        PurchaseOrderStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PurchaseOrder> query = context.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines);

        if (partnerId is { } supplier)
        {
            query = query.Where(order => order.PartnerId == supplier);
        }

        if (status is { } narrowed)
        {
            query = query.Where(order => order.Status == narrowed);
        }

        query = ProcurementPaging.ApplyNewestFirstCursor(query, after);

        return await ProcurementPaging.TakePageAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrder>> ListForSupplierPeriodAsync(
        Guid partnerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await context.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => order.PartnerId == partnerId)

            // The expected date, not the raised date. An order placed in March for April delivery is
            // April's performance — see ISupplierScorecardCalculator's remarks.
            .Where(order => order.ExpectedAt >= from && order.ExpectedAt <= to)
            .OrderBy(order => order.ExpectedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(PurchaseOrder order) => context.PurchaseOrders.Add(order);
}

/// <summary>EF Core implementation of <see cref="IGoodsReceiptRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class GoodsReceiptRepository(VumaRetailDbContext context) : IGoodsReceiptRepository
{
    /// <inheritdoc />
    public Task<GoodsReceipt?> FindAsync(Guid goodsReceiptId, CancellationToken cancellationToken = default)
        => context.GoodsReceipts
            .Include(receipt => receipt.Lines)
            .FirstOrDefaultAsync(receipt => receipt.Id == goodsReceiptId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> ListForOrderAsync(
        Guid purchaseOrderId, CancellationToken cancellationToken = default)
        => await context.GoodsReceipts
            .AsNoTracking()
            .Include(receipt => receipt.Lines)
            .Where(receipt => receipt.PurchaseOrderId == purchaseOrderId)
            .OrderBy(receipt => receipt.ReceivedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> ListForOrdersAsync(
        IReadOnlyCollection<Guid> purchaseOrderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(purchaseOrderIds);

        if (purchaseOrderIds.Count == 0)
        {
            return [];
        }

        List<Guid> ids = [.. purchaseOrderIds];

        return await context.GoodsReceipts
            .AsNoTracking()
            .Include(receipt => receipt.Lines)
            .Where(receipt => ids.Contains(receipt.PurchaseOrderId))
            .OrderBy(receipt => receipt.ReceivedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> ListWithRefusedStockAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken = default)
    {
        DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset end = new(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await context.GoodsReceipts
            .AsNoTracking()
            .Include(receipt => receipt.Lines)
            .Where(receipt => receipt.ReceivedAt >= start && receipt.ReceivedAt < end)
            .Where(receipt => receipt.Lines.Any(line
                => line.StockPosting == GoodsReceiptPostingStatus.Refused))
            .OrderByDescending(receipt => receipt.ReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(GoodsReceipt receipt) => context.GoodsReceipts.Add(receipt);
}

/// <summary>EF Core implementation of <see cref="ISupplierInvoiceMatchRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class SupplierInvoiceMatchRepository(VumaRetailDbContext context)
    : ISupplierInvoiceMatchRepository
{
    /// <inheritdoc />
    public Task<SupplierInvoiceMatch?> FindAsync(Guid matchId, CancellationToken cancellationToken = default)
        => context.SupplierInvoiceMatches
            .Include(match => match.Lines)
            .FirstOrDefaultAsync(match => match.Id == matchId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierInvoiceMatch>> ListForOrderAsync(
        Guid purchaseOrderId, CancellationToken cancellationToken = default)
        => await context.SupplierInvoiceMatches
            .AsNoTracking()
            .Include(match => match.Lines)
            .Where(match => match.PurchaseOrderId == purchaseOrderId)
            .OrderBy(match => match.MatchedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierInvoiceMatch>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        ThreeWayMatchStatus? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SupplierInvoiceMatch> query = context.SupplierInvoiceMatches
            .AsNoTracking()
            .Include(match => match.Lines)
            .Where(match => match.InvoiceDate >= from && match.InvoiceDate <= to);

        if (status is { } narrowed)
        {
            query = query.Where(match => match.Status == narrowed);
        }

        return await query
            .OrderByDescending(match => match.MatchedAt)
            .ThenByDescending(match => match.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> ExistsForInvoiceNumberAsync(
        Guid purchaseOrderId, string supplierInvoiceNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierInvoiceNumber);

        string normalized = supplierInvoiceNumber.Trim();

        return context.SupplierInvoiceMatches
            .AsNoTracking()
            .AnyAsync(
                match => match.PurchaseOrderId == purchaseOrderId
                    && match.SupplierInvoiceNumber == normalized,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<decimal> SumInvoicedQuantityAsync(
        Guid purchaseOrderLineId, Guid? excludingMatchId, CancellationToken cancellationToken = default)
    {
        // Released matches only, and this is the deliberate opposite of ISalesReturnRepository's
        // equivalent. An unreleased match is a bill nobody has accepted — very often the blocked one
        // somebody is about to replace with a corrected invoice — and counting it would block the
        // correction as well.
        IQueryable<SupplierInvoiceMatchLine> query = context.SupplierInvoiceMatchLines
            .AsNoTracking()
            .Where(line => line.PurchaseOrderLineId == purchaseOrderLineId)
            .Where(line => context.SupplierInvoiceMatches
                .Any(match => match.Id == line.SupplierInvoiceMatchId && match.ReleasedAt != null));

        if (excludingMatchId is { } excluded)
        {
            query = query.Where(line => line.SupplierInvoiceMatchId != excluded);
        }

        return await query
            .SumAsync(line => line.InvoicedQuantity.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(SupplierInvoiceMatch match) => context.SupplierInvoiceMatches.Add(match);
}

/// <summary>EF Core implementation of <see cref="ISupplierScorecardRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class SupplierScorecardRepository(VumaRetailDbContext context) : ISupplierScorecardRepository
{
    /// <inheritdoc />
    public Task<SupplierScorecard?> FindAsync(
        Guid partnerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
        => context.SupplierScorecards.FirstOrDefaultAsync(
            scorecard => scorecard.PartnerId == partnerId
                && scorecard.PeriodStart == periodStart
                && scorecard.PeriodEnd == periodEnd,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierScorecard>> ListForSupplierAsync(
        Guid partnerId, int limit, CancellationToken cancellationToken = default)
        => await context.SupplierScorecards
            .AsNoTracking()
            .Where(scorecard => scorecard.PartnerId == partnerId)
            .OrderByDescending(scorecard => scorecard.PeriodStart)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierScorecard>> ListForPeriodAsync(
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
        => await context.SupplierScorecards
            .AsNoTracking()
            .Where(scorecard => scorecard.PeriodStart == periodStart && scorecard.PeriodEnd == periodEnd)
            .OrderByDescending(scorecard => scorecard.OverallRating)
            .ThenByDescending(scorecard => scorecard.PurchaseValue.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(SupplierScorecard scorecard) => context.SupplierScorecards.Add(scorecard);
}

/// <summary>
/// The newest-first keyset page every procurement list endpoint uses, written once
/// (<c>docs/API_STANDARDS.md</c> §8).
/// </summary>
/// <remarks>
/// Four repositories page the same way over four different entities. Written per repository the cursor
/// comparison would be right the first three times and subtly wrong the fourth — and a keyset
/// comparison that drops the tie-break repeats or skips a row exactly when two documents were created
/// in the same tick, which is precisely what a bulk import or a busy goods-in desk produces.
/// </remarks>
internal static class ProcurementPaging
{
    /// <summary>Narrows a query to everything strictly older than the cursor.</summary>
    /// <typeparam name="TEntity">The entity being paged.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="after">The cursor, or <c>null</c> for the first page.</param>
    internal static IQueryable<TEntity> ApplyNewestFirstCursor<TEntity>(
        IQueryable<TEntity> query, KeysetCursor? after)
        where TEntity : Domain.Entities.Entity
    {
        if (after is not { } cursor
            || !DateTimeOffset.TryParse(
                cursor.SortKey,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset createdAt))
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
