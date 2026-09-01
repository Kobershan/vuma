using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Queries;

/// <summary>One requisition, with its lines.</summary>
/// <param name="PurchaseRequisitionId">The requisition.</param>
public sealed record GetPurchaseRequisitionQuery(Guid PurchaseRequisitionId)
    : IQuery<PurchaseRequisition>;

/// <summary>Reads the requisition.</summary>
/// <param name="requisitions">Requisition lookup.</param>
public sealed class GetPurchaseRequisitionQueryHandler(IPurchaseRequisitionRepository requisitions)
    : IQueryHandler<GetPurchaseRequisitionQuery, PurchaseRequisition>
{
    /// <inheritdoc />
    public async Task<PurchaseRequisition> HandleAsync(
        GetPurchaseRequisitionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await requisitions
            .FindAsync(query.PurchaseRequisitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException(
                "purchase requisition", query.PurchaseRequisitionId);
    }
}

/// <summary>A page of requisitions, newest first.</summary>
/// <param name="Status">The status to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Limit">How many to return, clamped by <c>Paging</c>.</param>
/// <param name="After">The opaque cursor, or <c>null</c> for the first page.</param>
public sealed record ListPurchaseRequisitionsQuery(
    PurchaseRequisitionStatus? Status, int? Limit, string? After)
    : IQuery<PageResult<PurchaseRequisition>>;

/// <summary>Reads the page.</summary>
/// <param name="requisitions">Requisition lookup.</param>
public sealed class ListPurchaseRequisitionsQueryHandler(IPurchaseRequisitionRepository requisitions)
    : IQueryHandler<ListPurchaseRequisitionsQuery, PageResult<PurchaseRequisition>>
{
    /// <inheritdoc />
    public async Task<PageResult<PurchaseRequisition>> HandleAsync(
        ListPurchaseRequisitionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<PurchaseRequisition> items, bool hasMore) = await requisitions
            .ListPageAsync(query.Status, KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return ProcurementPages.From(items, hasMore, requisition => requisition);
    }
}

/// <summary>One RFQ, with its lines, its responses and their lines.</summary>
/// <param name="RfqId">The RFQ.</param>
public sealed record GetRfqQuery(Guid RfqId) : IQuery<Rfq>;

/// <summary>Reads the RFQ.</summary>
/// <param name="rfqs">RFQ lookup.</param>
public sealed class GetRfqQueryHandler(IRfqRepository rfqs) : IQueryHandler<GetRfqQuery, Rfq>
{
    /// <inheritdoc />
    public async Task<Rfq> HandleAsync(GetRfqQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await rfqs.FindAsync(query.RfqId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("RFQ", query.RfqId);
    }
}

/// <summary>A page of RFQs, newest first.</summary>
/// <param name="Status">The status to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Limit">How many to return, clamped by <c>Paging</c>.</param>
/// <param name="After">The opaque cursor, or <c>null</c> for the first page.</param>
public sealed record ListRfqsQuery(RfqStatus? Status, int? Limit, string? After)
    : IQuery<PageResult<Rfq>>;

/// <summary>Reads the page.</summary>
/// <param name="rfqs">RFQ lookup.</param>
public sealed class ListRfqsQueryHandler(IRfqRepository rfqs)
    : IQueryHandler<ListRfqsQuery, PageResult<Rfq>>
{
    /// <inheritdoc />
    public async Task<PageResult<Rfq>> HandleAsync(
        ListRfqsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<Rfq> items, bool hasMore) = await rfqs
            .ListPageAsync(query.Status, KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return ProcurementPages.From(items, hasMore, rfq => rfq);
    }
}

/// <summary>One purchase order, with its lines.</summary>
/// <param name="PurchaseOrderId">The order.</param>
public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrder>;

/// <summary>Reads the order.</summary>
/// <param name="orders">Order lookup.</param>
public sealed class GetPurchaseOrderQueryHandler(IPurchaseOrderRepository orders)
    : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrder>
{
    /// <inheritdoc />
    public async Task<PurchaseOrder> HandleAsync(
        GetPurchaseOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await orders.FindAsync(query.PurchaseOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", query.PurchaseOrderId);
    }
}

/// <summary>A page of purchase orders, newest first.</summary>
/// <param name="PartnerId">The supplier to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Status">The status to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Limit">How many to return, clamped by <c>Paging</c>.</param>
/// <param name="After">The opaque cursor, or <c>null</c> for the first page.</param>
public sealed record ListPurchaseOrdersQuery(
    Guid? PartnerId, PurchaseOrderStatus? Status, int? Limit, string? After)
    : IQuery<PageResult<PurchaseOrder>>;

/// <summary>Reads the page.</summary>
/// <param name="orders">Order lookup.</param>
public sealed class ListPurchaseOrdersQueryHandler(IPurchaseOrderRepository orders)
    : IQueryHandler<ListPurchaseOrdersQuery, PageResult<PurchaseOrder>>
{
    /// <inheritdoc />
    public async Task<PageResult<PurchaseOrder>> HandleAsync(
        ListPurchaseOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<PurchaseOrder> items, bool hasMore) = await orders
            .ListPageAsync(
                query.PartnerId, query.Status, KeysetCursor.TryDecode(query.After), limit,
                cancellationToken)
            .ConfigureAwait(false);

        return ProcurementPages.From(items, hasMore, order => order);
    }
}

/// <summary>One goods receipt, with its lines.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
public sealed record GetGoodsReceiptQuery(Guid GoodsReceiptId) : IQuery<GoodsReceipt>;

/// <summary>Reads the receipt.</summary>
/// <param name="receipts">Receipt lookup.</param>
public sealed class GetGoodsReceiptQueryHandler(IGoodsReceiptRepository receipts)
    : IQueryHandler<GetGoodsReceiptQuery, GoodsReceipt>
{
    /// <inheritdoc />
    public async Task<GoodsReceipt> HandleAsync(
        GetGoodsReceiptQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await receipts.FindAsync(query.GoodsReceiptId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("goods receipt", query.GoodsReceiptId);
    }
}

/// <summary>Every receipt against one order, oldest first.</summary>
/// <param name="PurchaseOrderId">The order.</param>
public sealed record ListGoodsReceiptsForOrderQuery(Guid PurchaseOrderId)
    : IQuery<IReadOnlyList<GoodsReceipt>>;

/// <summary>Reads the receipts.</summary>
/// <param name="receipts">Receipt lookup.</param>
public sealed class ListGoodsReceiptsForOrderQueryHandler(IGoodsReceiptRepository receipts)
    : IQueryHandler<ListGoodsReceiptsForOrderQuery, IReadOnlyList<GoodsReceipt>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> HandleAsync(
        ListGoodsReceiptsForOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await receipts
            .ListForOrderAsync(query.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Receipts that completed without their stock movement being posted (ADR-073).
/// </summary>
/// <param name="From">The first day, inclusive.</param>
/// <param name="To">The last day, inclusive.</param>
/// <param name="Limit">How many to return.</param>
/// <remarks>
/// The mirror of POS's <c>GET /pos/reconciliation/stock-issues</c>, and the same expectation applies: a
/// growing list means the shelf and the system have drifted apart, not that the feature is broken. It
/// should stay empty.
/// </remarks>
public sealed record ListRefusedStockPostingsQuery(DateOnly From, DateOnly To, int Limit)
    : IQuery<IReadOnlyList<GoodsReceipt>>;

/// <summary>Reads the reconciliation queue.</summary>
/// <param name="receipts">Receipt lookup.</param>
public sealed class ListRefusedStockPostingsQueryHandler(IGoodsReceiptRepository receipts)
    : IQueryHandler<ListRefusedStockPostingsQuery, IReadOnlyList<GoodsReceipt>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> HandleAsync(
        ListRefusedStockPostingsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await receipts
            .ListWithRefusedStockAsync(query.From, query.To, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>One three-way match, with its comparison lines.</summary>
/// <param name="SupplierInvoiceMatchId">The match.</param>
public sealed record GetSupplierInvoiceMatchQuery(Guid SupplierInvoiceMatchId)
    : IQuery<SupplierInvoiceMatch>;

/// <summary>Reads the match.</summary>
/// <param name="matches">Match lookup.</param>
public sealed class GetSupplierInvoiceMatchQueryHandler(ISupplierInvoiceMatchRepository matches)
    : IQueryHandler<GetSupplierInvoiceMatchQuery, SupplierInvoiceMatch>
{
    /// <inheritdoc />
    public async Task<SupplierInvoiceMatch> HandleAsync(
        GetSupplierInvoiceMatchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await matches
            .FindAsync(query.SupplierInvoiceMatchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException(
                "supplier invoice match", query.SupplierInvoiceMatchId);
    }
}

/// <summary>
/// Matches in a period, newest first — the accounts-payable work queue when narrowed to
/// <see cref="ThreeWayMatchStatus.Blocked"/>.
/// </summary>
/// <param name="From">The first day, inclusive.</param>
/// <param name="To">The last day, inclusive.</param>
/// <param name="Status">The verdict to narrow to, or <c>null</c> for all of them.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListSupplierInvoiceMatchesQuery(
    DateOnly From, DateOnly To, ThreeWayMatchStatus? Status, int Limit)
    : IQuery<IReadOnlyList<SupplierInvoiceMatch>>;

/// <summary>Reads the matches.</summary>
/// <param name="matches">Match lookup.</param>
public sealed class ListSupplierInvoiceMatchesQueryHandler(ISupplierInvoiceMatchRepository matches)
    : IQueryHandler<ListSupplierInvoiceMatchesQuery, IReadOnlyList<SupplierInvoiceMatch>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierInvoiceMatch>> HandleAsync(
        ListSupplierInvoiceMatchesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await matches
            .ListForPeriodAsync(query.From, query.To, query.Status, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>One supplier's scorecards, newest period first.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListSupplierScorecardsQuery(Guid PartnerId, int Limit)
    : IQuery<IReadOnlyList<SupplierScorecard>>;

/// <summary>Reads the scorecards.</summary>
/// <param name="scorecards">Scorecard lookup.</param>
public sealed class ListSupplierScorecardsQueryHandler(ISupplierScorecardRepository scorecards)
    : IQueryHandler<ListSupplierScorecardsQuery, IReadOnlyList<SupplierScorecard>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierScorecard>> HandleAsync(
        ListSupplierScorecardsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await scorecards
            .ListForSupplierAsync(query.PartnerId, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Every supplier's scorecard for one period, best rating first — the league table.</summary>
/// <param name="PeriodStart">The period's first day.</param>
/// <param name="PeriodEnd">The period's last day.</param>
public sealed record ListScorecardLeagueQuery(DateOnly PeriodStart, DateOnly PeriodEnd)
    : IQuery<IReadOnlyList<SupplierScorecard>>;

/// <summary>Reads the league table.</summary>
/// <param name="scorecards">Scorecard lookup.</param>
public sealed class ListScorecardLeagueQueryHandler(ISupplierScorecardRepository scorecards)
    : IQueryHandler<ListScorecardLeagueQuery, IReadOnlyList<SupplierScorecard>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierScorecard>> HandleAsync(
        ListScorecardLeagueQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await scorecards
            .ListForPeriodAsync(query.PeriodStart, query.PeriodEnd, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Turns a repository page into the <see cref="PageResult{T}"/> the API returns, minting the cursor
/// from the last row.
/// </summary>
/// <remarks>
/// Every procurement list sorts newest-first on <c>CreatedAt</c> with the id as tiebreaker, so the
/// cursor is always built the same way. Written once here rather than three times in three handlers:
/// a cursor minted from a different column than the query sorted by silently repeats or skips rows,
/// and it does so only under concurrent inserts, which is the hardest kind of bug to see in a test.
/// </remarks>
internal static class ProcurementPages
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
