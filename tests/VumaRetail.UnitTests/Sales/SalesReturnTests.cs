using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The return document's invariants: nothing comes back that did not go out, and what comes back is
/// refunded at what was actually charged.
/// </summary>
/// <remarks>
/// Both of these are money rules with a customer standing at the counter, so both are asserted here in
/// the aggregate and again as check constraints in the integration suite. The tax cases are ADR-075:
/// the refund's tax is derived from the tax stored on the original line, pro-rata, and never recomputed
/// from today's rate.
/// </remarks>
public sealed class SalesReturnTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_return_refunds_what_was_charged_and_takes_its_tax_pro_rata_from_the_original_line()
    {
        // Three sold at R115 gross, of which R15 tax each: R345 gross, R45 tax. One comes back, so a
        // third of each — and the third is taken off the *stored* amounts, not recomputed at today's
        // rate. Yesterday's receipt is not restated by tomorrow's budget speech.
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 115m, net: 300m, tax: 45m);
        SalesReturn salesReturn = Raise(sale);

        SalesReturnLine line = salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        line.Net.Amount.Should().Be(100m);
        line.Tax.Amount.Should().Be(15m);
        line.Gross.Amount.Should().Be(115m);
        line.UnitPrice.Amount.Should().Be(115m);
        salesReturn.Gross.Amount.Should().Be(115m);
    }

    [Fact]
    public void A_line_bought_on_special_is_refunded_at_the_special_price()
    {
        // Business rule 6. Two at R40 with R20 off the line: R60 charged, so R30 a unit comes back —
        // not the R50 the shelf says today and not the R40 the list said then.
        Sale sale = CompletedSale(quantity: 2m, unitPrice: 40m, net: 52.17m, tax: 7.83m, discount: 20m);
        SalesReturn salesReturn = Raise(sale);

        SalesReturnLine line = salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        line.Gross.Amount.Should().Be(30.00m);
        line.Tax.Amount.Should().Be(3.92m);
        line.Net.Amount.Should().Be(26.08m);
    }

    [Fact]
    public void Partial_refunds_of_one_line_sum_to_exactly_what_was_charged_for_it()
    {
        // The reason the shares are cumulative rather than per-return. Rounding each return
        // independently refunds R30.01 for each half of this R60.00 line — R60.02 in total, a cent of
        // money the shop never took. Business rule 5 caps the quantity that can come back and would
        // not have caught it.
        Sale sale = CompletedSale(quantity: 2m, unitPrice: 40m, net: 52.17m, tax: 7.83m, discount: 20m);

        SalesReturnLine first = Raise(sale)
            .AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        SalesReturnLine second = Raise(sale)
            .AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 1m);

        (first.Gross + second.Gross).Amount.Should().Be(60.00m);
        (first.Tax + second.Tax).Amount.Should().Be(7.83m);
        (first.Net + second.Net).Amount.Should().Be(52.17m);
    }

    [Fact]
    public void Returning_a_whole_line_at_once_refunds_exactly_what_the_line_carried()
    {
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 33.33m, net: 86.95m, tax: 13.04m);

        SalesReturnLine line = Raise(sale)
            .AddLine(sale.Lines[0], new Quantity(3m, "EA"), previouslyReturned: 0m);

        line.Gross.Amount.Should().Be(99.99m);
        line.Tax.Amount.Should().Be(13.04m);
        line.Net.Amount.Should().Be(86.95m);
    }

    [Fact]
    public void A_return_lines_parts_always_add_up_to_its_gross()
    {
        // Net and tax are each rounded once and gross is their sum, rather than a third independent
        // rounding — otherwise a line can miss its own total by a cent and every document above it is
        // then wrong by that cent.
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 33.33m, net: 86.95m, tax: 13.04m);
        SalesReturn salesReturn = Raise(sale);

        SalesReturnLine line = salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        (line.Net + line.Tax).Amount.Should().Be(line.Gross.Amount);
    }

    [Fact]
    public void Partial_returns_accumulate_and_the_last_one_that_fits_is_allowed()
    {
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 115m, net: 300m, tax: 45m);

        Raise(sale).AddLine(sale.Lines[0], new Quantity(2m, "EA"), previouslyReturned: 0m);

        SalesReturn second = Raise(sale);
        SalesReturnLine line = second.AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 2m);

        line.Quantity.Value.Should().Be(1m);
        line.PreviouslyReturnedQuantity.Should().Be(2m);
        line.OriginalQuantity.Value.Should().Be(3m);
    }

    [Fact]
    public void A_return_can_never_exceed_what_was_sold()
    {
        // Business rule 5, and the one this stage would most regret getting wrong: an over-return is a
        // refund of money the shop never took.
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 115m, net: 300m, tax: 45m);
        SalesReturn salesReturn = Raise(sale);

        Action overReturning = () => salesReturn.AddLine(
            sale.Lines[0], new Quantity(2m, "EA"), previouslyReturned: 2m);

        overReturning.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_RETURN_EXCEEDS_QUANTITY_SOLD");
    }

    [Fact]
    public void The_same_sale_line_cannot_go_on_one_return_twice()
    {
        // Two rows for one original line is exactly how an over-return slips past a per-row check: each
        // row passes on its own and the pair does not.
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 115m, net: 300m, tax: 45m);
        SalesReturn salesReturn = Raise(sale);

        salesReturn.AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        Action twice = () => salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        twice.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_DUPLICATE_RETURN_LINE");
    }

    [Fact]
    public void Only_a_completed_sale_can_be_returned()
    {
        // Business rule 7. A voided sale took no money and moved no goods; an open one is corrected by
        // voiding a line, which costs the customer nothing.
        Sale open = OpenSale();

        Action raising = () => SalesReturn.Raise(
            open, "RTN-000001", "Faulty", TenderType.Cash, OperatorId, Now);

        raising.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_RETURN_REQUIRES_COMPLETED_SALE");
    }

    [Fact]
    public void A_return_with_no_lines_does_not_complete()
    {
        SalesReturn salesReturn = Raise(CompletedSale(1m, 115m, 100m, 15m));

        Action completing = () => salesReturn.Complete(Now);

        completing.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_RETURN_HAS_NO_LINES");
    }

    [Fact]
    public void A_completed_return_is_frozen()
    {
        Sale sale = CompletedSale(quantity: 3m, unitPrice: 115m, net: 300m, tax: 45m);
        SalesReturn salesReturn = Raise(sale);

        salesReturn.AddLine(sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);
        salesReturn.Complete(Now);

        salesReturn.Status.Should().Be(SalesReturnStatus.Completed);
        salesReturn.CompletedAt.Should().Be(Now);

        Action addingAfterwards = () => salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 1m);

        addingAfterwards.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_UNEXPECTED_RETURN_STATUS");
    }

    [Fact]
    public void A_refused_stock_receipt_is_recorded_on_the_line_and_does_not_undo_the_refund()
    {
        // ADR-073, applied to goods moving the other way. The customer is at the counter and the item
        // is already back over it, so the ledger's refusal becomes a reconciliation queue rather than a
        // failed refund.
        Sale sale = CompletedSale(quantity: 1m, unitPrice: 115m, net: 100m, tax: 15m);
        SalesReturn salesReturn = Raise(sale);

        SalesReturnLine line = salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        line.RecordStockReturnRefused("The location is closed to receipts.");

        line.StockReturn.Should().Be(StockReturnStatus.Refused);
        line.StockLedgerEntryId.Should().BeNull();
        salesReturn.Gross.Amount.Should().Be(115m);
    }

    [Fact]
    public void A_return_line_carries_the_original_issue_entry_so_the_goods_come_back_at_what_they_left_at()
    {
        // The cost is on the ledger entry, not on the sale line — Stage 09 stored what the customer
        // paid, which is right for a receipt and useless for a stock receipt.
        Guid issueEntryId = UuidV7.NewGuid();
        Sale sale = CompletedSale(quantity: 1m, unitPrice: 115m, net: 100m, tax: 15m);
        sale.Lines[0].RecordStockIssued(issueEntryId);

        SalesReturn salesReturn = Raise(sale);
        SalesReturnLine line = salesReturn.AddLine(
            sale.Lines[0], new Quantity(1m, "EA"), previouslyReturned: 0m);

        line.OriginalStockLedgerEntryId.Should().Be(issueEntryId);
        line.StockReturn.Should().Be(StockReturnStatus.Pending);
    }

    private static SalesReturn Raise(Sale sale)
        => SalesReturn.Raise(sale, "RTN-000001", "Faulty", TenderType.Cash, OperatorId, Now);

    private static Sale OpenSale()
    {
        TillSession session = TillSession.Open(
            TenantId, StoreId, TerminalId, OperatorId, Money.Zero("ZAR"), Now.AddHours(-2));

        return Sale.Open(
            UuidV7.NewGuid(), TenantId, StoreId, "SALE-000001", session, OperatorId, LocationId,
            customerId: null, "ZAR", Now.AddHours(-1));
    }

    private static Sale CompletedSale(
        decimal quantity, decimal unitPrice, decimal net, decimal tax, decimal discount = 0m)
    {
        Sale sale = OpenSale();

        sale.AddLine(SaleLine.Ring(
            TenantId,
            StoreId,
            sale.Id,
            1,
            ItemId,
            null,
            "Full cream milk 2L",
            new Quantity(quantity, "EA"),
            new Money(unitPrice, "ZAR"),
            new Money(discount, "ZAR"),
            "STANDARD",
            new Money(net, "ZAR"),
            new Money(tax, "ZAR"),
            new Money(net + tax, "ZAR")));

        sale.AddTender(SaleTender.Capture(
            TenantId, StoreId, sale.Id, TenderType.Cash, new Money(net + tax, "ZAR"), null, Now));

        sale.Complete(Now);

        return sale;
    }
}
