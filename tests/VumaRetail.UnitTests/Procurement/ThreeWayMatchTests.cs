using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.UnitTests.Procurement;

/// <summary>
/// The three-way match's arithmetic, exhaustively: ordered against received against invoiced, and
/// exactly where the two tolerances stop.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece of Stage 12 most worth testing at this level. Every interesting case is a
/// combination of three numbers and two bounds, and a pure comparison over five numbers can be tested
/// exhaustively where a handler running against a database cannot.
/// </para>
/// <para>
/// The rounding boundary is <c>PROGRESS.md</c> §4.14's regression written for this module. Rounding the
/// unit cost first and multiplying produces a variance made entirely of arithmetic nobody performed,
/// and it blocks a supplier's payment over it — which is worse than the sell-side version, because a
/// blocked invoice is a phone call.
/// </para>
/// </remarks>
public sealed class ThreeWayMatchTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Expected = new(2026, 8, 24);
    private static readonly DateOnly InvoiceDate = new(2026, 8, 25);

    [Fact]
    public void An_invoice_that_agrees_with_the_delivery_matches()
    {
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_000m);

        match.AddLine(
            line,
            invoicedQuantity: new Quantity(100m, "EA"),
            invoicedUnitCost: new Money(80m, "ZAR"),
            receivedQuantity: new Quantity(100m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        match.Status.Should().Be(ThreeWayMatchStatus.Matched);
        match.Variances.Should().Be(ThreeWayMatchVarianceKind.None);
        match.MatchedNet.Amount.Should().Be(8_000m);
        match.PriceVariance.Amount.Should().Be(0m);
        match.IsPayable.Should().BeTrue();
    }

    [Fact]
    public void Invoicing_for_more_than_arrived_blocks_with_no_tolerance()
    {
        // Business rule 11 has no tolerance and is not a variance to be judged: goods that have not
        // arrived cannot be paid for, whatever the amount.
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_000m);

        SupplierInvoiceMatchLine compared = match.AddLine(
            line,
            invoicedQuantity: new Quantity(100m, "EA"),
            invoicedUnitCost: new Money(80m, "ZAR"),
            receivedQuantity: new Quantity(94m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        compared.QuantityVariance.Value.Should().Be(6m);
        match.Status.Should().Be(ThreeWayMatchStatus.Blocked);
        match.Variances.Should().HaveFlag(ThreeWayMatchVarianceKind.Quantity);
        match.IsPayable.Should().BeFalse();
    }

    [Fact]
    public void Invoicing_for_less_than_arrived_is_not_a_fault()
    {
        // A supplier billing for less than they delivered is entitled to bill for the rest later.
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 4_800m);

        SupplierInvoiceMatchLine compared = match.AddLine(
            line,
            invoicedQuantity: new Quantity(60m, "EA"),
            invoicedUnitCost: new Money(80m, "ZAR"),
            receivedQuantity: new Quantity(100m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        compared.QuantityVariance.Value.Should().Be(-40m);
        match.Status.Should().Be(ThreeWayMatchStatus.Matched);
    }

    [Fact]
    public void Cumulative_invoicing_across_two_invoices_is_checked_against_one_delivery()
    {
        // Each invoice passes on its own; together they claim 110 against a delivery of 100. This is the
        // case a per-invoice check misses, and it is the one a supplier's accounts department produces
        // by accident about once a year.
        SupplierInvoiceMatch second = Match(out PurchaseOrderLine line, claimedNet: 4_000m);

        second.AddLine(
            line,
            invoicedQuantity: new Quantity(50m, "EA"),
            invoicedUnitCost: new Money(80m, "ZAR"),
            receivedQuantity: new Quantity(100m, "EA"),
            previouslyInvoicedQuantity: new Quantity(60m, "EA"));

        second.Status.Should().Be(ThreeWayMatchStatus.Blocked);
        second.Variances.Should().HaveFlag(ThreeWayMatchVarianceKind.Quantity);
    }

    [Theory]
    [InlineData(80.00, ThreeWayMatchStatus.Matched)]                    // Exact.
    [InlineData(81.60, ThreeWayMatchStatus.MatchedWithinTolerance)]     // Exactly 2% up: R160 on R8,000.
    [InlineData(81.59, ThreeWayMatchStatus.MatchedWithinTolerance)]     // Just inside.
    [InlineData(81.61, ThreeWayMatchStatus.Blocked)]                    // Just past it.
    [InlineData(78.40, ThreeWayMatchStatus.MatchedWithinTolerance)]     // 2% down — under-charging is a variance too.
    [InlineData(90.00, ThreeWayMatchStatus.Blocked)]                    // Comfortably past.
    public void The_percentage_tolerance_is_applied_to_the_extended_line(
        decimal invoicedUnitCost, ThreeWayMatchStatus expected)
    {
        // On the extended line, not the unit cost. A one-cent unit difference on 5,000 units is R50 and
        // matters; the same cent on 3 units does not.
        SupplierInvoiceMatch match = Match(
            out PurchaseOrderLine line, claimedNet: 8_000m, tolerancePercentage: 2m, toleranceFloor: 0m);

        match.AddLine(
            line,
            invoicedQuantity: new Quantity(100m, "EA"),
            invoicedUnitCost: new Money(invoicedUnitCost, "ZAR"),
            receivedQuantity: new Quantity(100m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        match.Status.Should().Be(expected);
    }

    [Fact]
    public void The_absolute_floor_saves_a_cheap_line_the_percentage_would_block()
    {
        // 2% of a R9.00 line is 18 cents, and nobody holds up a supplier's payment over 18 cents. Without
        // the floor, somebody would eventually raise the percentage until it stopped catching anything
        // worth catching — which is how a tolerance becomes decoration.
        SupplierInvoiceMatch match = Match(
            out PurchaseOrderLine line,
            claimedNet: 12m,
            tolerancePercentage: 2m,
            toleranceFloor: 10m,
            ordered: 3m,
            unitCost: 3m);

        match.AddLine(
            line,
            invoicedQuantity: new Quantity(3m, "EA"),
            invoicedUnitCost: new Money(4m, "ZAR"),
            receivedQuantity: new Quantity(3m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        // R3 over on a R9 line: a third, far past 2%, and still inside the R10 floor.
        match.Status.Should().Be(ThreeWayMatchStatus.MatchedWithinTolerance);
        match.Variances.Should().HaveFlag(ThreeWayMatchVarianceKind.Price);
    }

    [Fact]
    public void A_variance_past_both_bounds_blocks()
    {
        SupplierInvoiceMatch match = Match(
            out PurchaseOrderLine line,
            claimedNet: 30m,
            tolerancePercentage: 2m,
            toleranceFloor: 10m,
            ordered: 3m,
            unitCost: 3m);

        match.AddLine(
            line,
            invoicedQuantity: new Quantity(3m, "EA"),
            invoicedUnitCost: new Money(10m, "ZAR"),
            receivedQuantity: new Quantity(3m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        // R21 over on a R9 line — past the percentage and past the R10 floor.
        match.Status.Should().Be(ThreeWayMatchStatus.Blocked);
    }

    [Fact]
    public void An_agreeing_invoice_on_a_fractional_unit_cost_does_not_manufacture_a_variance()
    {
        // §4.14's regression, on the buying side. 7 units at R13.333: rounding the unit cost to R13.33
        // first and multiplying gives R93.31 against a true R93.33 — a two-cent variance made entirely of
        // arithmetic nobody performed, on an invoice that agrees exactly. With a zero floor it would
        // block a supplier's payment.
        SupplierInvoiceMatch match = Match(
            out PurchaseOrderLine line,
            claimedNet: 93.33m,
            tolerancePercentage: 0m,
            toleranceFloor: 0m,
            ordered: 7m,
            unitCost: 13.333m);

        SupplierInvoiceMatchLine compared = match.AddLine(
            line,
            invoicedQuantity: new Quantity(7m, "EA"),
            invoicedUnitCost: new Money(13.333m, "ZAR"),
            receivedQuantity: new Quantity(7m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        compared.OrderedValue.Amount.Should().Be(93.33m);
        compared.InvoicedValue.Amount.Should().Be(93.33m);
        compared.PriceVariance.Amount.Should().Be(0m);
        match.Status.Should().Be(ThreeWayMatchStatus.Matched);
    }

    [Fact]
    public void The_half_cent_boundary_rounds_away_from_zero_on_both_sides()
    {
        // ADR-033's midpoint rule, asserted on the extended amount. 3 at R1.6683 is R5.0049; 3 at R1.6685
        // is R5.0055, which rounds to R5.01. The variance is the one cent that really exists, not a
        // rounding artefact — and it is inside the floor, so it passes with the variance recorded.
        SupplierInvoiceMatch match = Match(
            out PurchaseOrderLine line,
            claimedNet: 5.01m,
            tolerancePercentage: 0m,
            toleranceFloor: 10m,
            ordered: 3m,
            unitCost: 1.6683m);

        SupplierInvoiceMatchLine compared = match.AddLine(
            line,
            invoicedQuantity: new Quantity(3m, "EA"),
            invoicedUnitCost: new Money(1.6685m, "ZAR"),
            receivedQuantity: new Quantity(3m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        compared.OrderedValue.Amount.Should().Be(5.00m);
        compared.InvoicedValue.Amount.Should().Be(5.01m);
        compared.PriceVariance.Amount.Should().Be(0.01m);
        match.Status.Should().Be(ThreeWayMatchStatus.MatchedWithinTolerance);
    }

    [Fact]
    public void A_line_the_order_does_not_have_is_recorded_and_blocks()
    {
        // Not an exception. An invoice line the order does not have is the single most interesting thing
        // an invoice can contain, and throwing would leave the only trace in whoever keyed it in.
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_500m);

        match.AddLine(
            line,
            invoicedQuantity: new Quantity(100m, "EA"),
            invoicedUnitCost: new Money(80m, "ZAR"),
            receivedQuantity: new Quantity(100m, "EA"),
            previouslyInvoicedQuantity: new Quantity(0m, "EA"));

        SupplierInvoiceMatchLine unknown = match.AddUnknownLine(
            "Delivery surcharge", new Quantity(1m, "EA"), new Money(500m, "ZAR"));

        unknown.PurchaseOrderLineId.Should().BeNull();
        unknown.OrderedValue.Amount.Should().Be(0m);
        unknown.PriceVariance.Amount.Should().Be(500m);
        match.Status.Should().Be(ThreeWayMatchStatus.Blocked);
        match.Variances.Should().HaveFlag(ThreeWayMatchVarianceKind.UnknownLine);

        // The document-level variance is the claim against what the order supports, which is exactly the
        // surcharge nobody agreed to.
        match.MatchedNet.Amount.Should().Be(8_000m);
        match.PriceVariance.Amount.Should().Be(500m);
    }

    [Fact]
    public void One_blocked_line_blocks_the_whole_invoice()
    {
        // An invoice is paid or it is not. The worst line wins.
        SupplierInvoiceMatch match = TwoLineMatch(
            out PurchaseOrderLine first, out PurchaseOrderLine second);

        match.AddLine(
            first, new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            new Quantity(10m, "EA"), new Quantity(0m, "EA"));

        match.AddLine(
            second, new Quantity(4m, "EA"), new Money(200m, "ZAR"),
            new Quantity(4m, "EA"), new Quantity(0m, "EA"));

        match.Status.Should().Be(ThreeWayMatchStatus.Blocked);
    }

    [Fact]
    public void A_blocked_match_cannot_be_released()
    {
        // Business rule 13, and the one thing in this module that must never be softened: releasing a
        // blocked match is money out the door against a delivery nobody could evidence.
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_000m);

        match.AddLine(
            line, new Quantity(100m, "EA"), new Money(80m, "ZAR"),
            new Quantity(50m, "EA"), new Quantity(0m, "EA"));

        Action release = () => match.Release(UserId, Now);

        release.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_BLOCKED_MATCH_CANNOT_BE_RELEASED");

        match.IsReleased.Should().BeFalse();
    }

    [Fact]
    public void A_released_match_is_frozen()
    {
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_000m);

        match.AddLine(
            line, new Quantity(100m, "EA"), new Money(80m, "ZAR"),
            new Quantity(100m, "EA"), new Quantity(0m, "EA"));

        match.Release(UserId, Now);

        Action releaseAgain = () => match.Release(UserId, Now);
        Action addAfter = () => match.AddUnknownLine("Late", new Quantity(1m, "EA"), new Money(1m, "ZAR"));

        releaseAgain.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_MATCH_ALREADY_RELEASED");

        addAfter.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_MATCH_ALREADY_RELEASED");
    }

    [Fact]
    public void A_match_with_no_lines_cannot_be_released()
    {
        SupplierInvoiceMatch match = Match(out _, claimedNet: 8_000m);

        Action release = () => match.Release(UserId, Now);

        release.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_MATCH_HAS_NO_LINES");
    }

    [Fact]
    public void The_same_order_line_cannot_be_compared_twice_on_one_match()
    {
        SupplierInvoiceMatch match = Match(out PurchaseOrderLine line, claimedNet: 8_000m);

        match.AddLine(
            line, new Quantity(60m, "EA"), new Money(80m, "ZAR"),
            new Quantity(100m, "EA"), new Quantity(0m, "EA"));

        Action twice = () => match.AddLine(
            line, new Quantity(40m, "EA"), new Money(80m, "ZAR"),
            new Quantity(100m, "EA"), new Quantity(0m, "EA"));

        twice.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_DUPLICATE_ORDER_LINE_REFERENCE");
    }

    [Fact]
    public void A_claim_in_another_currency_is_refused()
    {
        PurchaseOrder order = IssuedOrder(out _, 100m, 80m);

        Action foreign = () => SupplierInvoiceMatch.Open(
            order, "INV-1", InvoiceDate, new Money(500m, "USD"), new Money(75m, "USD"),
            2m, new Money(10m, "ZAR"), Now);

        foreign.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_CURRENCY_MISMATCH");
    }

    private static SupplierInvoiceMatch Match(
        out PurchaseOrderLine line,
        decimal claimedNet,
        decimal tolerancePercentage = 2m,
        decimal toleranceFloor = 10m,
        decimal ordered = 100m,
        decimal unitCost = 80m)
    {
        PurchaseOrder order = IssuedOrder(out line, ordered, unitCost);

        return SupplierInvoiceMatch.Open(
            order,
            "INV-90210",
            InvoiceDate,
            new Money(claimedNet, "ZAR"),
            new Money(claimedNet, "ZAR") * 0.15m,
            tolerancePercentage,
            new Money(toleranceFloor, "ZAR"),
            Now);
    }

    private static SupplierInvoiceMatch TwoLineMatch(
        out PurchaseOrderLine first, out PurchaseOrderLine second)
    {
        PurchaseOrder order = PurchaseOrder.Raise(
            TenantId, StoreId, "PO-000001", PartnerId, "ZAR", LocationId, Expected, null, null, Now);

        first = order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(10m, "EA"), new Money(80m, "ZAR"),
            "STANDARD", new Money(800m, "ZAR"), new Money(120m, "ZAR"), null);

        second = order.AddLine(
            UuidV7.NewGuid(), null, "Filters, box of 100", new Quantity(4m, "EA"),
            new Money(45m, "ZAR"), "STANDARD", new Money(180m, "ZAR"), new Money(27m, "ZAR"), null);

        order.Approve(UserId, Now);
        order.Issue(Now);

        return SupplierInvoiceMatch.Open(
            order, "INV-90211", InvoiceDate, new Money(1_600m, "ZAR"), new Money(240m, "ZAR"),
            2m, new Money(10m, "ZAR"), Now);
    }

    private static PurchaseOrder IssuedOrder(
        out PurchaseOrderLine line, decimal ordered, decimal unitCost)
    {
        PurchaseOrder order = PurchaseOrder.Raise(
            TenantId, StoreId, "PO-000001", PartnerId, "ZAR", LocationId, Expected, null, null, Now);

        Money cost = new(unitCost, "ZAR");
        Money net = (cost * ordered).RoundToCurrencyScale();

        line = order.AddLine(
            ItemId, null, "Coffee beans, 1kg", new Quantity(ordered, "EA"), cost, "STANDARD",
            net, (net * 0.15m).RoundToCurrencyScale(), null);

        order.Approve(UserId, Now);
        order.Issue(Now);

        return order;
    }
}
