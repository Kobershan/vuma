using VumaRetail.Domain.Procurement;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions.Procurement;

/// <summary>Reads and writes <see cref="PurchaseRequisition"/> aggregates.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, <c>CLAUDE.md</c> §7 rule 2).
/// </remarks>
public interface IPurchaseRequisitionRepository
{
    /// <summary>
    /// Finds a requisition with its lines loaded, or <c>null</c>. Loading the lines is not optional —
    /// <c>Submit</c> refuses an empty document and cannot tell an empty one from an unloaded one.
    /// </summary>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PurchaseRequisition?> FindAsync(Guid requisitionId, CancellationToken cancellationToken = default);

    /// <summary>A keyset page of requisitions, newest first, optionally narrowed to one status.</summary>
    /// <param name="status">The status to narrow to, or <c>null</c> for all of them.</param>
    /// <param name="after">The cursor, or <c>null</c> for the first page.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<PurchaseRequisition> Requisitions, bool HasMore)> ListPageAsync(
        PurchaseRequisitionStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the requisition that owns one line, with its lines loaded, or <c>null</c>.
    /// </summary>
    /// <param name="requisitionLineId">The line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The lookup a sourcing document needs. A caller that already knows which requisition line it is
    /// satisfying should not have to name the requisition too — a second id on the command is a second
    /// chance for the two to disagree, and the line already knows its parent.
    /// </remarks>
    Task<PurchaseRequisition?> FindByLineAsync(
        Guid requisitionLineId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new requisition.</summary>
    /// <param name="requisition">The requisition.</param>
    void Add(PurchaseRequisition requisition);
}

/// <summary>Reads and writes <see cref="Rfq"/> aggregates, with their lines and responses.</summary>
public interface IRfqRepository
{
    /// <summary>
    /// Finds an RFQ with its lines, its responses and those responses' lines loaded, or <c>null</c>.
    /// </summary>
    /// <param name="rfqId">The RFQ.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The whole graph, deliberately. "One RFQ awards at most one response" is an invariant across
    /// siblings, and an aggregate loaded without its siblings cannot check it — it would award a second
    /// time and be right about everything it could see.
    /// </remarks>
    Task<Rfq?> FindAsync(Guid rfqId, CancellationToken cancellationToken = default);

    /// <summary>A keyset page of RFQs, newest first, optionally narrowed to one status.</summary>
    /// <param name="status">The status to narrow to, or <c>null</c> for all of them.</param>
    /// <param name="after">The cursor, or <c>null</c> for the first page.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<Rfq> Rfqs, bool HasMore)> ListPageAsync(
        RfqStatus? status, KeysetCursor? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a new RFQ.</summary>
    /// <param name="rfq">The RFQ.</param>
    void Add(Rfq rfq);
}

/// <summary>Reads and writes <see cref="PurchaseOrder"/> aggregates.</summary>
public interface IPurchaseOrderRepository
{
    /// <summary>Finds an order with its lines loaded, or <c>null</c>.</summary>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PurchaseOrder?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>Finds an order by its document number, or <c>null</c>.</summary>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PurchaseOrder?> FindByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>A keyset page of orders, newest first, optionally narrowed by supplier and status.</summary>
    /// <param name="partnerId">The supplier to narrow to, or <c>null</c> for all of them.</param>
    /// <param name="status">The status to narrow to, or <c>null</c> for all of them.</param>
    /// <param name="after">The cursor, or <c>null</c> for the first page.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<PurchaseOrder> Orders, bool HasMore)> ListPageAsync(
        Guid? partnerId,
        PurchaseOrderStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every order issued to one supplier whose expected date falls in a period, with lines loaded.
    /// The scorecard calculator's read.
    /// </summary>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="from">The period's first day, inclusive.</param>
    /// <param name="to">The period's last day, inclusive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<PurchaseOrder>> ListForSupplierPeriodAsync(
        Guid partnerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Adds a new order.</summary>
    /// <param name="order">The order.</param>
    void Add(PurchaseOrder order);
}

/// <summary>Reads and writes <see cref="GoodsReceipt"/> aggregates.</summary>
public interface IGoodsReceiptRepository
{
    /// <summary>Finds a receipt with its lines loaded, or <c>null</c>.</summary>
    /// <param name="goodsReceiptId">The receipt.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<GoodsReceipt?> FindAsync(Guid goodsReceiptId, CancellationToken cancellationToken = default);

    /// <summary>Every completed receipt against one order, oldest first, with lines loaded.</summary>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<GoodsReceipt>> ListForOrderAsync(
        Guid purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>Every completed receipt against one supplier's orders in a period, with lines loaded.</summary>
    /// <param name="purchaseOrderIds">The orders in scope.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<GoodsReceipt>> ListForOrdersAsync(
        IReadOnlyCollection<Guid> purchaseOrderIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every receipt line whose stock posting the ledger refused (ADR-073) in a period, newest first.
    /// The reconciliation queue, and it should stay empty.
    /// </summary>
    /// <param name="from">The first day, inclusive.</param>
    /// <param name="to">The last day, inclusive.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<GoodsReceipt>> ListWithRefusedStockAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a new receipt.</summary>
    /// <param name="receipt">The receipt.</param>
    void Add(GoodsReceipt receipt);
}

/// <summary>Reads and writes <see cref="SupplierInvoiceMatch"/> aggregates.</summary>
public interface ISupplierInvoiceMatchRepository
{
    /// <summary>Finds a match with its lines loaded, or <c>null</c>.</summary>
    /// <param name="matchId">The match.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SupplierInvoiceMatch?> FindAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>Every match against one order, oldest first, with lines loaded.</summary>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SupplierInvoiceMatch>> ListForOrderAsync(
        Guid purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>Matches in a period, newest first, optionally narrowed to one verdict.</summary>
    /// <param name="from">The first day, inclusive.</param>
    /// <param name="to">The last day, inclusive.</param>
    /// <param name="status">The verdict to narrow to, or <c>null</c> for all of them.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SupplierInvoiceMatch>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        ThreeWayMatchStatus? status,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this supplier invoice number has already been matched against this order — the check
    /// that stops one delivery being paid for twice.
    /// </summary>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="supplierInvoiceNumber">The supplier's invoice number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> ExistsForInvoiceNumberAsync(
        Guid purchaseOrderId, string supplierInvoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// How much of one order line has already been claimed on <em>released</em> matches, excluding the
    /// document being built.
    /// </summary>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="excludingMatchId">The document being built, so its own lines are not counted twice.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Released only, deliberately, and the opposite call from <c>ISalesReturnRepository</c>'s
    /// equivalent. An unreleased match has not been accepted — it may well be the blocked one somebody
    /// is about to correct — and counting it would block the corrected invoice too.
    /// </remarks>
    Task<decimal> SumInvoicedQuantityAsync(
        Guid purchaseOrderLineId, Guid? excludingMatchId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new match.</summary>
    /// <param name="match">The match.</param>
    void Add(SupplierInvoiceMatch match);
}

/// <summary>Reads and writes <see cref="SupplierScorecard"/> snapshots.</summary>
public interface ISupplierScorecardRepository
{
    /// <summary>Finds one supplier's scorecard for one period, or <c>null</c>.</summary>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="periodStart">The period's first day.</param>
    /// <param name="periodEnd">The period's last day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SupplierScorecard?> FindAsync(
        Guid partnerId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default);

    /// <summary>Every scorecard for one supplier, newest period first.</summary>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SupplierScorecard>> ListForSupplierAsync(
        Guid partnerId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Every supplier's scorecard for one period, best rating first — the league table.</summary>
    /// <param name="periodStart">The period's first day.</param>
    /// <param name="periodEnd">The period's last day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SupplierScorecard>> ListForPeriodAsync(
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default);

    /// <summary>Adds a new snapshot.</summary>
    /// <param name="scorecard">The scorecard.</param>
    void Add(SupplierScorecard scorecard);
}

/// <summary>
/// The one financial fact procurement raises: this supplier invoice, matched against this order, for
/// this net, tax and gross.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 12, the same way <c>SalesReturnCompletedEvent</c> honours it: there is no
/// field a GL account code could go in. The event says a matched supplier liability crystallised and
/// how much of it was tax; that the goods-received-not-invoiced clearing account is relieved and trade
/// creditors credited is a posting rule a tenant owns (ADR-016, ADR-085).
/// </para>
/// <para>
/// Note what it does <em>not</em> carry: no <c>ApInvoice</c>, no due date, no payment terms. Those are
/// Stage 07's, and procurement hands over a matched fact rather than a document Finance did not author.
/// </para>
/// </remarks>
/// <param name="TenantId">The tenant the match belongs to.</param>
/// <param name="StoreId">The store that bought.</param>
/// <param name="SupplierInvoiceMatchId">The match.</param>
/// <param name="PurchaseOrderId">The order it was matched against.</param>
/// <param name="OrderNumber">The order number — what somebody reconciling the journal searches on.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="Net">What is being accepted, excluding tax.</param>
/// <param name="Tax">The tax on it.</param>
/// <param name="Gross">What the supplier will be paid.</param>
/// <param name="OccurredAt">When the match was released, UTC.</param>
public sealed record SupplierInvoiceMatchedEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid SupplierInvoiceMatchId,
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid PartnerId,
    string SupplierInvoiceNumber,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where procurement raises the financial event a released match produces, for whatever turns it into
/// a balanced journal.
/// </summary>
/// <remarks>
/// The same port shape and the same two reasons as <c>ISalesReturnFinancialEventPublisher</c>:
/// <c>VumaRetail.Application</c> may not reference <c>VumaRetail.Finance</c>, and a host wired without
/// a finance module must still be able to buy things rather than fail to build a container.
/// </remarks>
public interface IProcurementFinancialEventPublisher
{
    /// <summary>Raises one matched-invoice event.</summary>
    /// <param name="matchedEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The journal that was posted, or <c>null</c> if nothing posted one.</returns>
    Task<Guid?> PublishAsync(
        SupplierInvoiceMatchedEvent matchedEvent, CancellationToken cancellationToken = default);
}
