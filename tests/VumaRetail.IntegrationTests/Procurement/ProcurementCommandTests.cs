using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Procurement.Commands;
using VumaRetail.Application.Procurement.Queries;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Procurement;

/// <summary>
/// The <c>procurement</c> module against real PostgreSQL: the whole buying chain end to end, the
/// three-way match's two verdicts, and the database constraints that hold when the aggregate is
/// bypassed.
/// </summary>
/// <remarks>
/// The constraint tests write through raw SQL on purpose. A check constraint that is only ever
/// exercised through the aggregate that already enforces the same rule has not been tested — it has
/// been assumed, and the whole reason a blocked match's release is also a constraint is the case where
/// something other than the aggregate writes the row.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ProcurementCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task The_whole_chain_runs_from_requisition_to_a_released_supplier_invoice()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        // 1. Somebody says they need something, and somebody senior agrees.
        Guid requisitionId = await harness.SendAsync(new CreatePurchaseRequisitionCommand(
            harness.LocationId, Expected(harness), "The back office has run out of coffee."));

        Guid requisitionLineId = await harness.SendAsync(new AddPurchaseRequisitionLineCommand(
            requisitionId, harness.ItemId, null, "Coffee beans, 1kg",
            new Quantity(100m, "EA"), new Money(85m, "ZAR")));

        await harness.SendAsync(new SubmitPurchaseRequisitionCommand(requisitionId));
        await harness.SendAsync(new DecidePurchaseRequisitionCommand(requisitionId, Approve: true, null));

        // 2. The buyer asks two suppliers, and picks one.
        Guid rfqId = await harness.SendAsync(new CreateRfqCommand(
            "Coffee for the back office", requisitionId, harness.Clock.UtcNow.AddDays(3)));

        Guid rfqLineId = await harness.SendAsync(new AddRfqLineCommand(
            rfqId, harness.ItemId, null, "Coffee beans, 1kg", new Quantity(100m, "EA"),
            "Medium roast, whole bean.", requisitionLineId));

        await harness.SendAsync(new IssueRfqCommand(rfqId));

        Guid responseId = await harness.SendAsync(new RecordRfqResponseCommand(
            rfqId, harness.SupplierId, "ZAR", harness.Clock.UtcNow, null, 7, "Free delivery over R5,000."));

        await harness.SendAsync(new AddRfqResponseLineCommand(
            rfqId, responseId, rfqLineId, new Money(80m, "ZAR"), null));

        await harness.SendAsync(new AwardRfqCommand(rfqId, responseId));

        // 3. The commitment.
        Guid orderId = await harness.SendAsync(new CreatePurchaseOrderCommand(
            harness.SupplierId, "ZAR", harness.LocationId, Expected(harness), responseId, null));

        Guid orderLineId = await harness.SendAsync(new AddPurchaseOrderLineCommand(
            orderId, harness.ItemId, null, "Coffee beans, 1kg", new Quantity(100m, "EA"),
            new Money(80m, "ZAR"), "STANDARD", requisitionLineId));

        await harness.SendAsync(new ApprovePurchaseOrderCommand(orderId, Issue: true));

        PurchaseOrder issued = await harness.QueryAsync(new GetPurchaseOrderQuery(orderId));
        issued.Status.Should().Be(PurchaseOrderStatus.Issued);
        issued.Net.Amount.Should().Be(8_000m);
        issued.Tax.Amount.Should().Be(1_200m);
        issued.Gross.Amount.Should().Be(9_200m);

        // 4. The goods arrive, and stock really moves at the order's cost.
        Guid receiptId = await harness.SendAsync(new CreateGoodsReceiptCommand(orderId, "DN-99812", null));

        await harness.SendAsync(new AddGoodsReceiptLineCommand(
            receiptId, orderLineId, new Quantity(100m, "EA"), null, GoodsRejectionReason.None, null));

        GoodsReceiptCompletionResult completion = await harness.SendAsync(
            new CompleteGoodsReceiptCommand(receiptId));

        completion.StockPostingsRefused.Should().Be(0);
        completion.ReceivedValue.Amount.Should().Be(8_000m);
        completion.OrderStatus.Should().Be(PurchaseOrderStatus.Received);

        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance.Should().NotBeNull();
        balance!.QuantityOnHand.Value.Should().Be(100m);
        balance.AverageCost.Amount.Should().Be(80m);

        // 5. The supplier's invoice is checked against the order and the delivery, then released.
        ThreeWayMatchResult matched = await harness.SendAsync(new MatchSupplierInvoiceCommand(
            orderId,
            "INV-90210",
            DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime),
            new Money(8_000m, "ZAR"),
            new Money(1_200m, "ZAR"),
            [
                new SupplierInvoiceLineInput(
                    orderLineId, "Coffee beans, 1kg", new Quantity(100m, "EA"), new Money(80m, "ZAR")),
            ]));

        matched.Status.Should().Be(ThreeWayMatchStatus.Matched);
        matched.IsPayable.Should().BeTrue();
        matched.PriceVariance.Amount.Should().Be(0m);

        SupplierInvoiceReleaseResult released = await harness.SendAsync(
            new ReleaseSupplierInvoiceCommand(matched.SupplierInvoiceMatchId));

        released.Gross.Amount.Should().Be(9_200m);
        released.JournalId.Should().Be(harness.MatchEvents.JournalId);

        // Exactly one financial event, carrying the supplier's own invoice number and no GL account.
        harness.MatchEvents.Events.Should().ContainSingle();
        harness.MatchEvents.Events[0].SupplierInvoiceNumber.Should().Be("INV-90210");
        harness.MatchEvents.Events[0].Net.Amount.Should().Be(8_000m);
        harness.MatchEvents.Events[0].Gross.Amount.Should().Be(9_200m);

        // And the order's invoiced total moved, which is what makes the next invoice cumulative.
        PurchaseOrder afterRelease = await harness.QueryAsync(new GetPurchaseOrderQuery(orderId));
        afterRelease.Lines[0].InvoicedQuantity.Value.Should().Be(100m);
    }

    [Fact]
    public async Task A_blocked_match_is_written_readable_and_cannot_be_released()
    {
        // ADR-082. "Why did we not pay this invoice" is a question somebody asks three weeks later, and
        // a rejected comparison that left no row cannot answer it.
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await ReceivedOrderAsync(harness, ordered: 100m, received: 60m);

        ThreeWayMatchResult matched = await harness.SendAsync(new MatchSupplierInvoiceCommand(
            orderId,
            "INV-90211",
            DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime),
            new Money(8_000m, "ZAR"),
            new Money(1_200m, "ZAR"),
            [
                new SupplierInvoiceLineInput(
                    orderLineId, "Coffee beans, 1kg", new Quantity(100m, "EA"), new Money(80m, "ZAR")),
            ]));

        matched.Status.Should().Be(ThreeWayMatchStatus.Blocked);
        matched.IsPayable.Should().BeFalse();

        // It is a real, readable document — not a refusal that vanished.
        SupplierInvoiceMatch stored = await harness.QueryAsync(
            new GetSupplierInvoiceMatchQuery(matched.SupplierInvoiceMatchId));

        stored.Variances.Should().HaveFlag(ThreeWayMatchVarianceKind.Quantity);
        stored.Lines.Should().ContainSingle();
        stored.Lines[0].QuantityVariance.Value.Should().Be(40m);

        Func<Task> release = () => harness.SendAsync(
            new ReleaseSupplierInvoiceCommand(matched.SupplierInvoiceMatchId));

        (await release.Should().ThrowAsync<ProcurementRuleException>())
            .Which.Code.Should().Be("PROCUREMENT_BLOCKED_MATCH_CANNOT_BE_RELEASED");

        harness.MatchEvents.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_supplier_invoice_cannot_be_matched_against_one_order_twice()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await ReceivedOrderAsync(harness, 100m, 100m);

        DateOnly invoiceDate = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        MatchSupplierInvoiceCommand command = new(
            orderId,
            "INV-DUP",
            invoiceDate,
            new Money(8_000m, "ZAR"),
            new Money(1_200m, "ZAR"),
            [
                new SupplierInvoiceLineInput(
                    orderLineId, "Coffee beans, 1kg", new Quantity(100m, "EA"), new Money(80m, "ZAR")),
            ]);

        await harness.SendAsync(command);

        Func<Task> again = () => harness.SendAsync(command);

        (await again.Should().ThrowAsync<ProcurementConflictException>())
            .Which.Code.Should().Be("PROCUREMENT_DUPLICATE_SUPPLIER_INVOICE");
    }

    [Fact]
    public async Task A_rejected_quantity_never_reaches_the_stock_ledger()
    {
        // Business rule 7, proven against a real balance rather than against the aggregate that already
        // says so.
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await IssuedOrderAsync(harness, ordered: 100m);

        Guid receiptId = await harness.SendAsync(new CreateGoodsReceiptCommand(orderId, "DN-1", null));

        await harness.SendAsync(new AddGoodsReceiptLineCommand(
            receiptId, orderLineId, new Quantity(94m, "EA"), new Quantity(6m, "EA"),
            GoodsRejectionReason.Damaged, "Six bags split in transit."));

        await harness.SendAsync(new CompleteGoodsReceiptCommand(receiptId));

        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance!.QuantityOnHand.Value.Should().Be(94m);

        PurchaseOrder order = await harness.QueryAsync(new GetPurchaseOrderQuery(orderId));
        order.Lines[0].ReceivedQuantity.Value.Should().Be(94m);
        order.Lines[0].RejectedQuantity.Value.Should().Be(6m);

        // Not fully received: 6 are still owed, and the order says so rather than closing itself.
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public async Task An_over_receipt_beyond_tolerance_is_refused_and_moves_no_stock()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await IssuedOrderAsync(harness, ordered: 100m);

        Guid receiptId = await harness.SendAsync(new CreateGoodsReceiptCommand(orderId, "DN-2", null));

        await harness.SendAsync(new AddGoodsReceiptLineCommand(
            receiptId, orderLineId, new Quantity(120m, "EA"), null, GoodsRejectionReason.None, null));

        Func<Task> complete = () => harness.SendAsync(new CompleteGoodsReceiptCommand(receiptId));

        (await complete.Should().ThrowAsync<ProcurementRuleException>())
            .Which.Code.Should().Be("PROCUREMENT_RECEIPT_EXCEEDS_ORDERED");

        // The pipeline's transaction rolled it back, so nothing moved and the receipt is still a draft.
        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance.Should().BeNull();
    }

    [Fact]
    public async Task An_order_to_a_partner_who_is_not_a_supplier_is_refused()
    {
        // Nothing in the database stops it — CONVENTIONS.md §2 forbids the cross-schema foreign key — so
        // this is the application layer's job, and this test is the only thing that proves it happens.
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        Func<Task> order = () => harness.SendAsync(new CreatePurchaseOrderCommand(
            harness.CustomerOnlyPartnerId, "ZAR", harness.LocationId, Expected(harness), null, null));

        (await order.Should().ThrowAsync<ProcurementRuleException>())
            .Which.Code.Should().Be("PROCUREMENT_PARTNER_IS_NOT_A_SUPPLIER");
    }

    [Fact]
    public async Task An_rfq_cannot_be_raised_from_an_unapproved_requisition()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        Guid requisitionId = await harness.SendAsync(new CreatePurchaseRequisitionCommand(
            harness.LocationId, Expected(harness), "Still being written."));

        Func<Task> rfq = () => harness.SendAsync(new CreateRfqCommand(
            "Too early", requisitionId, harness.Clock.UtcNow.AddDays(3)));

        (await rfq.Should().ThrowAsync<ProcurementRuleException>())
            .Which.Code.Should().Be("PROCUREMENT_UNEXPECTED_REQUISITION_STATUS");
    }

    [Fact]
    public async Task A_scorecard_counts_a_closed_period_and_cannot_be_taken_twice()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, _) = await ReceivedOrderAsync(harness, ordered: 100m, received: 90m);

        PurchaseOrder order = await harness.QueryAsync(new GetPurchaseOrderQuery(orderId));

        DateOnly periodStart = new(order.ExpectedAt.Year, order.ExpectedAt.Month, 1);
        DateOnly periodEnd = periodStart.AddMonths(1).AddDays(-1);

        // Move past the period so it has actually closed (business rule 15).
        harness.Clock.Advance(TimeSpan.FromDays(60));

        Guid scorecardId = await harness.SendAsync(new SnapshotSupplierScorecardCommand(
            harness.SupplierId, periodStart, periodEnd, "ZAR"));

        SupplierScorecard? scorecard = await harness.Scorecards.FindAsync(
            harness.SupplierId, periodStart, periodEnd);

        scorecard.Should().NotBeNull();
        scorecard!.Id.Should().Be(scorecardId);
        scorecard.OrdersPlaced.Should().Be(1);
        scorecard.LinesOrdered.Should().Be(1);
        scorecard.LinesDelivered.Should().Be(1);
        scorecard.QuantityOrdered.Should().Be(100m);
        scorecard.QuantityReceived.Should().Be(90m);
        scorecard.FillRate.Should().Be(90m);
        scorecard.PurchaseValue.Amount.Should().Be(8_000m);

        Func<Task> again = () => harness.SendAsync(new SnapshotSupplierScorecardCommand(
            harness.SupplierId, periodStart, periodEnd, "ZAR"));

        (await again.Should().ThrowAsync<ProcurementConflictException>())
            .Which.Code.Should().Be("PROCUREMENT_DUPLICATE_SCORECARD");
    }

    [Fact]
    public async Task A_scorecard_over_a_period_that_has_not_closed_is_refused()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        Func<Task> open = () => harness.SendAsync(new SnapshotSupplierScorecardCommand(
            harness.SupplierId, today.AddDays(-10), today.AddDays(10), "ZAR"));

        (await open.Should().ThrowAsync<ProcurementRuleException>())
            .Which.Code.Should().Be("PROCUREMENT_SCORECARD_PERIOD_NOT_CLOSED");
    }

    [Fact]
    public async Task The_database_refuses_a_released_blocked_match_written_around_the_aggregate()
    {
        // The one constraint in this schema worth the most: a blocked match that reaches a payment run
        // is money out the door against a delivery nobody could evidence. The aggregate refuses it and
        // so does the row, because the aggregate is not the only thing that can write a row.
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await ReceivedOrderAsync(harness, 100m, 60m);

        ThreeWayMatchResult matched = await harness.SendAsync(new MatchSupplierInvoiceCommand(
            orderId,
            "INV-RAW",
            DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime),
            new Money(8_000m, "ZAR"),
            new Money(1_200m, "ZAR"),
            [
                new SupplierInvoiceLineInput(
                    orderLineId, "Coffee beans, 1kg", new Quantity(100m, "EA"), new Money(80m, "ZAR")),
            ]));

        matched.Status.Should().Be(ThreeWayMatchStatus.Blocked);

        Func<Task> raw = () => harness.Context.Database.ExecuteSqlRawAsync(
            """
            UPDATE procurement.supplier_invoice_matches
            SET released_at = now(), released_by_user_id = {0}
            WHERE id = {1}
            """,
            harness.BuyerUserId,
            matched.SupplierInvoiceMatchId);

        (await raw.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_supplier_invoice_matches_blocked_not_released");
    }

    [Fact]
    public async Task The_database_refuses_a_rejected_quantity_with_no_reason()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        (Guid orderId, Guid orderLineId) = await IssuedOrderAsync(harness, ordered: 100m);

        Guid receiptId = await harness.SendAsync(new CreateGoodsReceiptCommand(orderId, "DN-3", null));

        Guid lineId = await harness.SendAsync(new AddGoodsReceiptLineCommand(
            receiptId, orderLineId, new Quantity(94m, "EA"), new Quantity(6m, "EA"),
            GoodsRejectionReason.Damaged, null));

        Func<Task> raw = () => harness.Context.Database.ExecuteSqlRawAsync(
            "UPDATE procurement.goods_receipt_lines SET rejection_reason = 'None' WHERE id = {0}",
            lineId);

        (await raw.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_goods_receipt_lines_rejection_pairs");
    }

    [Fact]
    public async Task The_database_refuses_a_second_quote_from_the_same_supplier()
    {
        await using ProcurementHarness harness = await ProcurementHarness.CreateAsync(fixture);

        Guid rfqId = await harness.SendAsync(new CreateRfqCommand(
            "Coffee", null, harness.Clock.UtcNow.AddDays(3)));

        await harness.SendAsync(new AddRfqLineCommand(
            rfqId, harness.ItemId, null, "Coffee beans, 1kg", new Quantity(100m, "EA"), null, null));

        await harness.SendAsync(new IssueRfqCommand(rfqId));

        Guid responseId = await harness.SendAsync(new RecordRfqResponseCommand(
            rfqId, harness.SupplierId, "ZAR", harness.Clock.UtcNow, null, 7, null));

        Func<Task> raw = () => harness.Context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO procurement.rfq_responses
                (id, tenant_id, store_id, rfq_id, partner_id, currency, quoted_at, lead_time_days,
                 status, total_amount, total_currency, created_at, created_by, updated_at, updated_by,
                 row_version, sync_state, sync_stamp)
            SELECT gen_random_uuid(), tenant_id, store_id, rfq_id, partner_id, currency, quoted_at,
                   lead_time_days, status, total_amount, total_currency, created_at, created_by,
                   updated_at, updated_by, row_version, sync_state, sync_stamp
            FROM procurement.rfq_responses WHERE id = {0}
            """,
            responseId);

        (await raw.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ux_rfq_responses_rfq_id_partner_id");
    }

    private static DateOnly Expected(ProcurementHarness harness)
        => DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime).AddDays(8);

    private static async Task<(Guid OrderId, Guid OrderLineId)> IssuedOrderAsync(
        ProcurementHarness harness, decimal ordered)
    {
        Guid orderId = await harness.SendAsync(new CreatePurchaseOrderCommand(
            harness.SupplierId, "ZAR", harness.LocationId, Expected(harness), null, null));

        Guid orderLineId = await harness.SendAsync(new AddPurchaseOrderLineCommand(
            orderId, harness.ItemId, null, "Coffee beans, 1kg", new Quantity(ordered, "EA"),
            new Money(80m, "ZAR"), "STANDARD", null));

        await harness.SendAsync(new ApprovePurchaseOrderCommand(orderId, Issue: true));

        return (orderId, orderLineId);
    }

    private static async Task<(Guid OrderId, Guid OrderLineId)> ReceivedOrderAsync(
        ProcurementHarness harness, decimal ordered, decimal received)
    {
        (Guid orderId, Guid orderLineId) = await IssuedOrderAsync(harness, ordered);

        Guid receiptId = await harness.SendAsync(new CreateGoodsReceiptCommand(orderId, "DN-0", null));

        await harness.SendAsync(new AddGoodsReceiptLineCommand(
            receiptId, orderLineId, new Quantity(received, "EA"), null, GoodsRejectionReason.None, null));

        await harness.SendAsync(new CompleteGoodsReceiptCommand(receiptId));

        return (orderId, orderLineId);
    }
}
