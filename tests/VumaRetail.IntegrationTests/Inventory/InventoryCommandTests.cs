using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Queries;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Inventory;

/// <summary>
/// Stage 08's commands through the real dispatcher against a real database — the ledger and its
/// balance projection staying in step, which is the whole of ADR-005.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryCommandTests(PostgresFixture fixture)
{
    private static Quantity Each(decimal value) => new(value, "EA");

    private static Money Rand(decimal amount) => new(amount, "ZAR");

    private static Task<Guid> CreateLocationAsync(InventoryHarness harness, string code, string name)
        => harness.SendAsync(new CreateStockLocationCommand(code, name, StockLocationType.Warehouse));

    [Fact]
    public async Task A_receipt_writes_a_ledger_entry_and_opens_the_balance_it_projects_to()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");

        Guid entryId = await harness.SendAsync(
            new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(25m), "First delivery"));

        StockLedgerEntry? entry = await harness.Ledger.FindAsync(entryId);
        entry.Should().NotBeNull();
        entry!.MovementType.Should().Be(StockMovementType.Receipt);
        entry.Quantity.Value.Should().Be(10m);
        entry.Value.Amount.Should().Be(250m);

        StockBalance? balance = await harness.Balances.FindAsync(location, harness.ItemId, null);
        balance.Should().NotBeNull();
        balance!.QuantityOnHand.Value.Should().Be(10m);
        balance.AverageCost.Amount.Should().Be(25m);
    }

    [Fact]
    public async Task The_balance_is_always_the_sum_of_the_ledger()
    {
        // ADR-005's actual claim: the balance is a projection, not an independent number. Five
        // movements of different kinds, then assert the projection equals the sum of the entries —
        // which is the invariant that would silently rot if any poster path forgot to update one side.
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");

        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(20m), null));
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(30m), null));
        await harness.SendAsync(new RecordSaleIssueCommand(location, harness.ItemId, null, Each(4m), UuidV7.NewGuid()));
        await harness.SendAsync(new AdjustStockCommand(
            location, harness.ItemId, null, Each(-1m), AdjustmentReasonCode.Damage, null, "Dropped"));
        await harness.SendAsync(new AdjustStockCommand(
            location, harness.ItemId, null, Each(2m), AdjustmentReasonCode.Found, null, "Found in the van"));

        decimal ledgerSum = await harness.Context.StockLedgerEntries
            .Where(entry => entry.LocationId == location)
            .SumAsync(entry => entry.Quantity.Value);

        StockBalance? balance = await harness.Balances.FindAsync(location, harness.ItemId, null);

        ledgerSum.Should().Be(17m);
        balance!.QuantityOnHand.Value.Should().Be(17m);
    }

    [Fact]
    public async Task An_issue_beyond_what_is_on_hand_is_refused_and_writes_nothing()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(3m), Rand(20m), null));

        Func<Task> issuing = () => harness.SendAsync(
            new RecordSaleIssueCommand(location, harness.ItemId, null, Each(4m), UuidV7.NewGuid()));

        (await issuing.Should().ThrowAsync<InventoryRuleException>())
            .Which.Code.Should().Be("INVENTORY_INSUFFICIENT_STOCK");

        // The transaction behaviour rolls the whole command back, so the refused issue leaves neither
        // a ledger entry nor a half-applied balance.
        StockBalance? balance = await harness.Balances.FindAsync(location, harness.ItemId, null);
        balance!.QuantityOnHand.Value.Should().Be(3m);
        (await harness.Context.StockLedgerEntries.CountAsync(entry => entry.LocationId == location))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_transfer_posts_two_correlated_entries_and_moves_value_without_creating_it()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid source = await CreateLocationAsync(harness, "MAIN", "Back room");
        Guid destination = await CreateLocationAsync(harness, "FLOOR", "Sales floor");
        await harness.SendAsync(new ReceiveStockCommand(source, harness.ItemId, null, Each(10m), Rand(25m), null));

        Guid transferId = await harness.SendAsync(
            new TransferStockCommand(source, destination, harness.ItemId, null, Each(4m), "Restock the floor"));

        StockTransfer? transfer = await harness.Transfers.FindAsync(transferId);
        transfer.Should().NotBeNull();

        StockBalance? sourceBalance = await harness.Balances.FindAsync(source, harness.ItemId, null);
        StockBalance? destinationBalance = await harness.Balances.FindAsync(destination, harness.ItemId, null);

        sourceBalance!.QuantityOnHand.Value.Should().Be(6m);
        destinationBalance!.QuantityOnHand.Value.Should().Be(4m);

        // The destination is valued at exactly what left the source. Total value across both
        // locations is unchanged — a transfer moves value, it does not create or destroy it.
        destinationBalance.AverageCost.Amount.Should().Be(25m);
        (sourceBalance.TotalValue + destinationBalance.TotalValue).Amount.Should().Be(250m);

        // Both entries carry the transfer's own id, which is how the two sides are found together.
        List<StockLedgerEntry> correlated = await harness.Context.StockLedgerEntries
            .Where(entry => entry.ReferenceId == transferId)
            .ToListAsync();

        correlated.Should().HaveCount(2);
        correlated.Select(entry => entry.MovementType).Should()
            .BeEquivalentTo([StockMovementType.TransferOut, StockMovementType.TransferIn]);
        correlated.Sum(entry => entry.Quantity.Value).Should().Be(0m);
    }

    [Fact]
    public async Task A_transfer_of_more_than_the_source_holds_is_refused_and_moves_nothing()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid source = await CreateLocationAsync(harness, "MAIN", "Back room");
        Guid destination = await CreateLocationAsync(harness, "FLOOR", "Sales floor");
        await harness.SendAsync(new ReceiveStockCommand(source, harness.ItemId, null, Each(3m), Rand(25m), null));

        Func<Task> transferring = () => harness.SendAsync(
            new TransferStockCommand(source, destination, harness.ItemId, null, Each(5m), null));

        await transferring.Should().ThrowAsync<InventoryRuleException>();

        // Critically: the destination must not have been credited. The transfer posts the outbound
        // side first, so a naive implementation that did not roll back would leave stock invented at
        // the destination and still present at the source.
        (await harness.Balances.FindAsync(destination, harness.ItemId, null)).Should().BeNull();
        (await harness.Balances.FindAsync(source, harness.ItemId, null))!.QuantityOnHand.Value.Should().Be(3m);
    }

    [Fact]
    public async Task Finalizing_a_stocktake_posts_every_line_variance_and_closes_the_session()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(20m), null));
        await harness.SendAsync(new ReceiveStockCommand(location, harness.SecondItemId, null, Each(5m), Rand(15m), null));

        Guid session = await harness.SendAsync(new OpenStocktakeCommand(location));
        await harness.SendAsync(new RecordStocktakeCountCommand(session, harness.ItemId, null, Each(8m)));
        await harness.SendAsync(new RecordStocktakeCountCommand(session, harness.SecondItemId, null, Each(5m)));

        await harness.SendAsync(new FinalizeStocktakeCommand(session));

        // Short by two on the first item; exact on the second, which must post nothing at all.
        (await harness.Balances.FindAsync(location, harness.ItemId, null))!.QuantityOnHand.Value.Should().Be(8m);
        (await harness.Balances.FindAsync(location, harness.SecondItemId, null))!.QuantityOnHand.Value.Should().Be(5m);

        List<StockLedgerEntry> variances = await harness.Context.StockLedgerEntries
            .Where(entry => entry.ReferenceId == session)
            .ToListAsync();

        variances.Should().HaveCount(1);
        variances[0].MovementType.Should().Be(StockMovementType.StocktakeVariance);
        variances[0].Quantity.Value.Should().Be(-2m);

        StocktakeSession? finalized = await harness.Stocktakes.FindSessionAsync(session);
        finalized!.IsFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task A_finalized_stocktake_refuses_a_second_finalize()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(20m), null));

        Guid session = await harness.SendAsync(new OpenStocktakeCommand(location));
        await harness.SendAsync(new RecordStocktakeCountCommand(session, harness.ItemId, null, Each(9m)));
        await harness.SendAsync(new FinalizeStocktakeCommand(session));

        Func<Task> again = () => harness.SendAsync(new FinalizeStocktakeCommand(session));

        (await again.Should().ThrowAsync<InventoryRuleException>())
            .Which.Code.Should().Be("INVENTORY_STOCKTAKE_ALREADY_FINALIZED");

        // The balance must not have moved twice — the real cost of a double finalize.
        (await harness.Balances.FindAsync(location, harness.ItemId, null))!.QuantityOnHand.Value.Should().Be(9m);
    }

    [Fact]
    public async Task Counting_the_same_item_twice_recounts_the_line_rather_than_adding_a_second()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(10m), Rand(20m), null));

        Guid session = await harness.SendAsync(new OpenStocktakeCommand(location));
        Guid first = await harness.SendAsync(new RecordStocktakeCountCommand(session, harness.ItemId, null, Each(7m)));
        Guid second = await harness.SendAsync(new RecordStocktakeCountCommand(session, harness.ItemId, null, Each(9m)));

        second.Should().Be(first);

        IReadOnlyList<StocktakeLine> lines = await harness.Stocktakes.ListLinesAsync(session);
        lines.Should().HaveCount(1);
        lines[0].CountedQuantity.Value.Should().Be(9m);
    }

    [Fact]
    public async Task A_receipt_in_the_wrong_unit_of_measure_is_refused()
    {
        // The item is counted in EA; receiving kilograms of it is a mistake the resolver catches
        // before the poster ever sees it.
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");

        Func<Task> receiving = () => harness.SendAsync(
            new ReceiveStockCommand(location, harness.ItemId, null, new Quantity(10m, "KG"), Rand(25m), null));

        (await receiving.Should().ThrowAsync<InventoryRuleException>())
            .Which.Code.Should().Be("INVENTORY_UOM_MISMATCH");
    }

    [Fact]
    public async Task Every_movement_raises_exactly_one_valuation_event_carrying_its_value()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid source = await CreateLocationAsync(harness, "MAIN", "Back room");
        Guid destination = await CreateLocationAsync(harness, "FLOOR", "Sales floor");

        await harness.SendAsync(new ReceiveStockCommand(source, harness.ItemId, null, Each(10m), Rand(25m), null));
        await harness.SendAsync(new RecordSaleIssueCommand(source, harness.ItemId, null, Each(2m), UuidV7.NewGuid()));
        await harness.SendAsync(new TransferStockCommand(source, destination, harness.ItemId, null, Each(3m), null));

        // One event per ledger entry, deliberately the same cardinality — a transfer is two entries
        // and so two events.
        harness.ValuationEvents.Events.Should().HaveCount(4);
        harness.ValuationEvents.Events.Select(raised => raised.MovementType).Should().BeEquivalentTo(
            [
                StockMovementType.Receipt,
                StockMovementType.SaleIssue,
                StockMovementType.TransferOut,
                StockMovementType.TransferIn,
            ]);

        harness.ValuationEvents.Events[0].Value.Amount.Should().Be(250m);
        harness.ValuationEvents.Events[1].Value.Amount.Should().Be(-50m);

        // The two transfer sides are equal and opposite, which is what makes a transfer net to nothing
        // in a chart with one inventory account.
        (harness.ValuationEvents.Events[2].Value + harness.ValuationEvents.Events[3].Value)
            .Amount.Should().Be(0m);
    }

    [Fact]
    public async Task A_positive_adjustment_with_no_existing_balance_needs_a_cost_to_open_one()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");

        Func<Task> adjusting = () => harness.SendAsync(new AdjustStockCommand(
            location, harness.ItemId, null, Each(5m), AdjustmentReasonCode.Found, null, null));

        (await adjusting.Should().ThrowAsync<InventoryRuleException>())
            .Which.Code.Should().Be("INVENTORY_UNIT_COST_REQUIRED");

        // Supplying one works, and opens the balance at that cost.
        await harness.SendAsync(new AdjustStockCommand(
            location, harness.ItemId, null, Each(5m), AdjustmentReasonCode.Found, Rand(12m), null));

        StockBalance? balance = await harness.Balances.FindAsync(location, harness.ItemId, null);
        balance!.AverageCost.Amount.Should().Be(12m);
    }

    [Fact]
    public async Task The_ledger_pages_newest_first_without_repeating_or_skipping_a_row()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");

        for (int index = 0; index < 7; index++)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(1m), Rand(10m), $"Delivery {index}"));
        }

        PageResult<StockLedgerEntryResult> first = await harness.QueryAsync(
            new ListStockLedgerEntriesQuery(location, Limit: 3));

        first.Items.Should().HaveCount(3);
        first.HasMore.Should().BeTrue();

        PageResult<StockLedgerEntryResult> second = await harness.QueryAsync(
            new ListStockLedgerEntriesQuery(location, Limit: 3, After: first.NextCursor));

        PageResult<StockLedgerEntryResult> third = await harness.QueryAsync(
            new ListStockLedgerEntriesQuery(location, Limit: 3, After: second.NextCursor));

        third.HasMore.Should().BeFalse();

        List<Guid> paged = [.. first.Items.Concat(second.Items).Concat(third.Items).Select(entry => entry.Id)];

        paged.Should().HaveCount(7);
        paged.Should().OnlyHaveUniqueItems();
        paged.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task A_location_code_must_be_unique_within_the_tenant()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        await CreateLocationAsync(harness, "MAIN", "Back room");

        Func<Task> again = () => CreateLocationAsync(harness, "main", "Another back room");

        (await again.Should().ThrowAsync<InventoryConflictException>())
            .Which.Code.Should().Be("INVENTORY_LOCATION_CODE_TAKEN");
    }

    [Fact]
    public async Task A_deactivated_location_keeps_its_history()
    {
        await using InventoryHarness harness = await InventoryHarness.CreateAsync(fixture);
        Guid location = await CreateLocationAsync(harness, "MAIN", "Back room");
        await harness.SendAsync(new ReceiveStockCommand(location, harness.ItemId, null, Each(4m), Rand(20m), null));

        await harness.SendAsync(new DeactivateStockLocationCommand(location));

        StockLocation? retired = await harness.Locations.FindAsync(location);
        retired!.IsActive.Should().BeFalse();
        retired.IsDeleted.Should().BeFalse();

        (await harness.Context.StockLedgerEntries.CountAsync(entry => entry.LocationId == location))
            .Should().Be(1);
    }
}
