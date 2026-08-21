using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Orders;

/// <summary>
/// One line coming back — a quantity off one original <see cref="SalesOrderLine"/>, refunded at that
/// line's snapshotted price (business rule 9's discipline applied to a return).
/// </summary>
/// <remarks>
/// <para>
/// Bounded by what was actually <em>fulfilled</em>, not by what was requested (business rule 6) — the
/// caller (<c>CreateOrderReturnCommandHandler</c>) reads the fulfilled quantity from
/// <c>IOrderFulfilmentReader</c> and the cumulative already-returned quantity from
/// <c>ISalesOrderReturnRepository</c>, because neither is visible to this aggregate on its own, the same
/// shape Stage 10's <c>SalesReturn.AddLine</c> uses.
/// </para>
/// <para>
/// The pro-rata math is the cumulative-fraction approach Stage 10's <c>SalesReturnLine</c> documents in
/// full: each amount is the rounded share of everything returned <em>so far</em> less the rounded share
/// of everything returned <em>before</em> this line, so partial returns telescope to exactly the line's
/// own total whatever they are split into, rather than accumulating a rounding error across several
/// partial returns.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesOrderReturnLine : Entity
{
    private SalesOrderReturnLine(
        Guid tenantId,
        Guid? storeId,
        Guid salesOrderReturnId,
        Guid salesOrderLineId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Quantity fulfilledQuantity,
        decimal previouslyReturnedQuantity,
        Money unitPrice,
        Money net,
        Money tax,
        Money gross)
        : base(tenantId, storeId)
    {
        SalesOrderReturnId = salesOrderReturnId;
        SalesOrderLineId = salesOrderLineId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        FulfilledQuantity = fulfilledQuantity;
        PreviouslyReturnedQuantity = previouslyReturnedQuantity;
        UnitPrice = unitPrice;
        Net = net;
        Tax = tax;
        Gross = gross;
        StockReturn = OrderStockReturnStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesOrderReturnLine()
    {
    }

    /// <summary>The return document this line belongs to.</summary>
    public Guid SalesOrderReturnId { get; private set; }

    /// <summary>The original <see cref="SalesOrderLine"/> this is a return of.</summary>
    public Guid SalesOrderLineId { get; private set; }

    /// <summary>The item returned, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant returned.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much is coming back. Always positive, never more than <see cref="FulfilledQuantity"/> less <see cref="PreviouslyReturnedQuantity"/>.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>How much of the original line was actually fulfilled, snapshotted at the moment this line was raised.</summary>
    public Quantity FulfilledQuantity { get; private set; }

    /// <summary>How much had already come back on earlier return documents when this line was raised.</summary>
    public decimal PreviouslyReturnedQuantity { get; private set; }

    /// <summary>What one unit was priced at on the order — the snapshotted price, not today's.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>The refund excluding tax.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax coming back, pro-rata off the order line's snapshotted price.</summary>
    public Money Tax { get; private set; }

    /// <summary>What the customer gets back for this line — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>What happened when this line's stock was received back.</summary>
    public OrderStockReturnStatus StockReturn { get; private set; }

    /// <summary>The <c>inventory.stock_ledger_entries</c> row this line produced, when it produced one.</summary>
    public Guid? StockLedgerEntryId { get; private set; }

    /// <summary>Why the stock receipt was refused, when it was.</summary>
    public string? StockReturnNote { get; private set; }

    /// <summary>
    /// Builds a return line from the original order line, pro-rating its snapshotted price to the
    /// quantity coming back. Prefer <see cref="SalesOrderReturn.AddLine"/>, which enforces the document's
    /// rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the return happened at.</param>
    /// <param name="salesOrderReturnId">The return document.</param>
    /// <param name="orderedLine">The original order line.</param>
    /// <param name="quantity">How much is coming back.</param>
    /// <param name="fulfilledQuantity">How much of <paramref name="orderedLine"/> was actually fulfilled.</param>
    /// <param name="previouslyReturnedQuantity">How much had already come back on earlier returns.</param>
    /// <exception cref="OrdersRuleException">The quantity is not positive, or exceeds what remains available to return.</exception>
    public static SalesOrderReturnLine ForFulfilledLine(
        Guid tenantId,
        Guid? storeId,
        Guid salesOrderReturnId,
        SalesOrderLine orderedLine,
        Quantity quantity,
        Quantity fulfilledQuantity,
        decimal previouslyReturnedQuantity)
    {
        ArgumentNullException.ThrowIfNull(orderedLine);

        if (quantity.Value <= 0m)
        {
            throw OrdersRuleException.QuantityMustBePositive();
        }

        if (previouslyReturnedQuantity + quantity.Value > fulfilledQuantity.Value)
        {
            throw OrdersRuleException.ReturnExceedsFulfilledQuantity(
                fulfilledQuantity.Value, previouslyReturnedQuantity, quantity.Value);
        }

        decimal requestedQuantity = orderedLine.RequestedQuantity.Value;

        Money originalGross = requestedQuantity == 0m
            ? Money.Zero(orderedLine.UnitPrice.Currency)
            : (orderedLine.UnitPrice * requestedQuantity) - orderedLine.DiscountAmount + orderedLine.TaxAmount;
        Money originalTax = orderedLine.TaxAmount;

        decimal fractionBefore = requestedQuantity == 0m ? 0m : previouslyReturnedQuantity / requestedQuantity;
        decimal fractionAfter = requestedQuantity == 0m ? 0m : (previouslyReturnedQuantity + quantity.Value) / requestedQuantity;

        Money gross = Share(originalGross, fractionAfter) - Share(originalGross, fractionBefore);
        Money tax = Share(originalTax, fractionAfter) - Share(originalTax, fractionBefore);
        Money net = gross - tax;

        return new SalesOrderReturnLine(
            tenantId,
            storeId,
            salesOrderReturnId,
            orderedLine.Id,
            orderedLine.ItemId,
            orderedLine.ItemVariantId,
            quantity,
            fulfilledQuantity,
            previouslyReturnedQuantity,
            orderedLine.UnitPrice,
            net,
            tax,
            gross);
    }

    /// <summary>The rounded share of an original amount for a cumulative fraction of the line.</summary>
    private static Money Share(Money original, decimal fraction) => (original * fraction).RoundToCurrencyScale();

    /// <summary>Records that this line's stock went back into stock.</summary>
    /// <param name="stockLedgerEntryId">The ledger entry that was posted.</param>
    public void RecordStockReturned(Guid stockLedgerEntryId)
    {
        StockReturn = OrderStockReturnStatus.Posted;
        StockLedgerEntryId = stockLedgerEntryId;
        StockReturnNote = null;
    }

    /// <summary>
    /// Records that the ledger refused to take this line's stock back, and why. The refund still stands
    /// (ADR-070/073's precedent).
    /// </summary>
    /// <param name="reason">The refusal, in words a manager reconciling it can act on.</param>
    public void RecordStockReturnRefused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        StockReturn = OrderStockReturnStatus.Refused;
        StockLedgerEntryId = null;
        StockReturnNote = reason.Length > 500 ? reason[..500] : reason;
    }
}
