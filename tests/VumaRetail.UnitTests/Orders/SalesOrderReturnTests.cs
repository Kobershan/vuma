using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// The return document's own invariant (business rule 6): nothing comes back beyond what was actually
/// fulfilled, and the refund is taken pro-rata off the order line's snapshotted price — the same
/// cumulative-fraction discipline Stage 10's <c>SalesReturnLine</c> uses, applied to a fulfilled order
/// line instead of a sold one.
/// </summary>
public sealed class SalesOrderReturnTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static SalesOrderLine FulfilledLine(decimal requested, decimal unitPrice, decimal discount, decimal tax)
    {
        SalesOrderLine line = SalesOrderLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, null, new Quantity(requested, "EA"), "ZAR");
        line.ApplyPricing(new Money(unitPrice, "ZAR"), new Money(discount, "ZAR"), new Money(tax, "ZAR"), null, string.Empty);
        return line;
    }

    private static SalesOrderReturn Raise(Guid salesOrderId)
        => SalesOrderReturn.Raise(salesOrderId, TenantId, StoreId, "ZAR", "ORT-000001", "Faulty item", UserId, Now);

    [Fact]
    public void A_return_refunds_what_was_actually_fulfilled_at_the_lines_snapshotted_price()
    {
        // Three at R100 net each, R45 tax off the line (R345 gross), of which one comes back: a third
        // of each, taken off the stored amounts.
        SalesOrderLine line = FulfilledLine(requested: 3m, unitPrice: 100m, discount: 0m, tax: 45m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);

        SalesOrderReturnLine returnLine = orderReturn.AddLine(
            line, new Quantity(1m, "EA"), fulfilledQuantity: new Quantity(3m, "EA"), previouslyReturned: 0m);

        returnLine.Net.Amount.Should().Be(100m);
        returnLine.Tax.Amount.Should().Be(15m);
        returnLine.Gross.Amount.Should().Be(115m);
        orderReturn.Gross.Amount.Should().Be(115m);
    }

    [Fact]
    public void Partial_returns_of_one_line_across_two_documents_sum_to_exactly_what_it_was_worth()
    {
        // The same telescoping cumulative-fraction arithmetic SalesReturnLine documents: two halves,
        // each on its own return document (business rule 6 refuses the same line twice on one
        // document), must sum to the whole line's gross, not to twice a rounded half.
        SalesOrderLine line = FulfilledLine(requested: 2m, unitPrice: 40m, discount: 20m, tax: 7.83m);
        // Charged: (40*2 - 20) = 60 net-of-discount, gross = 60 + 7.83 = 67.83 across 2 units.

        SalesOrderReturn firstReturn = Raise(line.SalesOrderId);
        SalesOrderReturnLine first = firstReturn.AddLine(
            line, new Quantity(1m, "EA"), fulfilledQuantity: new Quantity(2m, "EA"), previouslyReturned: 0m);

        SalesOrderReturn secondReturn = Raise(line.SalesOrderId);
        SalesOrderReturnLine second = secondReturn.AddLine(
            line, new Quantity(1m, "EA"), fulfilledQuantity: new Quantity(2m, "EA"), previouslyReturned: 1m);

        (first.Gross.Amount + second.Gross.Amount).Should().Be(67.83m);
    }

    [Fact]
    public void Returning_more_than_was_fulfilled_is_refused()
    {
        SalesOrderLine line = FulfilledLine(requested: 5m, unitPrice: 20m, discount: 0m, tax: 15m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);

        Action adding = () => orderReturn.AddLine(
            line, new Quantity(4m, "EA"), fulfilledQuantity: new Quantity(3m, "EA"), previouslyReturned: 0m);

        adding.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_RETURN_EXCEEDS_FULFILLED_QUANTITY");
    }

    [Fact]
    public void Returning_more_than_remains_after_earlier_returns_is_refused()
    {
        SalesOrderLine line = FulfilledLine(requested: 5m, unitPrice: 20m, discount: 0m, tax: 15m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);

        Action adding = () => orderReturn.AddLine(
            line, new Quantity(2m, "EA"), fulfilledQuantity: new Quantity(5m, "EA"), previouslyReturned: 4m);

        adding.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_RETURN_EXCEEDS_FULFILLED_QUANTITY");
    }

    [Fact]
    public void The_same_order_line_cannot_appear_twice_on_one_return()
    {
        SalesOrderLine line = FulfilledLine(requested: 5m, unitPrice: 20m, discount: 0m, tax: 15m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);
        orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(5m, "EA"), previouslyReturned: 0m);

        Action addingAgain = () => orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(5m, "EA"), previouslyReturned: 1m);

        addingAgain.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_DUPLICATE_RETURN_LINE");
    }

    [Fact]
    public void A_line_from_a_different_order_is_refused()
    {
        SalesOrderLine line = FulfilledLine(requested: 5m, unitPrice: 20m, discount: 0m, tax: 15m);
        SalesOrderReturn orderReturn = Raise(UuidV7.NewGuid()); // a different order entirely

        Action adding = () => orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(5m, "EA"), previouslyReturned: 0m);

        adding.Should().Throw<OrdersNotFoundException>();
    }

    [Fact]
    public void Completing_with_no_lines_is_refused()
    {
        SalesOrderReturn orderReturn = Raise(UuidV7.NewGuid());

        Action completing = () => orderReturn.Complete(Now);

        completing.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_RETURN_HAS_NO_LINES");
    }

    [Fact]
    public void Completing_freezes_the_return()
    {
        SalesOrderLine line = FulfilledLine(requested: 1m, unitPrice: 50m, discount: 0m, tax: 6.52m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);
        orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(1m, "EA"), previouslyReturned: 0m);

        orderReturn.Complete(Now);

        orderReturn.Status.Should().Be(SalesOrderReturnStatus.Completed);
        orderReturn.CompletedAt.Should().Be(Now);

        Action completingAgain = () => orderReturn.Complete(Now);
        completingAgain.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_UNEXPECTED_RETURN_STATUS");
    }

    [Fact]
    public void Recording_the_stock_return_outcome_updates_the_line()
    {
        SalesOrderLine line = FulfilledLine(requested: 1m, unitPrice: 50m, discount: 0m, tax: 6.52m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);
        SalesOrderReturnLine returnLine = orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(1m, "EA"), previouslyReturned: 0m);

        Guid ledgerEntryId = UuidV7.NewGuid();
        returnLine.RecordStockReturned(ledgerEntryId);

        returnLine.StockReturn.Should().Be(OrderStockReturnStatus.Posted);
        returnLine.StockLedgerEntryId.Should().Be(ledgerEntryId);
    }

    [Fact]
    public void A_refused_stock_return_still_stands_but_says_why()
    {
        // ADR-070/073's precedent: the refund is not held hostage by a ledger refusal.
        SalesOrderLine line = FulfilledLine(requested: 1m, unitPrice: 50m, discount: 0m, tax: 6.52m);
        SalesOrderReturn orderReturn = Raise(line.SalesOrderId);
        SalesOrderReturnLine returnLine = orderReturn.AddLine(line, new Quantity(1m, "EA"), new Quantity(1m, "EA"), previouslyReturned: 0m);

        returnLine.RecordStockReturnRefused("No shipment could be traced.");

        returnLine.StockReturn.Should().Be(OrderStockReturnStatus.Refused);
        returnLine.StockLedgerEntryId.Should().BeNull();
        returnLine.StockReturnNote.Should().Be("No shipment could be traced.");
    }
}
