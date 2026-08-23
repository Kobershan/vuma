using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Queries;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Warehouse;

/// <summary>
/// Stage 13's commands through the real dispatcher against a real database: bins subdividing a Stage
/// 08 location, putaway that never touches the location ledger, and pick/pack/ship and cycle counts
/// that do (business rules 2–4).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WarehouseCommandTests(PostgresFixture fixture)
{
    private static Quantity Each(decimal value) => new(value, "EA");

    private static async Task<(Guid ZoneId, Guid BinId)> CreateZoneAndBinAsync(
        WarehouseHarness harness, string zoneCode, string binCode)
    {
        Guid zoneId = await harness.SendAsync(new CreateZoneCommand(harness.LocationId, zoneCode, zoneCode, ZoneType.Storage));
        Guid binId = await harness.SendAsync(new CreateBinCommand(zoneId, binCode, binCode, BinType.Shelf));
        return (zoneId, binId);
    }

    [Fact]
    public async Task A_zone_and_bin_can_be_created_and_read_back()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);

        (Guid zoneId, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        var zones = await harness.QueryAsync(new ListZonesQuery(harness.LocationId));
        zones.Should().ContainSingle(zone => zone.Id == zoneId && zone.Code == "STOR-A");

        var bins = await harness.QueryAsync(new ListBinsForZoneQuery(zoneId));
        bins.Should().ContainSingle(bin => bin.Id == binId && bin.Code == "A-01");
    }

    [Fact]
    public async Task A_second_zone_with_the_same_code_at_the_same_location_is_refused()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        await harness.SendAsync(new CreateZoneCommand(harness.LocationId, "STOR-A", "Storage A", ZoneType.Storage));

        Func<Task> creating = () => harness.SendAsync(
            new CreateZoneCommand(harness.LocationId, "stor-a", "Storage A Again", ZoneType.Storage));

        (await creating.Should().ThrowAsync<WarehouseConflictException>())
            .Which.Code.Should().Be("WAREHOUSE_ZONE_CODE_TAKEN");
    }

    [Fact]
    public async Task Putaway_shelves_stock_into_a_bin_without_touching_the_locations_own_balance()
    {
        // Business rule 2: the location-level receipt already happened (a Stage 08/12 concern);
        // putaway only redistributes it inside bins.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "REC-A", "A-01");

        Guid ledgerEntryId = await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(50m), new Money(10m, "ZAR"), "PO delivery"));

        StockBalance? balanceBefore = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balanceBefore!.QuantityOnHand.Value.Should().Be(50m);

        Guid taskId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(50m), PutawaySourceReferenceType.ManualReceipt, ledgerEntryId));

        await harness.SendAsync(new ConfirmPutawayCommand(taskId, binId, Each(50m)));

        StockBalance? balanceAfter = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balanceAfter!.QuantityOnHand.Value.Should().Be(50m, "putaway redistributes, it does not create or consume stock");

        BinStockResult? binStock = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        binStock!.QuantityOnHand.Value.Should().Be(50m);

        PutawayTaskResult task = await harness.QueryAsync(new GetPutawayTaskQuery(taskId));
        task.Status.Should().Be(PutawayStatus.Confirmed);
        task.ConfirmedBinId.Should().Be(binId);
    }

    [Fact]
    public async Task A_putaway_task_can_be_confirmed_into_a_bin_that_differs_from_the_suggestion()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (Guid zoneId, Guid suggestedBin) = await CreateZoneAndBinAsync(harness, "REC-A", "A-01");
        Guid otherBin = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "A-02", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(5m, "ZAR"), null));

        Guid taskId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));

        await harness.SendAsync(new ConfirmPutawayCommand(taskId, otherBin, Each(10m)));

        BinStockResult? atOther = await harness.QueryAsync(new GetBinStockQuery(otherBin, harness.ItemId, null));
        atOther!.QuantityOnHand.Value.Should().Be(10m);

        // The suggested bin never received anything — confirming elsewhere is a legitimate override.
        _ = suggestedBin;
    }

    [Fact]
    public async Task The_whole_chain_receive_putaway_pick_pack_ship_really_moves_stock_out_of_the_location()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(20m), new Money(15m, "ZAR"), "Opening stock"));

        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(20m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(20m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        Guid pickTaskId = await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(8m), "SO-2001"));

        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        PickWaveResult released = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        released.Status.Should().Be(PickWaveStatus.Released);
        released.Tasks.Should().ContainSingle(task => task.Id == pickTaskId && task.AllocatedBinId == binId);

        await harness.SendAsync(new ConfirmPickCommand(pickTaskId, Each(8m)));

        PickWaveResult picked = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        picked.Status.Should().Be(PickWaveStatus.Picked, "every line on the wave is settled");

        BinStockResult? binAfterPick = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        binAfterPick!.QuantityOnHand.Value.Should().Be(12m, "the bin was relieved at pick time");

        await harness.SendAsync(new PackWaveCommand(waveId, 1, "One box"));
        Guid shipmentId = await harness.SendAsync(new ShipWaveCommand(waveId, "Courier Co", "TRK-001"));

        PickWaveResult shipped = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        shipped.Status.Should().Be(PickWaveStatus.Shipped);

        StockBalance? locationBalance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        locationBalance!.QuantityOnHand.Value.Should().Be(12m, "the location itself really lost the shipped quantity");

        StockLedgerEntry shipmentEntry = harness.Context.StockLedgerEntries
            .Single(entry => entry.ReferenceId == shipmentId);

        shipmentEntry.ReferenceType.Should().Be(StockReferenceType.Shipment);
        shipmentEntry.Quantity.Value.Should().Be(-8m);
        shipmentEntry.BinId.Should().BeNull("the wave may have picked from more than one bin; the location-level entry does not name one");

        ShipmentConfirmationResult shipment = await harness.QueryAsync(new GetShipmentConfirmationQuery(waveId));
        shipment.Id.Should().Be(shipmentId);
        shipment.Carrier.Should().Be("Courier Co");

        PackTaskResult pack = await harness.QueryAsync(new GetPackTaskQuery(waveId));
        pack.PackageCount.Should().Be(1);
    }

    [Fact]
    public async Task A_short_pick_is_recorded_and_ships_only_what_was_actually_picked()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        Guid pickTaskId = await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(10m), "SO-2002"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        // Only 6 physically found, even though 10 was allocated — recorded, not refused.
        await harness.SendAsync(new ConfirmPickCommand(pickTaskId, Each(6m)));

        PickWaveResult wave = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        wave.Tasks.Should().ContainSingle(task => task.Status == PickTaskStatus.ShortPicked);
        wave.Status.Should().Be(PickWaveStatus.Picked);

        await harness.SendAsync(new PackWaveCommand(waveId, 1));
        await harness.SendAsync(new ShipWaveCommand(waveId));

        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance!.QuantityOnHand.Value.Should().Be(4m, "only the 6 actually picked left the location");
    }

    [Fact]
    public async Task Shipping_a_wave_with_nothing_picked_is_refused()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));

        Func<Task> shipping = () => harness.SendAsync(new ShipWaveCommand(waveId));

        (await shipping.Should().ThrowAsync<WarehouseRuleException>())
            .Which.Code.Should().Be("WAREHOUSE_NOTHING_PICKED");
    }

    [Fact]
    public async Task Releasing_a_wave_beyond_what_any_bin_holds_is_refused()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(5m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(5m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(5m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(20m), "SO-2003"));

        Func<Task> releasing = () => harness.SendAsync(new ReleasePickWaveCommand(waveId));

        (await releasing.Should().ThrowAsync<WarehouseRuleException>())
            .Which.Code.Should().Be("WAREHOUSE_INSUFFICIENT_STOCK_TO_ALLOCATE");
    }

    [Fact]
    public async Task A_demand_line_spanning_two_bins_is_split_and_allocated_from_both()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (Guid zoneId, Guid binOne) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        Guid binTwo = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "A-02", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(15m), new Money(10m, "ZAR"), null));

        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(15m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binOne, Each(9m)));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binTwo, Each(6m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(12m), "SO-2004"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        PickWaveResult wave = await harness.QueryAsync(new GetPickWaveQuery(waveId));

        wave.Tasks.Should().HaveCount(2, "demand beyond the largest bin spills into a second allocated line");
        wave.Tasks.Sum(task => task.AllocatedQuantity!.Value.Value).Should().Be(12m);
        wave.Tasks.Select(task => task.AllocatedBinId).Should().BeEquivalentTo([binOne, binTwo]);
    }

    [Fact]
    public async Task A_cycle_count_variance_corrects_both_the_bin_and_the_locations_own_balance()
    {
        // Business rule 4: a bin-level count that disagrees with the system is real inventory
        // variance at the location, not merely a reshuffle between bins.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(20m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid countId = await harness.SendAsync(new OpenCycleCountCommand(harness.LocationId));

        // Physically found only 7, three fewer than the system expects.
        await harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(7m)));

        await harness.SendAsync(new FinalizeCycleCountCommand(countId));

        BinStockResult? binStock = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        binStock!.QuantityOnHand.Value.Should().Be(7m);

        StockBalance? locationBalance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        locationBalance!.QuantityOnHand.Value.Should().Be(7m, "the location's own on-hand quantity moved too");

        CycleCountResult count = await harness.QueryAsync(new GetCycleCountQuery(countId));
        count.Status.Should().Be(CycleCountStatus.Finalized);
        count.Lines.Should().ContainSingle(line => line.Variance.Value == -3m);

        // The exit checklist's "reaches Stage 07 through Stage 08's existing rules, verified rather
        // than assumed": the movement type is the whole of what picks the seeded rule key
        // (`EventTypeFor` maps StocktakeVariance to inventory.stocktake.shortage / .surplus), so a
        // cycle count raising StocktakeVariance is a cycle count reaching the rules Stage 08 seeded.
        // If this stage had invented a movement type of its own, this assertion is what would fail.
        InventoryValuationEvent variance = harness.ValuationEvents.Events
            .Single(raised => raised.ReferenceType == StockReferenceType.CycleCount);

        variance.MovementType.Should().Be(
            StockMovementType.StocktakeVariance,
            "no new posting rule is registered by this stage — a cycle count is economically a stocktake");
        variance.Value.IsNegative.Should().BeTrue("a shortage, so inventory.stocktake.shortage is the rule it lands on");
    }

    [Fact]
    public async Task A_recount_before_finalize_replaces_the_line_rather_than_adding_a_second_one()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(20m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid countId = await harness.SendAsync(new OpenCycleCountCommand(harness.LocationId));

        Guid lineId = await harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(7m)));
        Guid recountId = await harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(9m)));

        recountId.Should().Be(lineId);

        CycleCountResult count = await harness.QueryAsync(new GetCycleCountQuery(countId));
        count.Lines.Should().ContainSingle();
        count.Lines[0].CountedQuantity.Value.Should().Be(9m);
    }

    [Fact]
    public async Task A_finalized_cycle_count_refuses_a_further_count_and_a_second_finalize()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(5m), new Money(20m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(5m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(5m)));

        Guid countId = await harness.SendAsync(new OpenCycleCountCommand(harness.LocationId));
        await harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(5m)));
        await harness.SendAsync(new FinalizeCycleCountCommand(countId));

        Func<Task> recounting = () => harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(1m)));
        Func<Task> refinalizing = () => harness.SendAsync(new FinalizeCycleCountCommand(countId));

        (await recounting.Should().ThrowAsync<WarehouseRuleException>())
            .Which.Code.Should().Be("WAREHOUSE_CYCLE_COUNT_ALREADY_FINALIZED");
        (await refinalizing.Should().ThrowAsync<WarehouseRuleException>())
            .Which.Code.Should().Be("WAREHOUSE_CYCLE_COUNT_ALREADY_FINALIZED");
    }

    [Fact]
    public async Task Stock_can_be_moved_directly_from_one_bin_to_another()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (Guid zoneId, Guid binOne) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        Guid binTwo = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "A-02", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binOne, Each(10m)));

        await harness.SendAsync(new MoveBinStockCommand(binOne, binTwo, harness.ItemId, null, Each(4m)));

        BinStockResult? fromBin = await harness.QueryAsync(new GetBinStockQuery(binOne, harness.ItemId, null));
        BinStockResult? toBin = await harness.QueryAsync(new GetBinStockQuery(binTwo, harness.ItemId, null));

        fromBin!.QuantityOnHand.Value.Should().Be(6m);
        toBin!.QuantityOnHand.Value.Should().Be(4m);

        StockBalance? locationBalance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        locationBalance!.QuantityOnHand.Value.Should().Be(10m, "an internal move never changes what the location holds");
    }

    [Fact]
    public async Task A_shipment_issues_one_ledger_entry_per_SKU_however_many_bins_it_was_picked_from()
    {
        // Business rule 3 and the stage document's ShipmentConfirmation acceptance: two pick lines for
        // the same SKU in two different bins are one economic event at the location, so the wave must
        // post one issue for their sum — not one per bin, and not one per line.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (Guid zoneId, Guid binOne) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        Guid binTwo = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "A-02", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(15m), new Money(10m, "ZAR"), null));

        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(15m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binOne, Each(9m)));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binTwo, Each(6m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(12m), "SO-2005"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        PickWaveResult released = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        released.Tasks.Should().HaveCount(2, "the demand spans two bins");

        foreach (PickTaskResult task in released.Tasks)
        {
            await harness.SendAsync(new ConfirmPickCommand(task.Id, task.AllocatedQuantity!.Value));
        }

        await harness.SendAsync(new PackWaveCommand(waveId, 1, null));
        Guid shipmentId = await harness.SendAsync(new ShipWaveCommand(waveId, "Courier Co", "TRK-005"));

        StockLedgerEntry[] shipmentEntries = [.. harness.Context.StockLedgerEntries
            .Where(entry => entry.ReferenceId == shipmentId)];

        shipmentEntries.Should().ContainSingle("one issue per distinct SKU across the wave, not one per bin");
        shipmentEntries[0].Quantity.Value.Should().Be(-12m, "the SKU's picked quantity is summed across both lines");

        StockBalance? locationBalance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        locationBalance!.QuantityOnHand.Value.Should().Be(3m);

        BinStockResult? fromBinOne = await harness.QueryAsync(new GetBinStockQuery(binOne, harness.ItemId, null));
        BinStockResult? fromBinTwo = await harness.QueryAsync(new GetBinStockQuery(binTwo, harness.ItemId, null));
        (fromBinOne!.QuantityOnHand.Value + fromBinTwo!.QuantityOnHand.Value).Should().Be(3m,
            "both bins were relieved, and between them they hold what the location still holds");
    }

    [Fact]
    public async Task A_bin_tagged_post_carries_its_bin_onto_the_ledger_and_an_untagged_one_leaves_it_null()
    {
        // ADR-087's regression test: the bin id is additive. A Stage 08/09/10/12 call site that never
        // passes one must keep producing exactly the entry it produced before this stage existed, and
        // a Stage 13 call site that has one must record it.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(20m, "ZAR"), null));

        StockLedgerEntry receipt = harness.Context.StockLedgerEntries
            .Single(entry => entry.ReferenceType == StockReferenceType.Manual);

        receipt.BinId.Should().BeNull("a Stage 08 receipt names no bin and must behave exactly as before");

        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        harness.Context.StockLedgerEntries.Count().Should().Be(1, "business rule 2: putaway posts no ledger entry at all");

        Guid countId = await harness.SendAsync(new OpenCycleCountCommand(harness.LocationId));
        await harness.SendAsync(new RecordCycleCountCommand(countId, binId, harness.ItemId, null, Each(8m)));
        await harness.SendAsync(new FinalizeCycleCountCommand(countId));

        StockLedgerEntry variance = harness.Context.StockLedgerEntries
            .Single(entry => entry.ReferenceType == StockReferenceType.CycleCount);

        variance.BinId.Should().Be(binId, "the count knew which bin disagreed, so the ledger records it");
        variance.Quantity.Value.Should().Be(-2m);
    }

    [Fact]
    public async Task AddPickTaskCommand_replayed_with_the_same_id_returns_the_existing_task_instead_of_adding_a_second_one()
    {
        // §4.19 — the dropped-connection retry that used to double-add the line.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        Guid taskId = UuidV7.NewGuid();

        Guid first = await harness.SendAsync(
            new AddPickTaskCommand(waveId, harness.ItemId, null, Each(8m), "SO-3001", taskId));
        Guid replayed = await harness.SendAsync(
            new AddPickTaskCommand(waveId, harness.ItemId, null, Each(8m), "SO-3001", taskId));

        replayed.Should().Be(first);
        PickWaveResult wave = await harness.QueryAsync(new GetPickWaveQuery(waveId));
        wave.Tasks.Should().ContainSingle(task => task.Id == taskId, "the replay must not add a second line");
    }

    [Fact]
    public async Task OpenPutawayTaskCommand_replayed_with_the_same_id_returns_the_existing_task_instead_of_opening_a_second_one()
    {
        // §4.19 — the dropped-connection retry that used to double-open the task.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(5m, "ZAR"), null));
        Guid taskId = UuidV7.NewGuid();

        Guid first = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt, null, taskId));
        Guid replayed = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt, null, taskId));

        replayed.Should().Be(first);
        IReadOnlyList<PutawayTaskResult> pending = await harness.QueryAsync(new ListPendingPutawayTasksQuery(harness.LocationId));
        pending.Should().ContainSingle(task => task.Id == taskId, "the replay must not open a second task");
    }

    [Fact]
    public async Task MoveBinStockCommand_replayed_with_the_same_transfer_id_does_not_move_the_quantity_twice()
    {
        // §4.19 — the dropped-connection retry that used to move the same quantity a second time.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (Guid zoneId, Guid binOne) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");
        Guid binTwo = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "A-02", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binOne, Each(10m)));

        Guid transferId = UuidV7.NewGuid();
        await harness.SendAsync(new MoveBinStockCommand(binOne, binTwo, harness.ItemId, null, Each(4m), transferId));
        await harness.SendAsync(new MoveBinStockCommand(binOne, binTwo, harness.ItemId, null, Each(4m), transferId));

        BinStockResult? fromBin = await harness.QueryAsync(new GetBinStockQuery(binOne, harness.ItemId, null));
        BinStockResult? toBin = await harness.QueryAsync(new GetBinStockQuery(binTwo, harness.ItemId, null));

        fromBin!.QuantityOnHand.Value.Should().Be(6m, "the replay must not relieve the source bin twice");
        toBin!.QuantityOnHand.Value.Should().Be(4m, "the replay must not credit the destination bin twice");
    }

    [Fact]
    public async Task Releasing_a_second_wave_against_the_same_bin_only_sees_what_the_first_wave_did_not_reserve()
    {
        // §4.19's other CRITICAL: allocation was advisory-only — nothing marked a bin's stock as spoken
        // for, so two waves released one after another against the same bin could both allocate the full
        // on-hand quantity. A wave's own release now writes a real reservation (Reserve), and the
        // allocator ranks and filters on Available (on-hand less reserved), not raw on-hand.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid firstWaveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(firstWaveId, harness.ItemId, null, Each(7m), "SO-4001"));
        await harness.SendAsync(new ReleasePickWaveCommand(firstWaveId));

        BinStockResult? afterFirstRelease = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        afterFirstRelease!.QuantityOnHand.Value.Should().Be(10m, "a reservation never moves physical stock");
        afterFirstRelease.QuantityReserved.Value.Should().Be(7m);
        afterFirstRelease.Available.Value.Should().Be(3m);

        Guid secondWaveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(secondWaveId, harness.ItemId, null, Each(5m), "SO-4002"));

        Func<Task> releasingSecond = () => harness.SendAsync(new ReleasePickWaveCommand(secondWaveId));

        // Only 3 is genuinely available — the other 7 already belongs to the first wave's reservation.
        (await releasingSecond.Should().ThrowAsync<WarehouseRuleException>())
            .Which.Code.Should().Be("WAREHOUSE_INSUFFICIENT_STOCK_TO_ALLOCATE");
    }

    [Fact]
    public async Task A_short_pick_releases_its_whole_reservation_not_only_what_was_picked()
    {
        // The unpicked remainder of a short pick must not sit as a phantom reservation forever.
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        Guid pickTaskId = await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(10m), "SO-4003"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        await harness.SendAsync(new ConfirmPickCommand(pickTaskId, Each(6m)));

        BinStockResult? bin = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        bin!.QuantityOnHand.Value.Should().Be(4m, "only what was physically picked left the bin");
        bin.QuantityReserved.Value.Should().Be(0m, "the whole reservation clears on confirm, even a short one");
        bin.Available.Value.Should().Be(4m, "the unpicked remainder is available to the next wave immediately");
    }

    [Fact]
    public async Task Cancelling_an_allocated_task_releases_its_reservation()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        Guid pickTaskId = await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(10m), "SO-4004"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        await harness.SendAsync(new CancelPickTaskCommand(pickTaskId));

        BinStockResult? bin = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        bin!.QuantityReserved.Value.Should().Be(0m);
        bin.Available.Value.Should().Be(10m, "an abandoned task must not leave its stock permanently unpromisable");
    }

    [Fact]
    public async Task Cancelling_a_released_wave_releases_every_task_still_holding_a_reservation()
    {
        await using WarehouseHarness harness = await WarehouseHarness.CreateAsync(fixture);
        (_, Guid binId) = await CreateZoneAndBinAsync(harness, "STOR-A", "A-01");

        await harness.SendAsync(new ReceiveStockCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), new Money(10m, "ZAR"), null));
        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            harness.LocationId, harness.ItemId, null, Each(10m), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, Each(10m)));

        Guid waveId = await harness.SendAsync(new OpenPickWaveCommand(harness.LocationId));
        await harness.SendAsync(new AddPickTaskCommand(waveId, harness.ItemId, null, Each(10m), "SO-4005"));
        await harness.SendAsync(new ReleasePickWaveCommand(waveId));

        BinStockResult? beforeCancel = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        beforeCancel!.QuantityReserved.Value.Should().Be(10m);

        await harness.SendAsync(new CancelPickWaveCommand(waveId));

        BinStockResult? afterCancel = await harness.QueryAsync(new GetBinStockQuery(binId, harness.ItemId, null));
        afterCancel!.QuantityReserved.Value.Should().Be(0m, "cancelling the wave must not leak its tasks' reservations");
        afterCancel.Available.Value.Should().Be(10m);
    }
}
