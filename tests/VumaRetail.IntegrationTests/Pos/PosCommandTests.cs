using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Application.Pos.Queries;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Pos;

/// <summary>
/// The till, driven end to end through the real dispatcher against a real database: a sale is opened,
/// rung up, tendered and completed, the stock comes off, the money is counted, and the record of it
/// cannot be edited afterwards.
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones about what happens when something downstream is
/// wrong — a shelf that disagrees with the ledger, a sale replayed after the network came back, a
/// drawer counted while a customer is still at the till. R1 says the till never stops, and these are
/// where that is either true or a slogan.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PosCommandTests(PostgresFixture fixture)
{
    private static readonly Money Rand50 = new(50m, "ZAR");

    /// <summary>Opens a shift, rings up one item and returns the sale, ready to tender.</summary>
    private static async Task<(Guid SessionId, Guid SaleId)> RingUpAsync(
        PosHarness harness, decimal unitPrice = 115m, decimal quantity = 1m, Guid? itemId = null)
    {
        Guid sessionId = await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));
        await harness.Context.CommitAsync();

        Guid saleId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));

        await harness.SendAsync(new AddSaleLineCommand(
            saleId,
            itemId ?? harness.ItemId,
            null,
            new Quantity(quantity, "EA"),
            new Money(unitPrice, "ZAR")));

        return (sessionId, saleId);
    }

    [Fact]
    public async Task A_sale_rung_up_and_tendered_completes_relieves_stock_and_raises_one_event()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(200m, "ZAR")));

        SaleCompletionResult result = await harness.SendAsync(new CompleteSaleCommand(saleId));

        result.Gross.Amount.Should().Be(115m);
        result.ChangeGiven.Amount.Should().Be(85m);
        result.StockIssuesRefused.Should().Be(0);
        result.SaleNumber.Should().StartWith("SALE-");

        Sale sale = await harness.QueryAsync(new GetSaleQuery(saleId));
        sale.Status.Should().Be(SaleStatus.Completed);

        // The line names the ledger row it produced — the correlation Stage 08 built ReferenceId for.
        SaleLine line = sale.LiveLines.Single();
        line.StockIssue.Should().Be(StockIssueStatus.Posted);
        line.StockLedgerEntryId.Should().NotBeNull();

        harness.SaleEvents.Events.Should().ContainSingle();
        SaleTenderedEvent raised = harness.SaleEvents.Events.Single();
        raised.Gross.Amount.Should().Be(115m);
        raised.Net.Amount.Should().Be(100m);
        raised.Tax.Amount.Should().Be(15m);
        raised.SaleNumber.Should().Be(result.SaleNumber);
    }

    [Fact]
    public async Task Tax_comes_from_the_configured_rule_rather_than_a_constant()
    {
        // The harness seeds STANDARD at 15% inclusive. R115 charged is R100 net and R15 tax — and the
        // engine, not the till, decided that the price was tax-inclusive.
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness, unitPrice: 115m);

        Sale sale = await harness.QueryAsync(new GetSaleQuery(saleId));

        sale.Net.Amount.Should().Be(100m);
        sale.Tax.Amount.Should().Be(15m);
        sale.Gross.Amount.Should().Be(115m);
        sale.LiveLines.Single().TaxCode.Should().Be("STANDARD");
    }

    [Fact]
    public async Task The_stock_relieved_equals_what_was_sold()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness, quantity: 3m);

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(345m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        Domain.Inventory.StockBalance? balance = await new Infrastructure.Persistence.Repositories
            .StockBalanceRepository(harness.Context)
            .FindAsync(harness.LocationId, harness.ItemId, null);

        // 100 received, 3 sold.
        balance!.QuantityOnHand.Value.Should().Be(97m);
    }

    [Fact]
    public async Task A_sale_the_ledger_cannot_supply_still_completes_and_lands_in_the_reconciliation_queue()
    {
        // ADR-073, and the single most important test in this stage. The customer is holding the item;
        // refusing to record that they paid for it does not put it back on the shelf. Stage 08's rule
        // that stock never goes negative also holds — nothing is posted.
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness, unitPrice: 20m, itemId: harness.SecondItemId);

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(20m, "ZAR")));

        SaleCompletionResult result = await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        result.StockIssuesRefused.Should().Be(1);

        Sale sale = await harness.QueryAsync(new GetSaleQuery(saleId));
        sale.Status.Should().Be(SaleStatus.Completed);

        SaleLine line = sale.LiveLines.Single();
        line.StockIssue.Should().Be(StockIssueStatus.Refused);
        line.StockLedgerEntryId.Should().BeNull();
        line.StockIssueNote.Should().Contain("on hand");

        // And it is findable, which is the whole point of recording it rather than logging it.
        IReadOnlyList<RefusedStockIssue> queue = await harness.QueryAsync(
            new ListRefusedStockIssuesQuery(harness.LocationId));

        queue.Should().ContainSingle();
        queue[0].SaleNumber.Should().Be(result.SaleNumber);
        queue[0].Quantity.Value.Should().Be(1m);
    }

    [Fact]
    public async Task A_normal_sale_leaves_the_reconciliation_queue_empty()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(115m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        IReadOnlyList<RefusedStockIssue> queue = await harness.QueryAsync(
            new ListRefusedStockIssuesQuery(null));

        queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_replayed_sale_id_returns_the_first_sale_rather_than_creating_a_second()
    {
        // The offline path. A till that lost its acknowledgement and retried has done nothing wrong.
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));
        await harness.Context.CommitAsync();

        Guid terminalMinted = UuidV7.NewGuid();

        Guid first = await harness.SendAsync(new OpenSaleCommand(terminalMinted, harness.LocationId));
        await harness.Context.CommitAsync();

        Guid second = await harness.SendAsync(new OpenSaleCommand(terminalMinted, harness.LocationId));
        await harness.Context.CommitAsync();

        first.Should().Be(terminalMinted);
        second.Should().Be(first);

        harness.Context.Sales.Count().Should().Be(1);
    }

    [Fact]
    public async Task A_completed_sale_refuses_further_lines_and_tenders_through_the_pipeline()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(115m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        Func<Task> adding = () => harness.SendAsync(new AddSaleLineCommand(
            saleId, harness.ItemId, null, new Quantity(1m, "EA"), Rand50));

        Func<Task> tendering = () => harness.SendAsync(
            new TenderSaleCommand(saleId, TenderType.Cash, Rand50));

        (await adding.Should().ThrowAsync<PosRuleException>()).Which.Code.Should().Be("POS_SALE_NOT_OPEN");
        (await tendering.Should().ThrowAsync<PosRuleException>()).Which.Code.Should().Be("POS_SALE_NOT_OPEN");
    }

    [Fact]
    public async Task An_under_tendered_sale_is_refused_and_moves_no_stock()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, Rand50));
        await harness.Context.CommitAsync();

        Func<Task> completing = () => harness.SendAsync(new CompleteSaleCommand(saleId));

        (await completing.Should().ThrowAsync<PosRuleException>())
            .Which.Code.Should().Be("POS_SALE_NOT_FULLY_TENDERED");

        // The refusal happens before anything downstream runs, so nothing was relieved and no event
        // was raised.
        harness.SaleEvents.Events.Should().BeEmpty();

        Domain.Inventory.StockBalance? balance = await new Infrastructure.Persistence.Repositories
            .StockBalanceRepository(harness.Context)
            .FindAsync(harness.LocationId, harness.ItemId, null);

        balance!.QuantityOnHand.Value.Should().Be(100m);
    }

    [Fact]
    public async Task A_voided_line_comes_off_the_total_and_off_the_stock_issue()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);

        Guid secondLine = await harness.SendAsync(new AddSaleLineCommand(
            saleId, harness.ItemId, null, new Quantity(2m, "EA"), Rand50));

        await harness.SendAsync(new VoidSaleLineCommand(saleId, secondLine));

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(115m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        Sale sale = await harness.QueryAsync(new GetSaleQuery(saleId));

        sale.Gross.Amount.Should().Be(115m);
        sale.Lines.Should().HaveCount(2);

        // The voided line was never issued — only 1 of the 100 came off.
        Domain.Inventory.StockBalance? balance = await new Infrastructure.Persistence.Repositories
            .StockBalanceRepository(harness.Context)
            .FindAsync(harness.LocationId, harness.ItemId, null);

        balance!.QuantityOnHand.Value.Should().Be(99m);
    }

    [Fact]
    public async Task A_sale_parks_and_resumes_and_shows_up_in_the_terminal_list_while_parked()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);

        await harness.SendAsync(new ParkSaleCommand(saleId));
        await harness.Context.CommitAsync();

        IReadOnlyList<Sale> parked = await harness.QueryAsync(new ListParkedSalesQuery(harness.TerminalId));
        parked.Should().ContainSingle().Which.Id.Should().Be(saleId);

        await harness.SendAsync(new ResumeSaleCommand(saleId));
        await harness.Context.CommitAsync();

        (await harness.QueryAsync(new ListParkedSalesQuery(harness.TerminalId))).Should().BeEmpty();
        (await harness.QueryAsync(new GetSaleQuery(saleId))).Status.Should().Be(SaleStatus.Open);
    }

    [Fact]
    public async Task A_second_till_session_on_the_same_terminal_is_refused()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));
        await harness.Context.CommitAsync();

        Func<Task> again = () => harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));

        (await again.Should().ThrowAsync<PosConflictException>())
            .Which.Code.Should().Be("POS_TILL_SESSION_ALREADY_OPEN");
    }

    [Fact]
    public async Task The_expected_cash_is_derived_from_the_sessions_own_sales()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (Guid sessionId, Guid saleId) = await RingUpAsync(harness);

        // R200 in, R85 change out: R115 stays in the drawer, on top of the R500 float.
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(200m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        TillSessionView view = await harness.QueryAsync(new GetTillSessionQuery(sessionId));

        view.ExpectedCash.Amount.Should().Be(615m);
        view.SalesCompleted.Should().Be(1);
        view.SalesUnfinished.Should().Be(0);

        CashUpResult cashUp = await harness.SendAsync(
            new CloseTillSessionCommand(sessionId, new Money(615m, "ZAR")));

        cashUp.ExpectedCash.Amount.Should().Be(615m);
        cashUp.Variance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task A_card_sale_puts_nothing_in_the_drawer()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (Guid sessionId, Guid saleId) = await RingUpAsync(harness);

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Card, new Money(115m, "ZAR"), "AUTH-991"));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        TillSessionView view = await harness.QueryAsync(new GetTillSessionQuery(sessionId));

        view.ExpectedCash.Amount.Should().Be(500m);
    }

    [Fact]
    public async Task A_short_drawer_records_the_variance_rather_than_being_talked_out_of_it()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (Guid sessionId, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(115m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        CashUpResult cashUp = await harness.SendAsync(
            new CloseTillSessionCommand(sessionId, new Money(575m, "ZAR"), "R40 short"));

        cashUp.ExpectedCash.Amount.Should().Be(615m);
        cashUp.Variance.Amount.Should().Be(-40m);
    }

    [Fact]
    public async Task A_session_will_not_close_while_a_sale_is_still_open_on_it()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (Guid sessionId, _) = await RingUpAsync(harness);
        await harness.Context.CommitAsync();

        Func<Task> closing = () => harness.SendAsync(
            new CloseTillSessionCommand(sessionId, new Money(500m, "ZAR")));

        (await closing.Should().ThrowAsync<PosRuleException>())
            .Which.Code.Should().Be("POS_TILL_SESSION_HAS_OPEN_SALES");
    }

    [Fact]
    public async Task The_receipt_carries_the_sellers_VAT_number_the_tax_summary_and_the_layout()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(200m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        ReceiptDocument receipt = await harness.QueryAsync(new BuildReceiptQuery(saleId));

        receipt.StoreName.Should().Be("Harness Sandton");
        receipt.OperatorName.Should().Be("Thandi Nkosi");
        receipt.Lines.Should().ContainSingle().Which.Description.Should().Be("Full cream milk 2L");
        receipt.TaxLines.Should().ContainSingle().Which.Tax.Amount.Should().Be(15m);
        receipt.ChangeGiven.Amount.Should().Be(85m);
        receipt.IsReprint.Should().BeFalse();
    }

    [Fact]
    public async Task The_first_print_is_not_a_reprint_and_the_second_one_needs_a_reason()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);
        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, new Money(115m, "ZAR")));
        await harness.SendAsync(new CompleteSaleCommand(saleId));
        await harness.Context.CommitAsync();

        await harness.SendAsync(new RecordReceiptPrintCommand(saleId));
        await harness.Context.CommitAsync();

        // Reprint status is derived from the log, so a caller cannot declare its way out of it.
        Func<Task> reprintWithoutReason = () => harness.SendAsync(new RecordReceiptPrintCommand(saleId));

        await reprintWithoutReason.Should().ThrowAsync<ReceiptReprintRequiresReasonException>();

        await harness.SendAsync(new RecordReceiptPrintCommand(saleId, "Customer lost the original"));
        await harness.Context.CommitAsync();

        IReadOnlyList<ReceiptPrint> prints = await harness.QueryAsync(new ListReceiptPrintsQuery(saleId));

        prints.Should().HaveCount(2);
        prints[0].IsReprint.Should().BeFalse();
        prints[1].IsReprint.Should().BeTrue();
        prints[1].Reason.Should().Be("Customer lost the original");

        // And the receipt itself now says so.
        (await harness.QueryAsync(new BuildReceiptQuery(saleId))).IsReprint.Should().BeTrue();
    }

    [Fact]
    public async Task A_scanned_barcode_resolves_to_something_the_till_can_ring_up()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        SellableItem item = await harness.QueryAsync(new LookupBarcodeQuery("6009876543210"));

        item.ItemId.Should().Be(harness.ItemId);
        item.Description.Should().Be("Full cream milk 2L");
        item.UnitOfMeasureCode.Should().Be("EA");
        item.TaxClassCode.Should().Be("STANDARD");
    }

    [Fact]
    public async Task A_line_in_the_wrong_unit_of_measure_is_refused_before_it_reaches_the_ledger()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));
        await harness.Context.CommitAsync();

        Guid saleId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));

        Func<Task> ringing = () => harness.SendAsync(new AddSaleLineCommand(
            saleId, harness.ItemId, null, new Quantity(1m, "KG"), Rand50));

        (await ringing.Should().ThrowAsync<Domain.Inventory.InventoryRuleException>())
            .Which.Code.Should().Be("INVENTORY_UOM_MISMATCH");
    }

    [Fact]
    public async Task A_sale_number_is_issued_from_the_gap_free_sequence()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));
        await harness.Context.CommitAsync();

        Guid firstId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));
        await harness.Context.CommitAsync();

        Guid secondId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));
        await harness.Context.CommitAsync();

        string first = (await harness.QueryAsync(new GetSaleQuery(firstId))).SaleNumber;
        string second = (await harness.QueryAsync(new GetSaleQuery(secondId))).SaleNumber;

        first.Should().Be("SALE-000001");
        second.Should().Be("SALE-000002");
    }

    [Fact]
    public async Task An_abandoned_sale_records_why_and_moves_no_stock()
    {
        await using PosHarness harness = await PosHarness.CreateAsync(fixture);

        (_, Guid saleId) = await RingUpAsync(harness);

        await harness.SendAsync(new VoidSaleCommand(saleId, "Customer left without paying"));
        await harness.Context.CommitAsync();

        Sale sale = await harness.QueryAsync(new GetSaleQuery(saleId));

        sale.Status.Should().Be(SaleStatus.Voided);
        sale.VoidReason.Should().Be("Customer left without paying");

        Domain.Inventory.StockBalance? balance = await new Infrastructure.Persistence.Repositories
            .StockBalanceRepository(harness.Context)
            .FindAsync(harness.LocationId, harness.ItemId, null);

        balance!.QuantityOnHand.Value.Should().Be(100m);
    }
}
