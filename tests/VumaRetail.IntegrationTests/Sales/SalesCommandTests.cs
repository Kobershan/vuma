using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Sales.Queries;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Sales;

/// <summary>
/// The <c>sales</c> module against real PostgreSQL: resolution over overlapping configuration, a
/// return that moves stock and money, and the database constraints that hold when the aggregate is
/// bypassed.
/// </summary>
/// <remarks>
/// The constraint tests write through raw SQL on purpose. A check constraint that is only ever
/// exercised through the aggregate that already enforces the same rule has not been tested — it has
/// been assumed, and the whole reason business rule 5 is also a constraint is the case where something
/// other than the aggregate writes the row.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SalesCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_price_resolves_against_the_highest_priority_list_that_carries_the_item()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await SeedRetailListAsync(harness, 12.99m);

        Guid staffListId = await harness.SendAsync(new CreatePriceListCommand(
            "STAFF", "Staff prices", "ZAR", PriceListKind.Staff,
            PricesIncludeTax: true, Priority: 50, today.AddDays(-1)));

        await harness.SendAsync(new SetPriceListLineCommand(
            staffListId, harness.ItemId, null, new Money(9.99m, "ZAR")));

        PriceResolution resolution = await harness.QueryAsync(new ResolvePriceQuery(
            new PriceResolutionRequest(
                harness.ItemId, null, null, 1m, harness.StoreId, today, new TimeOnly(12, 0), "ZAR")));

        resolution.PriceListCode.Should().Be("STAFF");
        resolution.UnitPrice.Amount.Should().Be(9.99m);
        resolution.Explanation.Should().Contain("STAFF");
    }

    [Fact]
    public async Task Overlapping_promotions_apply_in_priority_order_against_a_real_database()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await SeedRetailListAsync(harness, 50m);

        Guid amountOff = await harness.SendAsync(new CreatePromotionCommand(
            "R5-OFF", "R5 off each", PromotionKind.AmountOff, today.AddDays(-1),
            RewardAmount: new Money(5m, "ZAR"), Priority: 5));

        await harness.SendAsync(new AddPromotionLineCommand(amountOff, harness.ItemId));

        Guid percentageOff = await harness.SendAsync(new CreatePromotionCommand(
            "TEN-PCT", "10% off", PromotionKind.PercentageOff, today.AddDays(-1),
            DiscountPercentage: 10m, Priority: 1));

        await harness.SendAsync(new AddPromotionLineCommand(percentageOff, harness.ItemId));

        PriceResolution resolution = await harness.QueryAsync(new ResolvePriceQuery(
            new PriceResolutionRequest(
                harness.ItemId, null, null, 2m, harness.StoreId, today, new TimeOnly(12, 0), "ZAR")));

        // R100 extended, R10 off, then 10% of the remaining R90.
        resolution.Promotions.Select(promotion => promotion.Code).Should().ContainInOrder("R5-OFF", "TEN-PCT");
        resolution.DiscountAmount.Amount.Should().Be(19m);
        resolution.NetPayable.Amount.Should().Be(81m);
    }

    [Fact]
    public async Task A_promotion_targeting_another_item_does_not_fire_on_this_one()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await SeedRetailListAsync(harness, 50m);

        Guid promotion = await harness.SendAsync(new CreatePromotionCommand(
            "BREAD-ONLY", "Bread special", PromotionKind.PercentageOff, today.AddDays(-1),
            DiscountPercentage: 50m));

        await harness.SendAsync(new AddPromotionLineCommand(promotion, harness.UnstockedItemId));

        PriceResolution resolution = await harness.QueryAsync(new ResolvePriceQuery(
            new PriceResolutionRequest(
                harness.ItemId, null, null, 1m, harness.StoreId, today, new TimeOnly(12, 0), "ZAR")));

        resolution.Promotions.Should().BeEmpty();
        resolution.NetPayable.Amount.Should().Be(50m);
    }

    [Fact]
    public async Task A_price_list_code_is_unique_per_tenant()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await SeedRetailListAsync(harness, 12.99m);

        Func<Task> duplicate = () => harness.SendAsync(new CreatePriceListCommand(
            "retail", "Another shelf list", "ZAR", PriceListKind.Retail,
            PricesIncludeTax: true, Priority: 0, today.AddDays(-1)));

        (await duplicate.Should().ThrowAsync<SalesConflictException>())
            .Which.Kind.Should().Be(DomainProblemKind.Conflict);
    }

    [Fact]
    public async Task Setting_a_price_twice_reprices_the_break_rather_than_adding_a_second_one()
    {
        // What makes Stage 11's bulk price import idempotent: re-running the same sheet updates rows.
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid listId = await SeedRetailListAsync(harness, 12.99m);

        Guid first = await harness.SendAsync(new SetPriceListLineCommand(
            listId, harness.ItemId, null, new Money(11.50m, "ZAR")));

        first.Should().NotBe(Guid.Empty);

        PriceList reloaded = await harness.QueryAsync(new GetPriceListQuery(listId));

        reloaded.Lines.Should().ContainSingle();
        reloaded.Lines[0].UnitPrice.Amount.Should().Be(11.50m);
    }

    [Fact]
    public async Task A_completed_return_refunds_the_original_price_puts_stock_back_and_raises_one_event()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 3m, unitPrice: 115m);
        Sale sale = await RequireSaleAsync(harness, saleId);

        Guid returnId = await harness.SendAsync(new CreateSalesReturnCommand(
            saleId, "Faulty seal", TenderType.Cash));

        await harness.SendAsync(new AddSalesReturnLineCommand(
            returnId, sale.Lines[0].Id, new Quantity(1m, "EA")));

        SalesReturnCompletionResult result = await harness.SendAsync(
            new CompleteSalesReturnCommand(returnId));

        result.Gross.Amount.Should().Be(115m);
        result.Tax.Amount.Should().Be(15m);
        result.Net.Amount.Should().Be(100m);
        result.StockReturnsRefused.Should().Be(0);

        // One event, naming an event type and three amounts — and nowhere to put a GL account.
        harness.ReturnEvents.Events.Should().ContainSingle();
        harness.ReturnEvents.Events[0].Gross.Amount.Should().Be(115m);
        harness.ReturnEvents.Events[0].SaleId.Should().Be(saleId);

        // 100 received, 3 sold, 1 back: the shelf and the ledger agree again.
        StockBalance? balance = await harness.Context.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.LocationId == harness.LocationId && row.ItemId == harness.ItemId);

        balance!.QuantityOnHand.Value.Should().Be(98m);

        // Received back at what it left at (R20), not at today's average.
        StockLedgerEntry receipt = await harness.Context.StockLedgerEntries
            .AsNoTracking()
            .FirstAsync(entry => entry.MovementType == StockMovementType.SalesReturn);

        receipt.UnitCost.Amount.Should().Be(20m);
        receipt.ReferenceType.Should().Be(StockReferenceType.SalesReturn);
        receipt.ReferenceId.Should().Be(returnId);
    }

    [Fact]
    public async Task A_return_whose_stock_receipt_has_no_cost_basis_still_completes_and_records_why()
    {
        // ADR-073's shape. The original sale completed without relieving stock — bread has nothing on
        // hand — so there is no cost the goods left at. The customer is at the counter and the refund
        // stands; the line carries the reason and joins the reconciliation queue.
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 1m, unitPrice: 23m, itemId: harness.UnstockedItemId);
        Sale sale = await RequireSaleAsync(harness, saleId);

        sale.Lines[0].StockIssue.Should().Be(StockIssueStatus.Refused);

        Guid returnId = await harness.SendAsync(new CreateSalesReturnCommand(
            saleId, "Stale", TenderType.Cash));

        await harness.SendAsync(new AddSalesReturnLineCommand(
            returnId, sale.Lines[0].Id, new Quantity(1m, "EA")));

        SalesReturnCompletionResult result = await harness.SendAsync(
            new CompleteSalesReturnCommand(returnId));

        result.Gross.Amount.Should().Be(23m);
        result.StockReturnsRefused.Should().Be(1);

        SalesReturn reloaded = await harness.QueryAsync(new GetSalesReturnQuery(returnId));

        reloaded.Status.Should().Be(SalesReturnStatus.Completed);
        reloaded.Lines[0].StockReturn.Should().Be(StockReturnStatus.Refused);
        reloaded.Lines[0].StockReturnNote.Should().Contain("no cost");
    }

    [Fact]
    public async Task The_cumulative_returned_quantity_across_documents_can_never_exceed_what_was_sold()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 3m, unitPrice: 115m);
        Sale sale = await RequireSaleAsync(harness, saleId);

        Guid first = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Two back", TenderType.Cash));
        await harness.SendAsync(new AddSalesReturnLineCommand(first, sale.Lines[0].Id, new Quantity(2m, "EA")));
        await harness.SendAsync(new CompleteSalesReturnCommand(first));

        Guid second = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Two more", TenderType.Cash));

        Func<Task> overReturning = () => harness.SendAsync(
            new AddSalesReturnLineCommand(second, sale.Lines[0].Id, new Quantity(2m, "EA")));

        (await overReturning.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_RETURN_EXCEEDS_QUANTITY_SOLD");

        // The one that fits is still allowed, and its refund is the remainder to the cent.
        Guid lineId = await harness.SendAsync(
            new AddSalesReturnLineCommand(second, sale.Lines[0].Id, new Quantity(1m, "EA")));

        lineId.Should().NotBe(Guid.Empty);

        SalesReturn reloaded = await harness.QueryAsync(new GetSalesReturnQuery(second));

        reloaded.Gross.Amount.Should().Be(115m);
    }

    [Fact]
    public async Task A_draft_return_on_another_terminal_still_counts_against_what_is_left()
    {
        // Goods the shop has already taken back over the counter. Counting them only once the document
        // completes is how the same item is refunded twice.
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 2m, unitPrice: 115m);
        Sale sale = await RequireSaleAsync(harness, saleId);

        Guid draft = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Held", TenderType.Cash));
        await harness.SendAsync(new AddSalesReturnLineCommand(draft, sale.Lines[0].Id, new Quantity(2m, "EA")));

        Guid second = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Second", TenderType.Cash));

        Func<Task> againstADraft = () => harness.SendAsync(
            new AddSalesReturnLineCommand(second, sale.Lines[0].Id, new Quantity(1m, "EA")));

        (await againstADraft.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_RETURN_EXCEEDS_QUANTITY_SOLD");

        // Cancelling the draft releases the quantity: nothing came back on it after all.
        await harness.SendAsync(new CancelSalesReturnCommand(draft));

        Guid lineId = await harness.SendAsync(
            new AddSalesReturnLineCommand(second, sale.Lines[0].Id, new Quantity(1m, "EA")));

        lineId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_voided_sale_cannot_be_returned()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));

        Guid saleId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));
        await harness.SendAsync(new VoidSaleCommand(saleId, "Customer changed their mind"));

        Func<Task> raising = () => harness.SendAsync(
            new CreateSalesReturnCommand(saleId, "Faulty", TenderType.Cash));

        (await raising.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_RETURN_REQUIRES_COMPLETED_SALE");
    }

    [Fact]
    public async Task The_database_refuses_an_over_return_that_bypassed_the_aggregate()
    {
        // Business rule 5 as a database guarantee. Written through raw SQL because a constraint only
        // ever exercised through the aggregate that enforces the same rule has been assumed, not tested.
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 2m, unitPrice: 115m);
        Sale sale = await RequireSaleAsync(harness, saleId);

        Guid returnId = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Faulty", TenderType.Cash));
        await harness.SendAsync(new AddSalesReturnLineCommand(returnId, sale.Lines[0].Id, new Quantity(1m, "EA")));

        Func<Task> overReturning = () => harness.Context.Database.ExecuteSqlRawAsync(
            """
            UPDATE sales.sales_return_lines
               SET quantity_value = original_quantity_value + 1
             WHERE sales_return_id = {0}
            """,
            returnId);

        (await overReturning.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_sales_return_lines_within_quantity_sold");
    }

    [Fact]
    public async Task The_database_refuses_a_return_line_whose_parts_do_not_add_up()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        Guid saleId = await SellAsync(harness, quantity: 2m, unitPrice: 115m);
        Sale sale = await RequireSaleAsync(harness, saleId);

        Guid returnId = await harness.SendAsync(new CreateSalesReturnCommand(saleId, "Faulty", TenderType.Cash));
        await harness.SendAsync(new AddSalesReturnLineCommand(returnId, sale.Lines[0].Id, new Quantity(1m, "EA")));

        Func<Task> unbalanced = () => harness.Context.Database.ExecuteSqlRawAsync(
            "UPDATE sales.sales_return_lines SET tax_amount = tax_amount + 1 WHERE sales_return_id = {0}",
            returnId);

        (await unbalanced.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_sales_return_lines_balances");
    }

    [Fact]
    public async Task An_override_is_appended_and_read_back_with_what_the_gap_cost()
    {
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        await harness.SendAsync(new RecordPriceOverrideCommand(
            harness.ItemId,
            null,
            new Quantity(3m, "EA"),
            new Money(12.99m, "ZAR"),
            new Money(10m, "ZAR"),
            "Damaged packaging"));

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        IReadOnlyList<PriceOverrideLog> entries = await harness.QueryAsync(
            new ListPriceOverridesQuery(today.AddDays(-1), today, harness.OperatorUserId));

        entries.Should().ContainSingle();
        entries[0].Variance.Amount.Should().Be(-8.97m);
        entries[0].OperatorUserId.Should().Be(harness.OperatorUserId);
    }

    [Fact]
    public async Task A_resolution_in_the_wrong_currency_is_a_domain_refusal_rather_than_a_failure()
    {
        // §4.13's defect, one layer earlier: a code the caller can act on, never an exception that
        // bricks the terminal.
        await using SalesHarness harness = await SalesHarness.CreateAsync(fixture);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await SeedRetailListAsync(harness, 12.99m);

        Func<Task> resolving = () => harness.QueryAsync(new ResolvePriceQuery(
            new PriceResolutionRequest(
                harness.ItemId, null, null, 1m, harness.StoreId, today, new TimeOnly(12, 0), "USD")));

        (await resolving.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_CURRENCY_MISMATCH");
    }

    private static async Task<Guid> SeedRetailListAsync(SalesHarness harness, decimal unitPrice)
    {
        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        Guid listId = await harness.SendAsync(new CreatePriceListCommand(
            "RETAIL", "Shelf prices", "ZAR", PriceListKind.Retail,
            PricesIncludeTax: true, Priority: 0, today.AddDays(-1)));

        await harness.SendAsync(new SetPriceListLineCommand(
            listId, harness.ItemId, null, new Money(unitPrice, "ZAR")));

        return listId;
    }

    /// <summary>Rings a sale up through the real POS commands and completes it.</summary>
    private static async Task<Guid> SellAsync(
        SalesHarness harness, decimal quantity, decimal unitPrice, Guid? itemId = null)
    {
        await harness.SendAsync(new OpenTillSessionCommand(new Money(500m, "ZAR")));

        Guid saleId = await harness.SendAsync(new OpenSaleCommand(null, harness.LocationId));

        await harness.SendAsync(new AddSaleLineCommand(
            saleId,
            itemId ?? harness.ItemId,
            null,
            new Quantity(quantity, "EA"),
            new Money(unitPrice, "ZAR")));

        Sale open = await RequireSaleAsync(harness, saleId);

        await harness.SendAsync(new TenderSaleCommand(saleId, TenderType.Cash, open.Gross));
        await harness.SendAsync(new CompleteSaleCommand(saleId));

        return saleId;
    }

    private static async Task<Sale> RequireSaleAsync(SalesHarness harness, Guid saleId)
        => await harness.QueryAsync(new VumaRetail.Application.Pos.Queries.GetSaleQuery(saleId));
}
