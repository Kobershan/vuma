using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Warehouse;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Queries;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

public sealed class ConsolidatedWaveAndCountSheetTests
{
    [Fact]
    public async Task Build_wave_uses_ambient_tenant_and_persists_every_order_breakdown()
    {
        Guid tenantId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid orderOne = Guid.NewGuid();
        Guid orderTwo = Guid.NewGuid();
        IPickWaveRepository waves = Substitute.For<IPickWaveRepository>();
        IPickWaveLineBreakdownRepository breakdowns = Substitute.For<IPickWaveLineBreakdownRepository>();
        IPackSizeResolver packs = Substitute.For<IPackSizeResolver>();
        packs.ResolveAsync(itemId, null, "EA", Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new PackSizeSnapshot("Each"));
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        tenant.StoreId.Returns((Guid?)null);
        var tasks = new List<PickTask>();
        waves.When(x => x.AddTask(Arg.Any<PickTask>()))
            .Do(call => tasks.Add(call.Arg<PickTask>()));

        BuildConsolidatedWaveCommand command = new(
            new PickWaveFilter(
                new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 13),
                "City", "Durban", LocationId: locationId),
            [
                new(orderOne, Guid.NewGuid(), itemId, null, 2m, "EA", "Each", "Durban"),
                new(orderTwo, Guid.NewGuid(), itemId, null, 3m, "EA", "Each", "Durban")
            ]);

        Guid waveId = await new BuildConsolidatedWaveCommandHandler(waves, breakdowns, packs, tenant)
            .HandleAsync(command);

        waveId.Should().NotBeEmpty();
        tasks.Should().ContainSingle().Which.RequestedQuantity.Value.Should().Be(5m);
        tasks[0].TenantId.Should().Be(tenantId);
        breakdowns.Received(1).Add(Arg.Is<PickWaveLineBreakdown>(x => x.OrderId == orderOne && x.Quantity == 2m));
        breakdowns.Received(1).Add(Arg.Is<PickWaveLineBreakdown>(x => x.OrderId == orderTwo && x.Quantity == 3m));
    }

    [Fact]
    public async Task Count_sheet_snapshots_actual_bin_stock_and_warns_for_staging_bins()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Bin bin = Bin.Create(tenantId, storeId, locationId, zoneId, "DISPATCH-01", "Dispatch", BinType.Dispatch);
        BinStock stock = BinStock.Open(tenantId, storeId, bin.Id, itemId, null, "EA");
        stock.ApplyIn(new Domain.Primitives.Quantity(6m, "EA"));
        CountSchedule schedule = CountSchedule.Create(
            tenantId, storeId, "Weekly dispatch count", CountCadence.Weekly, "dispatch", 30, 1,
            DateTimeOffset.UtcNow);
        ICountScheduleRepository schedules = Substitute.For<ICountScheduleRepository>();
        schedules.FindAsync(schedule.Id, Arg.Any<CancellationToken>()).Returns(schedule);
        ICycleCountRepository counts = Substitute.For<ICycleCountRepository>();
        IBinRepository bins = Substitute.For<IBinRepository>();
        bins.ListActiveForLocationAsync(locationId, Arg.Any<CancellationToken>()).Returns([bin]);
        IBinStockRepository stocks = Substitute.For<IBinStockRepository>();
        stocks.ListForBinAsync(bin.Id, Arg.Any<CancellationToken>()).Returns([stock]);
        IBinStockMovementRepository movements = Substitute.For<IBinStockMovementRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        CountSheetResponse response = await new GetCountSheetQueryHandler(schedules, counts, bins, stocks, movements, clock)
            .HandleAsync(new GetCountSheetQuery(schedule.Id, locationId));

        response.Counts.Should().ContainSingle();
        response.InFlightWarnings.Should().ContainSingle().Which.InFlightQuantity.Should().Be(6m);
        counts.Received(1).AddLine(Arg.Is<CycleCountLine>(x =>
            x.BinId == bin.Id && x.ItemId == itemId && x.SystemQuantity.Value == 6m));
    }
}
