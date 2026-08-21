using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Orders;

/// <summary>
/// A promise to fulfil what a customer wants, placed at a counter or over the phone, ships from a
/// single Stage 08 location by delivery or click &amp; collect, and may ship in more than one Stage 13
/// wave. Owns its <see cref="SalesOrderLine"/>s.
/// </summary>
/// <remarks>
/// <para>
/// <b>No reservation ledger of its own (ADR-092).</b> This aggregate never persists an allocated or
/// fulfilled quantity; it asks <c>IOrderFulfilmentReader</c> and writes the answer onto its lines'
/// <see cref="SalesOrderLine.LineStatus"/> as a cache, not a source of truth. <see cref="Status"/> is a
/// further, order-level roll-up of that same cache — recomputed by <see cref="RecomputeStatus"/>, never
/// set directly by a handler.
/// </para>
/// <para>
/// <see cref="SettlingSaleId"/>/<see cref="SettlingCustomerAccountId"/> name <em>which</em> mechanism
/// paid, not a payment of this stage's own — this stage invents no tender type (see the stage document's
/// "what this stage does not own"). Both are plain <see cref="Guid"/>s, never a cross-schema foreign key
/// (<c>CONVENTIONS.md</c> §2).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesOrder : Entity
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder(
        Guid tenantId,
        Guid? storeId,
        string orderNumber,
        Guid? partnerId,
        SalesChannel channel,
        OrderFulfilmentType fulfilmentType,
        Guid fulfillingLocationId,
        Address? deliveryAddress,
        string currency,
        DateTimeOffset orderDate,
        DateTimeOffset? requestedFulfilmentDate)
        : base(tenantId, storeId)
    {
        OrderNumber = orderNumber;
        PartnerId = partnerId;
        Channel = channel;
        FulfilmentType = fulfilmentType;
        FulfillingLocationId = fulfillingLocationId;
        DeliveryAddress = deliveryAddress;
        Currency = currency;
        OrderDate = orderDate;
        RequestedFulfilmentDate = requestedFulfilmentDate;
        Status = SalesOrderStatus.Draft;
        PaymentStatus = OrderPaymentStatus.Unpaid;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesOrder()
    {
    }

    /// <summary>The human-readable number on the order. ADR-065's sequence, series <c>ORD</c>.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The customer, when one was identified. A counter or phone order from a walk-in needs none (business rule 10).</summary>
    public Guid? PartnerId { get; private set; }

    /// <summary>Where the order was taken.</summary>
    public SalesChannel Channel { get; private set; }

    /// <summary>How the goods reach the customer.</summary>
    public OrderFulfilmentType FulfilmentType { get; private set; }

    /// <summary>The single Stage 08 location this order ships from.</summary>
    public Guid FulfillingLocationId { get; private set; }

    /// <summary>Where the goods are delivered, present only when <see cref="FulfilmentType"/> is <see cref="OrderFulfilmentType.Delivery"/>.</summary>
    public Address? DeliveryAddress { get; private set; }

    /// <summary>Where the order stands, recomputed by <see cref="RecomputeStatus"/> from its lines.</summary>
    public SalesOrderStatus Status { get; private set; }

    /// <summary>Which mechanism has paid for the order, if any.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; }

    /// <summary>The Stage 09 <c>Sale</c> that settled this order, when a till did. A bare id, never a foreign key.</summary>
    public Guid? SettlingSaleId { get; private set; }

    /// <summary>The Stage 10b <c>CustomerAccount</c> this order was charged to, when one was. A bare id, never a foreign key.</summary>
    public Guid? SettlingCustomerAccountId { get; private set; }

    /// <summary>The order's currency. Every line is priced in it.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>When the order was placed, UTC.</summary>
    public DateTimeOffset OrderDate { get; private set; }

    /// <summary>When the customer asked to have it, if they gave a date.</summary>
    public DateTimeOffset? RequestedFulfilmentDate { get; private set; }

    /// <summary>Guards <c>orders.order.fulfilled</c> firing exactly once (business rule 5).</summary>
    public bool IsRevenueRecognised { get; private set; }

    /// <summary>When the order was cancelled, or <c>null</c>.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Who cancelled it.</summary>
    public string? CancelledBy { get; private set; }

    /// <summary>Why it was cancelled.</summary>
    public string? CancelReason { get; private set; }

    /// <summary>The order's net value — the sum of its lines' (price × quantity) less their discounts.</summary>
    public Money Net { get; private set; }

    /// <summary>The order's tax — the sum of its lines' tax.</summary>
    public Money Tax { get; private set; }

    /// <summary>What the order is worth — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>The order's lines.</summary>
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    /// <summary>True while lines may still be added and priced.</summary>
    public bool IsDraft => Status is SalesOrderStatus.Draft;

    /// <summary>Raises a new draft order.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="orderNumber">The order number, from ADR-065's <c>ORD</c> series.</param>
    /// <param name="partnerId">The customer, or <c>null</c> for a walk-in.</param>
    /// <param name="channel">Where it was taken.</param>
    /// <param name="fulfilmentType">Delivery or click &amp; collect.</param>
    /// <param name="fulfillingLocationId">The Stage 08 location this order ships from.</param>
    /// <param name="deliveryAddress">Required when <paramref name="fulfilmentType"/> is <see cref="OrderFulfilmentType.Delivery"/>; ignored otherwise.</param>
    /// <param name="currency">The order's currency.</param>
    /// <param name="orderDate">When the order was placed, UTC.</param>
    /// <param name="requestedFulfilmentDate">When the customer asked to have it, if they gave a date.</param>
    /// <exception cref="OrdersRuleException">Delivery was named with no address.</exception>
    public static SalesOrder Create(
        Guid tenantId,
        Guid? storeId,
        string orderNumber,
        Guid? partnerId,
        SalesChannel channel,
        OrderFulfilmentType fulfilmentType,
        Guid fulfillingLocationId,
        Address? deliveryAddress,
        string currency,
        DateTimeOffset orderDate,
        DateTimeOffset? requestedFulfilmentDate)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An order must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        if (fulfillingLocationId == Guid.Empty)
        {
            throw new ArgumentException("An order must name the location it ships from.", nameof(fulfillingLocationId));
        }

        if (fulfilmentType == OrderFulfilmentType.Delivery && deliveryAddress is null)
        {
            throw OrdersRuleException.DeliveryRequiresAddress();
        }

        return new SalesOrder(
            tenantId, storeId, orderNumber.Trim(), partnerId, channel, fulfilmentType, fulfillingLocationId,
            fulfilmentType == OrderFulfilmentType.Delivery ? deliveryAddress : null, currency, orderDate,
            requestedFulfilmentDate);
    }

    /// <summary>Adds a new demand line while the order is still a draft.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="requestedQuantity">How much is requested.</param>
    /// <exception cref="OrdersRuleException">The order is not a draft.</exception>
    public SalesOrderLine AddLine(Guid? itemId, Guid? itemVariantId, Quantity requestedQuantity)
    {
        EnsureDraft();

        SalesOrderLine line = SalesOrderLine.Create(TenantId, StoreId, Id, itemId, itemVariantId, requestedQuantity, Currency);
        _lines.Add(line);

        return line;
    }

    /// <summary>
    /// Accepts the order. Every line must already be priced (<see cref="SalesOrderLine.ApplyPricing"/>)
    /// before this is called — the handler resolves each line's price first, then confirms.
    /// </summary>
    /// <param name="confirmedAt">When, UTC. Also stamped as <see cref="OrderDate"/> if none was set yet — it never was, so this recomputes totals only.</param>
    /// <exception cref="OrdersRuleException">The order is not a draft, or has no lines.</exception>
    public void Confirm(DateTimeOffset confirmedAt)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw OrdersRuleException.OrderHasNoLines();
        }

        _ = confirmedAt;
        Status = SalesOrderStatus.Confirmed;
        RecalculateTotals();
    }

    /// <summary>
    /// Recomputes <see cref="Status"/> from its lines' current <see cref="SalesOrderLine.LineStatus"/>.
    /// Called after any operation that changes a line's status — confirm-time allocation, a backorder
    /// reattempt, a fulfilment refresh, or a line cancellation.
    /// </summary>
    /// <remarks>
    /// A no-op while the order is still <see cref="SalesOrderStatus.Draft"/> or already terminal
    /// (<see cref="SalesOrderStatus.Cancelled"/>/<see cref="SalesOrderStatus.Closed"/>) — those two
    /// states are set by their own explicit commands, never derived.
    /// </remarks>
    public void RecomputeStatus()
    {
        if (Status is SalesOrderStatus.Draft or SalesOrderStatus.Cancelled or SalesOrderStatus.Closed)
        {
            return;
        }

        List<SalesOrderLine> active = [.. _lines.Where(line => line.LineStatus != SalesOrderLineStatus.Cancelled)];

        if (active.Count == 0)
        {
            return;
        }

        if (active.All(line => line.LineStatus == SalesOrderLineStatus.Fulfilled))
        {
            Status = SalesOrderStatus.Fulfilled;
            return;
        }

        if (active.Any(line => line.LineStatus is SalesOrderLineStatus.Fulfilled or SalesOrderLineStatus.PartiallyFulfilled))
        {
            Status = SalesOrderStatus.PartiallyFulfilled;
            return;
        }

        if (active.All(line => line.LineStatus == SalesOrderLineStatus.Allocated))
        {
            Status = SalesOrderStatus.Allocated;
            return;
        }

        if (active.Any(line => line.LineStatus is SalesOrderLineStatus.Allocated or SalesOrderLineStatus.PartiallyAllocated))
        {
            Status = SalesOrderStatus.PartiallyAllocated;
            return;
        }

        Status = SalesOrderStatus.Confirmed;
    }

    /// <summary>
    /// Recognises this order's revenue if it has not already been recognised and every active line is
    /// fully accounted for (business rule 5).
    /// </summary>
    /// <param name="completedAt">When, UTC.</param>
    /// <returns><c>true</c> if this call actually recognised revenue; <c>false</c> if it already had (idempotent no-op).</returns>
    /// <exception cref="OrdersRuleException">Some active line has neither shipped in full nor been cancelled.</exception>
    public bool RecogniseRevenueIfDue(DateTimeOffset completedAt)
    {
        if (IsRevenueRecognised)
        {
            return false;
        }

        bool fullyAccountedFor = _lines.Count > 0
            && _lines.All(line => line.LineStatus is SalesOrderLineStatus.Fulfilled or SalesOrderLineStatus.Cancelled);
        bool hasAnyFulfilled = _lines.Any(line => line.LineStatus == SalesOrderLineStatus.Fulfilled);

        if (!fullyAccountedFor || !hasAnyFulfilled)
        {
            throw OrdersRuleException.OrderNotFullyAccountedFor();
        }

        IsRevenueRecognised = true;
        Status = SalesOrderStatus.Fulfilled;
        _ = completedAt;
        return true;
    }

    /// <summary>Records which mechanism paid for the order.</summary>
    /// <param name="paymentStatus">The new payment status.</param>
    /// <param name="settlingSaleId">The Stage 09 sale that settled it, if a till did.</param>
    /// <param name="settlingCustomerAccountId">The Stage 10b account it was charged to, if one was.</param>
    public void RecordSettlement(OrderPaymentStatus paymentStatus, Guid? settlingSaleId, Guid? settlingCustomerAccountId)
    {
        PaymentStatus = paymentStatus;
        SettlingSaleId = settlingSaleId;
        SettlingCustomerAccountId = settlingCustomerAccountId;
    }

    /// <summary>Refuses a whole-order cancel unless the order is still cancellable.</summary>
    /// <exception cref="OrdersRuleException">The order is already terminal, or some line has already shipped.</exception>
    public void EnsureCancellable()
    {
        if (Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed)
        {
            throw OrdersRuleException.UnexpectedOrderStatus(Status);
        }

        if (_lines.Any(line => line.LineStatus is SalesOrderLineStatus.Fulfilled or SalesOrderLineStatus.PartiallyFulfilled))
        {
            throw OrdersRuleException.CannotCancelFulfilledOrder();
        }
    }

    /// <summary>
    /// Marks the order cancelled. Call <see cref="EnsureCancellable"/> and cancel every open line first —
    /// the handler cancels each line's open <c>PickTask</c>s through Stage 13's own command before calling
    /// <see cref="SalesOrderLine.Cancel"/> on it, which this method does not do (business rule 7).
    /// </summary>
    /// <param name="reason">Why, in the operator's words.</param>
    /// <param name="cancelledBy">Who.</param>
    /// <param name="cancelledAt">When, UTC.</param>
    public void MarkCancelled(string? reason, string cancelledBy, DateTimeOffset cancelledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelledBy);

        Status = SalesOrderStatus.Cancelled;
        CancelledAt = cancelledAt;
        CancelledBy = cancelledBy;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Finds a line on this order.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="OrdersNotFoundException">No such line on this order.</exception>
    public SalesOrderLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new OrdersNotFoundException("sales order line", lineId);

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw OrdersRuleException.OrderNotDraft(Status);
        }
    }

    /// <summary>Recalculates <see cref="Net"/>, <see cref="Tax"/> and <see cref="Gross"/> from the current lines.</summary>
    private void RecalculateTotals()
    {
        Money net = Money.Zero(Currency);
        Money tax = Money.Zero(Currency);

        foreach (SalesOrderLine line in _lines)
        {
            net += (line.UnitPrice * line.RequestedQuantity.Value) - line.DiscountAmount;
            tax += line.TaxAmount;
        }

        Net = net;
        Tax = tax;
        Gross = net + tax;
    }
}
