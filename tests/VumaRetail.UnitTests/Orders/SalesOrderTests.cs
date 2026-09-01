using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// The order aggregate's own invariants: a delivery order needs an address, lines can only be added
/// while draft, and <see cref="SalesOrder.Status"/> is always derived from its lines, never set
/// directly (business rule 1's "no reservation ledger" extended to the order's own status).
/// </summary>
public sealed class SalesOrderTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid VariantId = UuidV7.NewGuid();
    private static readonly Address DeliveryAddress = Address.Create("12 Rivonia Road", "Sandton", "ZA");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static SalesOrder NewDraft(
        OrderFulfilmentType fulfilmentType = OrderFulfilmentType.Delivery, Address? address = null)
        => SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", partnerId: null, SalesChannel.Phone, fulfilmentType, LocationId,
            fulfilmentType == OrderFulfilmentType.Delivery ? (address ?? DeliveryAddress) : null, "ZAR", Now, null);

    [Fact]
    public void A_delivery_order_requires_a_delivery_address()
    {
        Action creating = () => SalesOrder.Create(
            TenantId, StoreId, "ORD-000001", null, SalesChannel.Phone, OrderFulfilmentType.Delivery, LocationId,
            deliveryAddress: null, "ZAR", Now, null);

        creating.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_DELIVERY_REQUIRES_ADDRESS");
    }

    [Fact]
    public void Click_and_collect_carries_no_delivery_address_even_if_one_is_supplied()
    {
        SalesOrder order = SalesOrder.Create(
            TenantId, StoreId, "ORD-000002", null, SalesChannel.InStore, OrderFulfilmentType.ClickAndCollect, LocationId,
            DeliveryAddress, "ZAR", Now, null);

        order.DeliveryAddress.Should().BeNull();
    }

    [Fact]
    public void A_walk_in_order_needs_no_partner()
    {
        // Business rule 10.
        SalesOrder order = NewDraft();

        order.PartnerId.Should().BeNull();
    }

    [Fact]
    public void A_line_can_only_be_added_while_the_order_is_a_draft()
    {
        SalesOrder order = NewDraft();
        order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        order.Confirm(Now);

        Action adding = () => order.AddLine(ItemId, null, new Quantity(1m, "EA"));

        adding.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_ORDER_NOT_DRAFT");
    }

    [Fact]
    public void Confirming_an_order_with_no_lines_is_refused()
    {
        SalesOrder order = NewDraft();

        Action confirming = () => order.Confirm(Now);

        confirming.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_ORDER_HAS_NO_LINES");
    }

    [Fact]
    public void Confirming_moves_the_order_to_confirmed_and_totals_the_lines()
    {
        SalesOrder order = NewDraft();
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(2m, "EA"));
        line.ApplyPricing(new Money(50m, "ZAR"), Money.Zero("ZAR"), new Money(6.52m, "ZAR"), null, "STANDARD");

        order.Confirm(Now);

        order.Status.Should().Be(SalesOrderStatus.Confirmed);
        order.Net.Amount.Should().Be(100m);
        order.Tax.Amount.Should().Be(6.52m);
        order.Gross.Amount.Should().Be(106.52m);
    }

    [Theory]
    [InlineData(SalesOrderLineStatus.Allocated, SalesOrderLineStatus.Allocated, SalesOrderStatus.Allocated)]
    [InlineData(SalesOrderLineStatus.Allocated, SalesOrderLineStatus.PartiallyAllocated, SalesOrderStatus.PartiallyAllocated)]
    [InlineData(SalesOrderLineStatus.Fulfilled, SalesOrderLineStatus.Fulfilled, SalesOrderStatus.Fulfilled)]
    [InlineData(SalesOrderLineStatus.Fulfilled, SalesOrderLineStatus.PartiallyFulfilled, SalesOrderStatus.PartiallyFulfilled)]
    [InlineData(SalesOrderLineStatus.Fulfilled, SalesOrderLineStatus.Backordered, SalesOrderStatus.PartiallyFulfilled)]
    [InlineData(SalesOrderLineStatus.Backordered, SalesOrderLineStatus.Backordered, SalesOrderStatus.Confirmed)]
    public void The_order_status_is_derived_from_its_lines(
        SalesOrderLineStatus firstLine, SalesOrderLineStatus secondLine, SalesOrderStatus expected)
    {
        SalesOrder order = NewDraft();
        SalesOrderLine line1 = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        SalesOrderLine line2 = order.AddLine(null, VariantId, new Quantity(1m, "EA"));
        line1.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        line2.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);

        SetLineStatus(line1, firstLine);
        SetLineStatus(line2, secondLine);
        order.RecomputeStatus();

        order.Status.Should().Be(expected);
    }

    [Fact]
    public void A_cancelled_line_is_excluded_from_the_orders_own_status()
    {
        SalesOrder order = NewDraft();
        SalesOrderLine line1 = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        SalesOrderLine line2 = order.AddLine(null, VariantId, new Quantity(1m, "EA"));
        line1.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        line2.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);

        line1.RecordAllocationOutcome(Quantity.Zero("EA"));
        line2.Cancel();
        order.RecomputeStatus();

        order.Status.Should().Be(SalesOrderStatus.Allocated);
    }

    [Fact]
    public void Revenue_is_recognised_once_and_a_second_call_is_a_no_op()
    {
        // Business rule 5.
        SalesOrder order = NewDraft();
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(new Money(10m, "ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(1m, "EA"));

        bool firstCall = order.RecogniseRevenueIfDue(Now);
        bool secondCall = order.RecogniseRevenueIfDue(Now.AddMinutes(1));

        firstCall.Should().BeTrue();
        secondCall.Should().BeFalse();
        order.IsRevenueRecognised.Should().BeTrue();
        order.Status.Should().Be(SalesOrderStatus.Fulfilled);
    }

    [Fact]
    public void Revenue_cannot_be_recognised_while_a_line_is_neither_shipped_nor_cancelled()
    {
        SalesOrder order = NewDraft();
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordFulfilmentOutcome(new Quantity(1m, "EA"), Quantity.Zero("EA")); // allocated, not shipped

        Action completing = () => order.RecogniseRevenueIfDue(Now);

        completing.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_NOT_FULLY_ACCOUNTED_FOR");
    }

    [Fact]
    public void A_confirmed_order_with_nothing_shipped_can_be_cancelled()
    {
        SalesOrder order = NewDraft();
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);

        order.EnsureCancellable();
        line.Cancel();
        order.MarkCancelled("Customer changed their mind", "user:abc", Now);

        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        order.CancelReason.Should().Be("Customer changed their mind");
    }

    [Fact]
    public void An_order_with_a_shipped_line_cannot_be_bulk_cancelled()
    {
        // Business rule 7 — cancel the open lines individually, or raise a return for what shipped.
        SalesOrder order = NewDraft();
        SalesOrderLine line = order.AddLine(ItemId, null, new Quantity(1m, "EA"));
        line.ApplyPricing(Money.Zero("ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), null, string.Empty);
        order.Confirm(Now);
        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(1m, "EA"));

        Action cancelling = () => order.EnsureCancellable();

        cancelling.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_CANNOT_CANCEL_FULFILLED_ORDER");
    }

    [Fact]
    public void Requiring_an_unknown_line_is_not_found()
    {
        SalesOrder order = NewDraft();

        Action requiring = () => order.RequireLine(UuidV7.NewGuid());

        requiring.Should().Throw<OrdersNotFoundException>();
    }

    private static void SetLineStatus(SalesOrderLine line, SalesOrderLineStatus status)
    {
        switch (status)
        {
            case SalesOrderLineStatus.Allocated:
                line.RecordAllocationOutcome(Quantity.Zero(line.RequestedQuantity.UnitOfMeasure));
                break;
            case SalesOrderLineStatus.PartiallyAllocated:
                line.RecordAllocationOutcome(new Quantity(line.RequestedQuantity.Value / 2m, line.RequestedQuantity.UnitOfMeasure));
                break;
            case SalesOrderLineStatus.Backordered:
                line.RecordAllocationOutcome(line.RequestedQuantity);
                break;
            case SalesOrderLineStatus.Fulfilled:
                line.RecordFulfilmentOutcome(Quantity.Zero(line.RequestedQuantity.UnitOfMeasure), line.RequestedQuantity);
                break;
            case SalesOrderLineStatus.PartiallyFulfilled:
                line.RecordFulfilmentOutcome(
                    Quantity.Zero(line.RequestedQuantity.UnitOfMeasure), new Quantity(line.RequestedQuantity.Value / 2m, line.RequestedQuantity.UnitOfMeasure));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Not needed by these tests.");
        }
    }
}

/// <summary>
/// The order line's own invariants: pricing snapshots once, allocation and fulfilment outcomes are
/// recorded from the outside (Stage 13 is the source of truth), and a line that has shipped anything
/// cannot be cancelled.
/// </summary>
public sealed class SalesOrderLineTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid SalesOrderId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid VariantId = UuidV7.NewGuid();

    private static SalesOrderLine NewLine(decimal quantity = 10m)
        => SalesOrderLine.Create(TenantId, StoreId, SalesOrderId, ItemId, null, new Quantity(quantity, "EA"), "ZAR");

    [Fact]
    public void Exactly_one_of_item_or_variant_is_required()
    {
        Action bothSet = () => SalesOrderLine.Create(
            TenantId, StoreId, SalesOrderId, ItemId, VariantId, new Quantity(1m, "EA"), "ZAR");
        Action neitherSet = () => SalesOrderLine.Create(
            TenantId, StoreId, SalesOrderId, null, null, new Quantity(1m, "EA"), "ZAR");

        bothSet.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_EXACTLY_ONE_ITEM_OR_VARIANT");
        neitherSet.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    [Fact]
    public void The_requested_quantity_must_be_positive()
    {
        Action creating = () => SalesOrderLine.Create(
            TenantId, StoreId, SalesOrderId, ItemId, null, new Quantity(0m, "EA"), "ZAR");

        creating.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_new_line_starts_pending_with_no_price_and_no_backorder()
    {
        SalesOrderLine line = NewLine();

        line.LineStatus.Should().Be(SalesOrderLineStatus.Pending);
        line.UnitPrice.Amount.Should().Be(0m);
        line.BackorderedQuantity.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Applying_pricing_snapshots_the_resolution_onto_the_line()
    {
        // Business rule 9 — an expired promotion never silently reprices a confirmed order.
        SalesOrderLine line = NewLine();
        Guid priceListId = UuidV7.NewGuid();

        line.ApplyPricing(new Money(59.99m, "ZAR"), new Money(5m, "ZAR"), new Money(7.17m, "ZAR"), priceListId, "3 for R150");

        line.UnitPrice.Amount.Should().Be(59.99m);
        line.DiscountAmount.Amount.Should().Be(5m);
        line.TaxAmount.Amount.Should().Be(7.17m);
        line.PriceListId.Should().Be(priceListId);
        line.PromotionsSummary.Should().Be("3 for R150");
    }

    [Fact]
    public void Full_allocation_leaves_no_backorder()
    {
        SalesOrderLine line = NewLine(10m);

        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Allocated);
        line.BackorderedQuantity.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Partial_allocation_splits_the_line_into_allocated_and_backordered()
    {
        SalesOrderLine line = NewLine(10m);

        line.RecordAllocationOutcome(new Quantity(4m, "EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyAllocated);
        line.BackorderedQuantity.Value.Should().Be(4m);
    }

    [Fact]
    public void Nothing_fitting_backorders_the_whole_line()
    {
        SalesOrderLine line = NewLine(10m);

        line.RecordAllocationOutcome(new Quantity(10m, "EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Backordered);
    }

    [Fact]
    public void A_full_ship_with_no_backorder_marks_the_line_fulfilled()
    {
        SalesOrderLine line = NewLine(10m);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(10m, "EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.Fulfilled);
    }

    [Fact]
    public void A_partial_ship_marks_the_line_partially_fulfilled()
    {
        SalesOrderLine line = NewLine(10m);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(6m, "EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyFulfilled);
    }

    [Fact]
    public void A_short_pick_is_partially_fulfilled_never_backordered()
    {
        // Business rule 4's worked example: 10 requested and allocated in full, but only 8 were
        // actually picked off the shelf. Stage 13's problem (a discovered stock discrepancy), never
        // this stage's Backordered (demand this stage never had stock to promise in the first place).
        SalesOrderLine line = NewLine(10m);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));

        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(8m, "EA"));

        line.LineStatus.Should().Be(SalesOrderLineStatus.PartiallyFulfilled);
        line.LineStatus.Should().NotBe(SalesOrderLineStatus.Backordered);
    }

    [Fact]
    public void A_fulfilled_line_cannot_be_cancelled()
    {
        SalesOrderLine line = NewLine(10m);
        line.RecordAllocationOutcome(Quantity.Zero("EA"));
        line.RecordFulfilmentOutcome(Quantity.Zero("EA"), new Quantity(10m, "EA"));

        Action cancelling = () => line.Cancel();

        cancelling.Should().Throw<OrdersRuleException>().Which.Code.Should().Be("ORDERS_CANNOT_CANCEL_FULFILLED_LINE");
    }

    [Fact]
    public void A_pending_line_can_be_cancelled_and_its_backorder_zeroed()
    {
        SalesOrderLine line = NewLine(10m);
        line.RecordAllocationOutcome(new Quantity(10m, "EA"));

        line.Cancel();

        line.LineStatus.Should().Be(SalesOrderLineStatus.Cancelled);
        line.BackorderedQuantity.IsZero.Should().BeTrue();
    }
}
