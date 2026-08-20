using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders;

/// <summary>
/// The one financial fact a completed order raises: this order, at this store, for this net, tax and
/// gross, recognised once (business rule 5).
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §7 rule 12: there is no field a GL account code could go in. The event says revenue
/// was earned and how much of it was tax; which account it lands on — and whether the debit side is a
/// trade debtor or a bank account — is a posting rule a tenant owns (ADR-016). This is a genuinely new
/// financial event, not a reuse: no earlier stage raised "an order was fulfilled" (ADR-093's sibling
/// decision for the return side, ADR-094 for this one).
/// </remarks>
/// <param name="TenantId">The tenant the order belongs to.</param>
/// <param name="StoreId">The store it was placed at.</param>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number — what a person reconciling the journal searches on.</param>
/// <param name="Net">The order's net value.</param>
/// <param name="Tax">The order's tax.</param>
/// <param name="Gross">What the order is worth.</param>
/// <param name="OccurredAt">When revenue was recognised, UTC.</param>
public sealed record OrderFulfilledEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid SalesOrderId,
    string OrderNumber,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where orders raises the financial event revenue recognition produces, for whatever turns it into a
/// balanced journal.
/// </summary>
/// <remarks>
/// The same port shape as <c>ISalesReturnFinancialEventPublisher</c>, for the same two reasons:
/// <c>VumaRetail.Application</c> may not reference <c>VumaRetail.Finance</c>, and a host wired without a
/// finance module must still be able to complete an order rather than fail to build a container.
/// </remarks>
public interface IOrderFulfilmentEventPublisher
{
    /// <summary>Raises one order-fulfilled event.</summary>
    /// <param name="fulfilledEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(OrderFulfilledEvent fulfilledEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one financial fact a completed order return raises: goods came back and a refund is due.
/// </summary>
/// <remarks>
/// A genuinely new event type, not a reuse of Stage 10's <c>sales.return.completed</c> — the same
/// reasoning <c>StockReferenceType.OrderReturn</c>'s own remarks give for the stock side (ADR-093):
/// this reverses a <see cref="Domain.Orders.SalesOrder"/> line, never a Stage 09 till <c>Sale</c>, and
/// the two document families must never be confused in a journal search either (business rule 6).
/// </remarks>
/// <param name="TenantId">The tenant the return belongs to.</param>
/// <param name="StoreId">The store it happened at.</param>
/// <param name="SalesOrderReturnId">The return.</param>
/// <param name="ReturnNumber">Its credit document number — ADR-065's <c>ORT</c> series.</param>
/// <param name="SalesOrderId">The order it reverses part of.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back.</param>
/// <param name="Gross">What the customer gets back.</param>
/// <param name="OccurredAt">When the return completed, UTC.</param>
public sealed record OrderReturnCompletedEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid SalesOrderReturnId,
    string ReturnNumber,
    Guid SalesOrderId,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where orders raises the financial event a completed order return produces.
/// </summary>
public interface IOrderReturnFinancialEventPublisher
{
    /// <summary>Raises one order-return-completed event.</summary>
    /// <param name="returnEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(OrderReturnCompletedEvent returnEvent, CancellationToken cancellationToken = default);
}
