using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Orders;

namespace VumaRetail.Application.Orders;

/// <summary>Reads and writes <see cref="SalesOrder"/> and <see cref="SalesOrderLine"/> rows.</summary>
public interface ISalesOrderRepository
{
    /// <summary>Finds an order, with its lines loaded, by id.</summary>
    Task<SalesOrder?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken = default);

    /// <summary>Finds an order, with its lines loaded, by its order number, case-insensitively.</summary>
    Task<SalesOrder?> FindByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// A keyset page of orders, newest-created first, optionally narrowed by status, channel, partner or
    /// order-date range. See <c>docs/API_STANDARDS.md</c> §8.
    /// </summary>
    Task<(IReadOnlyList<SalesOrder> Orders, bool HasMore)> ListPageAsync(
        SalesOrderStatus? status,
        SalesChannel? channel,
        Guid? partnerId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every order carrying at least one line with <see cref="SalesOrderLine.BackorderedQuantity"/>
    /// greater than zero, oldest <see cref="SalesOrder.OrderDate"/> first — business rule 3's fairness
    /// queue for <c>ReattemptBackorderedAllocationsCommand</c>.
    /// </summary>
    Task<IReadOnlyList<SalesOrder>> ListBackorderedOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a newly raised order.</summary>
    void Add(SalesOrder order);
}

/// <summary>Reads and writes <see cref="SalesOrderReturn"/> and <see cref="SalesOrderReturnLine"/> rows.</summary>
public interface ISalesOrderReturnRepository
{
    /// <summary>Finds a return, with its lines loaded, by id.</summary>
    Task<SalesOrderReturn?> FindAsync(Guid salesOrderReturnId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How much of one order line has already come back on earlier return documents — business rule 6's
    /// cumulative check, the same shape Stage 10's <c>ISalesReturnRepository.SumReturnedQuantityAsync</c>
    /// gives a sale line.
    /// </summary>
    /// <param name="salesOrderLineId">The original order line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<decimal> SumReturnedQuantityAsync(Guid salesOrderLineId, CancellationToken cancellationToken = default);

    /// <summary>A keyset page of returns raised within a period, newest first.</summary>
    Task<(IReadOnlyList<SalesOrderReturn> Returns, bool HasMore)> ListForPeriodAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly raised return.</summary>
    void Add(SalesOrderReturn orderReturn);
}
