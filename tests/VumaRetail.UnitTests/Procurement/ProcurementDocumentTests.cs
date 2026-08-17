using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.UnitTests.Procurement;

/// <summary>
/// The buying chain's state machines and their invariants: a requisition commits nothing, a quote is
/// frozen, an issued order does not change, and nothing is received beyond what was bought.
/// </summary>
/// <remarks>
/// The over-receipt boundary cases are the ones worth reading. A tolerance compared without rounding
/// refuses a delivery of exactly the allowed quantity for a reason nobody on a loading dock could ever
/// discover, and it does so only at certain quantities — which is precisely the sort of defect that
/// survives a happy-path test suite.
/// </remarks>
public sealed class ProcurementDocumentTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Expected = new(2026, 8, 24);

    [Fact]
    public void A_requisition_cannot_be_submitted_with_no_lines()
    {
        PurchaseRequisition requisition = Requisition();

        Action submit = () => requisition.Submit(Now);

        submit.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_REQUISITION_HAS_NO_LINES");
    }

    [Fact]
    public void A_rejected_requisition_cannot_be_sourced()
    {
        // Business rule 1. The whole reason the document exists is to make buying something nobody
        // agreed to impossible to do by accident.
        PurchaseRequisition requisition = Requisition();
        PurchaseRequisitionLine line = requisition.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), null);

        requisition.Submit(Now);
        requisition.Reject(UserId, "Buy from the existing contract instead.", Now);

        Action source = () => requisition.RecordLineSourced(line.Id, UuidV7.NewGuid(), Now);

        source.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_UNEXPECTED_REQUISITION_STATUS");
    }

    [Fact]
    public void A_requisition_cannot_be_decided_twice()
    {
        PurchaseRequisition requisition = Requisition();
        requisition.AddLine(ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), null);
        requisition.Submit(Now);
        requisition.Approve(UserId, Now);

        Action approveAgain = () => requisition.Approve(UserId, Now);

        approveAgain.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_UNEXPECTED_REQUISITION_STATUS");
    }

    [Fact]
    public void A_quote_dated_after_the_rfq_closed_is_refused()
    {
        Rfq rfq = IssuedRfq(out _);

        Action late = () => rfq.RecordResponse(
            PartnerId, "ZAR", rfq.ClosesAt.AddSeconds(1), null, 7, null);

        late.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RFQ_ALREADY_CLOSED");
    }

    [Fact]
    public void One_supplier_cannot_quote_twice_against_the_same_rfq()
    {
        Rfq rfq = IssuedRfq(out _);
        rfq.RecordResponse(PartnerId, "ZAR", Now, null, 7, null);

        Action second = () => rfq.RecordResponse(PartnerId, "ZAR", Now, null, 5, null);

        second.Should().Throw<ProcurementConflictException>()
            .Which.Code.Should().Be("PROCUREMENT_DUPLICATE_RFQ_RESPONSE");
    }

    [Fact]
    public void Awarding_declines_every_other_quote_and_closes_the_rfq()
    {
        // Declining the losers is not tidiness. "We only ever had one quote" is a different story from
        // "we compared three" when somebody audits the purchase, and nothing else records it.
        Rfq rfq = IssuedRfq(out RfqLine line);
        Guid otherPartner = UuidV7.NewGuid();

        RfqResponse cheap = rfq.RecordResponse(PartnerId, "ZAR", Now, null, 7, null);
        RfqResponse dear = rfq.RecordResponse(otherPartner, "ZAR", Now, null, 3, null);

        rfq.AddResponseLine(cheap.Id, line.Id, new Money(80m, "ZAR"), null);
        rfq.AddResponseLine(dear.Id, line.Id, new Money(95m, "ZAR"), null);

        rfq.Award(cheap.Id, UserId, Now);

        rfq.Status.Should().Be(RfqStatus.Awarded);
        rfq.AwardedResponseId.Should().Be(cheap.Id);
        cheap.Status.Should().Be(RfqResponseStatus.Awarded);
        dear.Status.Should().Be(RfqResponseStatus.Declined);
    }

    [Fact]
    public void An_rfq_cannot_award_twice()
    {
        Rfq rfq = IssuedRfq(out RfqLine line);
        RfqResponse response = rfq.RecordResponse(PartnerId, "ZAR", Now, null, 7, null);
        rfq.AddResponseLine(response.Id, line.Id, new Money(80m, "ZAR"), null);
        rfq.Award(response.Id, UserId, Now);

        Action again = () => rfq.Award(response.Id, UserId, Now);

        again.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RFQ_ALREADY_AWARDED");
    }

    [Fact]
    public void An_awarded_quote_is_frozen()
    {
        Rfq rfq = IssuedRfq(out RfqLine line);
        RfqResponse response = rfq.RecordResponse(PartnerId, "ZAR", Now, null, 7, null);
        rfq.AddResponseLine(response.Id, line.Id, new Money(80m, "ZAR"), null);
        rfq.Award(response.Id, UserId, Now);

        Action edit = () => response.AddLine(line, new Money(70m, "ZAR"), null);

        edit.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RESPONSE_IS_FROZEN");
    }

    [Fact]
    public void A_partial_quote_is_extended_on_what_the_supplier_can_actually_supply()
    {
        // Comparing a partial quote's price against a full quote's total flatters the partial one. The
        // requested quantity is snapshotted alongside so a buyer can see which is which.
        Rfq rfq = IssuedRfq(out RfqLine line);
        RfqResponse response = rfq.RecordResponse(PartnerId, "ZAR", Now, null, 7, null);

        RfqResponseLine quoted = rfq.AddResponseLine(
            response.Id, line.Id, new Money(80m, "ZAR"), availableQuantity: 40m);

        quoted.QuotedQuantity.Value.Should().Be(40m);
        quoted.RequestedQuantity.Value.Should().Be(100m);
        quoted.IsFullQuantity.Should().BeFalse();
        quoted.ExtendedCost.Amount.Should().Be(3_200m);
        response.Total.Amount.Should().Be(3_200m);
    }

    [Fact]
    public void An_order_line_in_another_currency_is_refused()
    {
        // PROGRESS.md §4.13's regression, on the buying side. A document that lets a currency in through
        // a line is permanently broken by one abandoned foreign-currency line.
        PurchaseOrder order = Order();

        Action foreign = () => order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"),
            new Money(5m, "USD"), "STANDARD", new Money(50m, "USD"), new Money(7.5m, "USD"), null);

        foreign.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_CURRENCY_MISMATCH");
    }

    [Fact]
    public void An_order_totals_its_lines_and_the_parts_add_up()
    {
        PurchaseOrder order = Order();

        order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            "STANDARD", new Money(800m, "ZAR"), new Money(120m, "ZAR"), null);

        order.AddLine(
            UuidV7.NewGuid(), null, "Filters, box of 100", new Quantity(4m, "EA"),
            new Money(45m, "ZAR"), "STANDARD", new Money(180m, "ZAR"), new Money(27m, "ZAR"), null);

        order.Net.Amount.Should().Be(980m);
        order.Tax.Amount.Should().Be(147m);
        order.Gross.Amount.Should().Be(1_127m);
        (order.Net + order.Tax).Should().Be(order.Gross);
    }

    [Fact]
    public void An_approved_order_cannot_take_another_line()
    {
        PurchaseOrder order = IssuedOrder(out _);

        Action add = () => order.AddLine(
            ItemId, null, "Late addition", new Quantity(1m, "EA"), new Money(10m, "ZAR"),
            "STANDARD", new Money(10m, "ZAR"), new Money(1.5m, "ZAR"), null);

        add.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_UNEXPECTED_ORDER_STATUS");
    }

    [Fact]
    public void An_order_cannot_be_issued_without_being_approved()
    {
        PurchaseOrder order = Order();
        order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            "STANDARD", new Money(800m, "ZAR"), new Money(120m, "ZAR"), null);

        Action issue = () => order.Issue(Now);

        issue.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_UNEXPECTED_ORDER_STATUS");
    }

    [Fact]
    public void Amending_an_order_opens_a_new_version_and_cancels_the_one_it_supersedes()
    {
        // Business rule 3. A supplier holding PO-000123 for 100 cases has a document, and quietly
        // turning it into 60 in our database does not change what they are going to deliver.
        PurchaseOrder order = IssuedOrder(out _);

        PurchaseOrder replacement = order.Amend("PO-000124", "Quantity reduced after a stock count.", Now);

        replacement.Version.Should().Be(2);
        replacement.AmendsPurchaseOrderId.Should().Be(order.Id);
        replacement.Status.Should().Be(PurchaseOrderStatus.Draft);
        replacement.Lines.Should().BeEmpty();
        replacement.Currency.Should().Be(order.Currency);

        order.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Quantity reduced after a stock count.");
    }

    [Fact]
    public void Nothing_is_received_against_an_order_that_was_never_issued()
    {
        PurchaseOrder order = Order();
        PurchaseOrderLine line = order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            "STANDARD", new Money(800m, "ZAR"), new Money(120m, "ZAR"), null);

        Action receive = () => order.RecordReceipt(line.Id, new Quantity(10m, "EA"), 0m);

        receive.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_ORDER_NOT_OPEN_FOR_RECEIPT");
    }

    [Theory]
    [InlineData(100, 0, 100)]      // Exactly what was ordered, no tolerance.
    [InlineData(100, 5, 105)]      // Exactly at a 5% tolerance.
    [InlineData(100, 5, 104.9)]    // Just inside it.
    [InlineData(3, 5, 3.15)]       // The decimal-arithmetic boundary: 3 * 1.05 is not exactly 3.15.
    public void A_delivery_within_tolerance_is_accepted(
        decimal ordered, decimal tolerance, decimal delivered)
    {
        PurchaseOrder order = IssuedOrder(out PurchaseOrderLine line, ordered);

        order.RecordReceipt(line.Id, new Quantity(delivered, "EA"), tolerance);

        line.ReceivedQuantity.Value.Should().Be(delivered);
    }

    [Theory]
    [InlineData(100, 0, 100.000001)]  // A hair over, with no tolerance at all.
    [InlineData(100, 5, 105.000001)]  // A hair past a 5% tolerance.
    [InlineData(100, 5, 110)]         // Comfortably past it.
    public void A_delivery_beyond_tolerance_is_refused(
        decimal ordered, decimal tolerance, decimal delivered)
    {
        // Business rule 6. Accepting it silently is how a supplier bills for stock nobody agreed to buy.
        PurchaseOrder order = IssuedOrder(out PurchaseOrderLine line, ordered);

        Action receive = () => order.RecordReceipt(line.Id, new Quantity(delivered, "EA"), tolerance);

        receive.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RECEIPT_EXCEEDS_ORDERED");
    }

    [Fact]
    public void Two_receipts_against_one_line_are_checked_cumulatively()
    {
        // Each half passes on its own; the pair does not. This is the case a per-receipt check misses.
        PurchaseOrder order = IssuedOrder(out PurchaseOrderLine line, ordered: 100m);

        order.RecordReceipt(line.Id, new Quantity(60m, "EA"), 0m);

        Action second = () => order.RecordReceipt(line.Id, new Quantity(50m, "EA"), 0m);

        second.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RECEIPT_EXCEEDS_ORDERED");

        line.ReceivedQuantity.Value.Should().Be(60m);
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void An_order_is_only_fully_received_when_every_line_is()
    {
        // A two-line order with one line complete is not a delivered order, and treating it as one is
        // how the second half never gets chased.
        PurchaseOrder order = Order();

        PurchaseOrderLine first = order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            "STANDARD", new Money(800m, "ZAR"), new Money(120m, "ZAR"), null);

        PurchaseOrderLine second = order.AddLine(
            UuidV7.NewGuid(), null, "Filters", new Quantity(4m, "EA"), new Money(45m, "ZAR"),
            "STANDARD", new Money(180m, "ZAR"), new Money(27m, "ZAR"), null);

        order.Approve(UserId, Now);
        order.Issue(Now);

        order.RecordReceipt(first.Id, new Quantity(10m, "EA"), 0m);
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);

        order.RecordReceipt(second.Id, new Quantity(4m, "EA"), 0m);
        order.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void A_rejected_quantity_never_counts_as_received()
    {
        // Business rule 7. A delivery of 100 of which 6 are broken is not a delivery of 94 — the shop
        // received 100 and turned 6 away, and ninety days of those 6s are what a scorecard is made of.
        GoodsReceipt receipt = ReceiptFor(out PurchaseOrder order, out PurchaseOrderLine line);

        GoodsReceiptLine received = receipt.AddLine(
            line, new Quantity(94m, "EA"), new Quantity(6m, "EA"), GoodsRejectionReason.Damaged, null);

        order.RecordReceipt(line.Id, received.AcceptedQuantity, 0m);
        order.RecordRejection(line.Id, received.RejectedQuantity);

        line.ReceivedQuantity.Value.Should().Be(94m);
        line.RejectedQuantity.Value.Should().Be(6m);
        received.DeliveredQuantity.Value.Should().Be(100m);
        received.AcceptedValue.Amount.Should().Be(7_520m);
    }

    [Fact]
    public void A_rejection_without_a_reason_is_refused()
    {
        GoodsReceipt receipt = ReceiptFor(out _, out PurchaseOrderLine line);

        Action unreasoned = () => receipt.AddLine(
            line, new Quantity(94m, "EA"), new Quantity(6m, "EA"), GoodsRejectionReason.None, null);

        unreasoned.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_REJECTION_REASON_REQUIRED");
    }

    [Fact]
    public void A_reason_without_a_rejected_quantity_is_refused()
    {
        GoodsReceipt receipt = ReceiptFor(out _, out PurchaseOrderLine line);

        Action unbacked = () => receipt.AddLine(
            line, new Quantity(100m, "EA"), null, GoodsRejectionReason.Damaged, null);

        unbacked.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_REJECTION_REASON_REQUIRED");
    }

    [Fact]
    public void The_same_order_line_cannot_appear_twice_on_one_receipt()
    {
        // Two rows for one order line is exactly how an over-receipt slips past a per-line tolerance
        // check: each passes on its own and the pair does not.
        GoodsReceipt receipt = ReceiptFor(out _, out PurchaseOrderLine line);
        receipt.AddLine(line, new Quantity(60m, "EA"), null, GoodsRejectionReason.None, null);

        Action twice = () => receipt.AddLine(
            line, new Quantity(40m, "EA"), null, GoodsRejectionReason.None, null);

        twice.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_DUPLICATE_ORDER_LINE_REFERENCE");
    }

    [Fact]
    public void A_receipt_cannot_be_completed_with_no_lines()
    {
        GoodsReceipt receipt = ReceiptFor(out _, out _);

        Action complete = () => receipt.Complete(Now);

        complete.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_RECEIPT_HAS_NO_LINES");
    }

    [Fact]
    public void A_receipt_line_values_stock_at_the_orders_cost_rounded_once()
    {
        // Business rule 8, and §4.14's arithmetic. 7.5 kg at R13.333 a kilo is R99.9975 — rounded once
        // on the extended amount, that is R100.00. Rounding the unit cost first gives R99.98.
        GoodsReceipt receipt = ReceiptFor(
            out _, out PurchaseOrderLine line, unitCost: 13.333m, unitOfMeasure: "KG", ordered: 10m);

        GoodsReceiptLine received = receipt.AddLine(
            line, new Quantity(7.5m, "KG"), null, GoodsRejectionReason.None, null);

        received.AcceptedValue.Amount.Should().Be(100.00m);
        received.UnitCost.Amount.Should().Be(13.333m);
    }

    [Fact]
    public void Stock_is_valued_at_the_net_cost_when_the_order_was_priced_tax_inclusive()
    {
        // ADR-086, and the defect it was written for. A South African supplier quotes VAT-inclusive
        // (CLAUDE.md §9), so 120 at R18.50 is R2,220 gross — R1,930.43 net and R289.57 VAT. Valuing
        // stock at R18.50 capitalises recoverable input VAT into inventory, overstating the balance
        // sheet by the VAT rate and cost of sales when the goods sell. Worse, the receipt would credit
        // goods-received-not-invoiced gross while the three-way match debits it net, so the clearing
        // account would be short by the VAT on every purchase, forever.
        PurchaseOrder order = Order();

        PurchaseOrderLine line = order.AddLine(
            ItemId,
            null,
            "Full cream milk 2L",
            new Quantity(120m, "EA"),
            new Money(18.50m, "ZAR"),
            "STANDARD",
            new Money(1_930.43m, "ZAR"),
            new Money(289.57m, "ZAR"),
            null);

        order.Approve(UserId, Now);
        order.Issue(Now);

        line.NetUnitCost.Amount.Should().Be(16.0869m);

        GoodsReceipt receipt = GoodsReceipt.Open(order, "GRN-000001", "DN-1", UserId, Now);

        GoodsReceiptLine received = receipt.AddLine(
            line, new Quantity(116m, "EA"), new Quantity(4m, "EA"), GoodsRejectionReason.Expired, null);

        // Net, not the R2,146.00 the gross cost would have produced.
        received.UnitCost.Amount.Should().Be(16.0869m);
        received.AcceptedValue.Amount.Should().Be(1_866.08m);
        received.AcceptedValue.Should().BeLessThan(new Money(2_146m, "ZAR"));
    }

    [Fact]
    public void A_tax_exclusive_order_values_stock_at_the_cost_that_was_quoted()
    {
        // The other half of ADR-086: a tenant who is not VAT registered, or a wholesale order priced
        // exclusive, has net equal to the quoted cost and nothing changes for them.
        PurchaseOrder order = IssuedOrder(out PurchaseOrderLine line, ordered: 100m, unitCost: 80m);
        GoodsReceipt receipt = GoodsReceipt.Open(order, "GRN-000002", null, UserId, Now);

        GoodsReceiptLine received = receipt.AddLine(
            line, new Quantity(100m, "EA"), null, GoodsRejectionReason.None, null);

        received.UnitCost.Amount.Should().Be(80m);
        received.AcceptedValue.Amount.Should().Be(8_000m);
    }

    private static PurchaseRequisition Requisition()
        => PurchaseRequisition.Raise(
            TenantId, StoreId, "REQ-000001", UserId, LocationId, Expected,
            "The back office has run out of coffee.", Now);

    private static Rfq IssuedRfq(out RfqLine line)
    {
        Rfq rfq = Rfq.Raise(
            TenantId, StoreId, "RFQ-000001", "Coffee for the back office", null, Now.AddDays(3), Now);

        line = rfq.AddLine(ItemId, null, "Coffee beans, 1kg", new Quantity(100m, "EA"), null, null);
        rfq.Issue(Now);

        return rfq;
    }

    private static PurchaseOrder Order()
        => PurchaseOrder.Raise(
            TenantId, StoreId, "PO-000001", PartnerId, "ZAR", LocationId, Expected, null, null, Now);

    private static PurchaseOrder IssuedOrder(
        out PurchaseOrderLine line,
        decimal ordered = 100m,
        decimal unitCost = 80m,
        string unitOfMeasure = "EA")
    {
        PurchaseOrder order = Order();

        Money cost = new(unitCost, "ZAR");
        Money net = (cost * ordered).RoundToCurrencyScale();
        Money tax = (net * 0.15m).RoundToCurrencyScale();

        line = order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(ordered, unitOfMeasure), cost,
            "STANDARD", net, tax, null);

        order.Approve(UserId, Now);
        order.Issue(Now);

        return order;
    }

    private static GoodsReceipt ReceiptFor(
        out PurchaseOrder order,
        out PurchaseOrderLine line,
        decimal unitCost = 80m,
        string unitOfMeasure = "EA",
        decimal ordered = 100m)
    {
        order = IssuedOrder(out line, ordered, unitCost, unitOfMeasure);

        return GoodsReceipt.Open(order, "GRN-000001", "DN-99812", UserId, Now);
    }
}
