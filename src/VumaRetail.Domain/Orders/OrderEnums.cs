namespace VumaRetail.Domain.Orders;

/// <summary>Where an order was taken. Only <see cref="InStore"/> and <see cref="Phone"/> have a real caller in this stage.</summary>
/// <remarks>
/// <see cref="Online"/> and <see cref="Marketplace"/> are reserved for Stage 21 (ecommerce) and Stage
/// 21b (Vuma Connect) — the enum carries their values now so <c>CreateOrderCommand</c> does not need to
/// change shape to accept them later, only who calls it.
/// </remarks>
public enum SalesChannel
{
    /// <summary>Taken at a counter by staff.</summary>
    InStore = 0,

    /// <summary>Taken over the phone by staff.</summary>
    Phone = 1,

    /// <summary>A future storefront order. No real caller until Stage 21.</summary>
    Online = 2,

    /// <summary>A future marketplace order. No real caller until Stage 21b.</summary>
    Marketplace = 3,
}

/// <summary>How an order's goods reach the customer.</summary>
public enum OrderFulfilmentType
{
    /// <summary>Delivered to <see cref="SalesOrder.DeliveryAddress"/>.</summary>
    Delivery = 0,

    /// <summary>Collected by the customer at <see cref="SalesOrder.FulfillingLocationId"/> (business rule 8).</summary>
    ClickAndCollect = 1,
}

/// <summary>Where a <see cref="SalesOrder"/> stands.</summary>
public enum SalesOrderStatus
{
    /// <summary>Being built. Lines may still be added and priced.</summary>
    Draft = 0,

    /// <summary>Priced and accepted, but nothing has been allocated yet.</summary>
    Confirmed = 1,

    /// <summary>Some, but not all, active lines have an allocation.</summary>
    PartiallyAllocated = 2,

    /// <summary>Every active line has an allocation (a bin-assigned <c>PickTask</c>, business rule 1).</summary>
    Allocated = 3,

    /// <summary>Some, but not all, active lines have shipped.</summary>
    PartiallyFulfilled = 4,

    /// <summary>Every active line has shipped. Revenue may be recognised (business rule 5).</summary>
    Fulfilled = 5,

    /// <summary>Abandoned before anything shipped.</summary>
    Cancelled = 6,

    /// <summary>Fulfilled, settled and closed out. Terminal.</summary>
    Closed = 7,
}

/// <summary>
/// Names which mechanism paid for an order. Not a tender type of its own — see <see cref="SalesOrder"/>'s
/// remarks on <c>SettlingSaleId</c>/<c>SettlingCustomerAccountId</c>.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Nothing has been taken yet.</summary>
    Unpaid = 0,

    /// <summary>Paid in full, at a Stage 09 till or otherwise.</summary>
    Paid = 1,

    /// <summary>Charged to a Stage 10b customer account.</summary>
    OnAccount = 2,
}

/// <summary>Where one <see cref="SalesOrderLine"/> stands.</summary>
public enum SalesOrderLineStatus
{
    /// <summary>Priced but no allocation has been attempted, or attempted and nothing fit (see <see cref="Backordered"/>).</summary>
    Pending = 0,

    /// <summary>An open <c>PickTask</c> with bins assigned covers the whole requested quantity (business rule 1).</summary>
    Allocated = 1,

    /// <summary>An open <c>PickTask</c> covers part of the requested quantity; the rest is backordered.</summary>
    PartiallyAllocated = 2,

    /// <summary>Nothing fits today. <see cref="SalesOrderLine.BackorderedQuantity"/> names how much.</summary>
    Backordered = 3,

    /// <summary>Every unit that was ever allocated to this line has shipped, and nothing remains backordered.</summary>
    Fulfilled = 4,

    /// <summary>Some, but not all, of this line has shipped.</summary>
    PartiallyFulfilled = 5,

    /// <summary>Abandoned. Nothing on this line will ship.</summary>
    Cancelled = 6,
}

/// <summary>Where a <see cref="SalesOrderReturn"/> stands.</summary>
public enum SalesOrderReturnStatus
{
    /// <summary>Raised, with its lines, but not yet completed — nothing has moved.</summary>
    Open = 0,

    /// <summary>Stock received back and the financial event raised. Frozen.</summary>
    Completed = 1,
}

/// <summary>What happened when one <see cref="SalesOrderReturnLine"/>'s stock was received back.</summary>
/// <remarks>
/// The same three states as Stage 10's <c>StockReturnStatus</c>, declared separately rather than shared
/// — the two document families never read each other's tables (business rule 6), and a shared enum
/// would be the one place they quietly did.
/// </remarks>
public enum OrderStockReturnStatus
{
    /// <summary>On the document but not yet received — the return has not completed.</summary>
    Pending = 0,

    /// <summary>Back in stock. <see cref="SalesOrderReturnLine.StockLedgerEntryId"/> names the ledger row.</summary>
    Posted = 1,

    /// <summary>The ledger refused it (ADR-070/073's precedent). The refund still stands.</summary>
    Refused = 2,
}
