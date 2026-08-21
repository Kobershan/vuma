using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Orders;

/// <summary>
/// One line of demand on a <see cref="SalesOrder"/>: an item or variant, a requested quantity, and the
/// price snapshot <see cref="SalesOrder.Confirm"/> takes once through <c>IPriceResolver</c> (business
/// rule 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>AllocatedQuantity</c>/<c>FulfilledQuantity</c> column.</b> Both are computed on demand from
/// Stage 13's <c>PickTask</c> state, keyed by this line's own <c>Id</c> as <c>PickTask.OutboundReference</c>
/// (ADR-092). Persisting a second copy here is exactly the drift this stage exists to avoid — the one
/// figure this line keeps of its own is <see cref="BackorderedQuantity"/>, because nothing in Stage 13
/// can ever tell this stage how much of a line was <em>never even attempted</em>.
/// </para>
/// <para>
/// <see cref="LineStatus"/> is set from the Application layer by <see cref="RecordAllocationOutcome"/>
/// and <see cref="RecordFulfilmentOutcome"/> — a handler reading <c>IOrderFulfilmentReader</c> calls
/// these directly on the line, the same shape Stage 10's <c>SalesReturnCompletionService</c> uses when it
/// calls <c>SalesReturnLine.RecordStockReturned</c> directly rather than through the aggregate root.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesOrderLine : Entity
{
    private SalesOrderLine(
        Guid tenantId,
        Guid? storeId,
        Guid salesOrderId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity requestedQuantity,
        string currency)
        : base(tenantId, storeId)
    {
        SalesOrderId = salesOrderId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        RequestedQuantity = requestedQuantity;
        UnitPrice = Money.Zero(currency);
        DiscountAmount = Money.Zero(currency);
        TaxAmount = Money.Zero(currency);
        BackorderedQuantity = Quantity.Zero(requestedQuantity.UnitOfMeasure);
        PromotionsSummary = string.Empty;
        LineStatus = SalesOrderLineStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesOrderLine()
    {
    }

    /// <summary>The order this line belongs to.</summary>
    public Guid SalesOrderId { get; private set; }

    /// <summary>The item requested, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant requested.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was requested.</summary>
    public Quantity RequestedQuantity { get; private set; }

    /// <summary>The unit price <c>IPriceResolver</c> resolved at confirm time. Immutable once set (business rule 9).</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>The whole-line discount amount from the price resolution.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The tax on this line, at the resolved price.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>The price list this line was resolved against, once confirmed.</summary>
    public Guid? PriceListId { get; private set; }

    /// <summary>A text summary of the promotions that fired, for the same reason Stage 10 keeps one.</summary>
    public string PromotionsSummary { get; private set; } = string.Empty;

    /// <summary>How much of this line has no allocation and none is being attempted right now.</summary>
    public Quantity BackorderedQuantity { get; private set; }

    /// <summary>Where this line stands.</summary>
    public SalesOrderLineStatus LineStatus { get; private set; }

    /// <summary>Creates a new demand line, unpriced, at zero backorder.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="salesOrderId">The order this line belongs to.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="requestedQuantity">How much is requested. Must be positive.</param>
    /// <param name="currency">The order's currency — every price on this line starts at zero in it.</param>
    /// <exception cref="OrdersRuleException">Neither or both of <paramref name="itemId"/>/<paramref name="itemVariantId"/> is set, or the quantity is not positive.</exception>
    public static SalesOrderLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid salesOrderId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity requestedQuantity,
        string currency)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An order line must belong to a tenant.", nameof(tenantId));
        }

        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("An order line must belong to an order.", nameof(salesOrderId));
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw OrdersRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (requestedQuantity.IsNegative || requestedQuantity.IsZero)
        {
            throw OrdersRuleException.QuantityMustBePositive();
        }

        return new SalesOrderLine(tenantId, storeId, salesOrderId, itemId, itemVariantId, requestedQuantity, currency);
    }

    /// <summary>
    /// Snapshots the price resolution onto the line. Called once, at confirm time — a later price list or
    /// promotion change never reprices a confirmed order (business rule 9).
    /// </summary>
    /// <param name="unitPrice">The resolved unit price.</param>
    /// <param name="discountAmount">The whole-line discount.</param>
    /// <param name="taxAmount">The tax on the line.</param>
    /// <param name="priceListId">The price list that priced it.</param>
    /// <param name="promotionsSummary">A text summary of the promotions that fired, if any.</param>
    public void ApplyPricing(Money unitPrice, Money discountAmount, Money taxAmount, Guid? priceListId, string promotionsSummary)
    {
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        TaxAmount = taxAmount;
        PriceListId = priceListId;
        PromotionsSummary = promotionsSummary ?? string.Empty;
    }

    /// <summary>
    /// Records what a fresh allocation attempt (<c>ConfirmOrderCommand</c> or
    /// <c>ReattemptBackorderedAllocationsCommand</c>) achieved.
    /// </summary>
    /// <param name="backorderedQuantity">What remains unpromised after the attempt. Zero means the line is fully allocated.</param>
    /// <exception cref="OrdersRuleException">The value is negative or exceeds what was requested.</exception>
    public void RecordAllocationOutcome(Quantity backorderedQuantity)
    {
        if (backorderedQuantity.IsNegative)
        {
            throw OrdersRuleException.QuantityMustBePositive();
        }

        BackorderedQuantity = backorderedQuantity;

        LineStatus = backorderedQuantity.IsZero
            ? SalesOrderLineStatus.Allocated
            : backorderedQuantity.Value >= RequestedQuantity.Value
                ? SalesOrderLineStatus.Backordered
                : SalesOrderLineStatus.PartiallyAllocated;
    }

    /// <summary>
    /// Reconciles this line's status from what Stage 13 currently reports (business rule 4). A short pick
    /// surfaces as <see cref="SalesOrderLineStatus.PartiallyFulfilled"/>, never
    /// <see cref="SalesOrderLineStatus.Backordered"/> — the two are different problems with different
    /// owners.
    /// </summary>
    /// <param name="allocatedQuantity">The open, bin-assigned quantity <c>IOrderFulfilmentReader</c> reports right now.</param>
    /// <param name="fulfilledQuantity">The shipped quantity <c>IOrderFulfilmentReader</c> reports right now.</param>
    public void RecordFulfilmentOutcome(Quantity allocatedQuantity, Quantity fulfilledQuantity)
    {
        if (LineStatus == SalesOrderLineStatus.Cancelled)
        {
            return;
        }

        if (fulfilledQuantity.Value > 0m && fulfilledQuantity.Value >= RequestedQuantity.Value && BackorderedQuantity.IsZero)
        {
            LineStatus = SalesOrderLineStatus.Fulfilled;
            return;
        }

        if (fulfilledQuantity.Value > 0m)
        {
            LineStatus = SalesOrderLineStatus.PartiallyFulfilled;
            return;
        }

        if (!allocatedQuantity.IsZero)
        {
            LineStatus = allocatedQuantity.Value >= RequestedQuantity.Value - BackorderedQuantity.Value
                ? SalesOrderLineStatus.Allocated
                : SalesOrderLineStatus.PartiallyAllocated;
            return;
        }

        LineStatus = BackorderedQuantity.IsZero
            ? SalesOrderLineStatus.Pending
            : BackorderedQuantity.Value >= RequestedQuantity.Value
                ? SalesOrderLineStatus.Backordered
                : SalesOrderLineStatus.PartiallyAllocated;
    }

    /// <summary>Cancels the line. Nothing further will ship against it.</summary>
    /// <exception cref="OrdersRuleException">Some or all of this line has already shipped.</exception>
    public void Cancel()
    {
        if (LineStatus is SalesOrderLineStatus.Fulfilled or SalesOrderLineStatus.PartiallyFulfilled)
        {
            throw OrdersRuleException.CannotCancelFulfilledLine();
        }

        LineStatus = SalesOrderLineStatus.Cancelled;
        BackorderedQuantity = Quantity.Zero(RequestedQuantity.UnitOfMeasure);
    }
}
