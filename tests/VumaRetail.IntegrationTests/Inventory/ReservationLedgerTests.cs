using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Inventory;

/// <summary>
/// The reservation ledger against real PostgreSQL: partial holds, release chains, expiry, and the
/// two-order race for the last units — the behaviours a mock cannot fail the way a database does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReservationLedgerTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Binds the shared PostgreSQL fixture.</summary>
    public ReservationLedgerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Reserve_more_than_available_holds_what_exists_and_reports_the_shortfall()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(12m);
        IReservationService reservations = harness.CreateService();

        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(20m, "EA"),
            ReservationSource.Order, Guid.NewGuid());

        outcome.Held.Value.Should().Be(12m);
        outcome.Shortfall.Value.Should().Be(8m);
        outcome.ReservationId.Should().NotBeNull();
        (await harness.ReadAvailableAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task Reserve_release_reserve_leaves_availability_where_it_started_with_three_rows()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(10m);
        IReservationService reservations = harness.CreateService();
        Guid orderId = Guid.NewGuid();

        ReserveOutcome first = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, orderId);

        await reservations.ReleaseAsync(first.ReservationId!.Value, "Customer cancelled");

        ReserveOutcome second = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, orderId);

        (await harness.ReadAvailableAsync()).Should().Be(6m);

        await using var check = harness.OpenCompanyDb();
        int rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            check.StockReservations);
        rows.Should().Be(3);
    }

    [Fact]
    public async Task Consume_closes_the_hold_and_keeps_it_closed()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(10m);
        IReservationService reservations = harness.CreateService();

        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(6m, "EA"),
            ReservationSource.Order, Guid.NewGuid());

        await reservations.ConsumeAsync(outcome.ReservationId!.Value, Guid.NewGuid());

        (await harness.ReadAvailableAsync()).Should().Be(10m);

        Func<Task> again = () => reservations.ConsumeAsync(outcome.ReservationId!.Value, Guid.NewGuid());
        await again.Should().ThrowAsync<InventoryRuleException>();
    }

    [Fact]
    public async Task Releasing_an_already_consumed_hold_refuses_instead_of_appending_a_second_terminal_row()
    {
        // Chain-awareness (Stage 09b fix): a row's born-state stays Held forever, so the seq-0 row
        // of a consumed chain still matches a bare state filter. Releasing it must refuse with the
        // chain-closed error — not append a Released row next to the Consumed one while zeroing a
        // reservation balance another hold now owns. Worked example: hold 6, consume it (reserved
        // 0), hold 6 again (reserved 6), then release the FIRST hold: the naive close zeroes the
        // second hold's availability and leaves one chain with two terminal rows.
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(10m);
        IReservationService reservations = harness.CreateService();
        Guid orderId = Guid.NewGuid();

        ReserveOutcome first = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(6m, "EA"),
            ReservationSource.Order, orderId);
        await reservations.ConsumeAsync(first.ReservationId!.Value, Guid.NewGuid());

        ReserveOutcome second = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(6m, "EA"),
            ReservationSource.Order, orderId);

        Func<Task> staleRelease = () => reservations.ReleaseAsync(first.ReservationId!.Value, "stale compensation");
        await staleRelease.Should().ThrowAsync<InventoryRuleException>();

        // The second hold stands untouched: still open, availability still held.
        (await harness.ReadAvailableAsync()).Should().Be(4m);

        await using var check = harness.OpenCompanyDb();
        int terminalRows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            check.StockReservations.Where(reservation =>
                reservation.ReservationId == first.ReservationId!.Value
                && reservation.State != ReservationState.Held));
        terminalRows.Should().Be(1, "one chain, one terminal row");

        // Cleanup: release the live hold so the ledger ends balanced.
        await reservations.ReleaseAsync(second.ReservationId!.Value, "test cleanup");
    }

    [Fact]
    public async Task Two_concurrent_orders_for_the_last_five_units_yield_one_winner_and_one_backorder()
    {
        // Genuinely parallel transactions on two connections: a barrier starts both at once, and
        // the invariant asserted is structural (5 held total, nothing negative), never timing.
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(5m);

        IReservationService firstService = harness.CreateService();
        IReservationService secondService = harness.CreateService();

        using var barrier = new Barrier(2);

        Task<ReserveOutcome> First() => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await firstService.ReserveAsync(
                harness.LocationId, harness.ItemId, null,
                new Quantity(5m, "EA"),
                ReservationSource.Order, Guid.NewGuid());
        });

        Task<ReserveOutcome> Second() => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await secondService.ReserveAsync(
                harness.LocationId, harness.ItemId, null,
                new Quantity(5m, "EA"),
                ReservationSource.Order, Guid.NewGuid());
        });

        ReserveOutcome[] outcomes = await Task.WhenAll(First(), Second());

        outcomes.Sum(outcome => outcome.Held.Value).Should().Be(5m);
        outcomes.Count(outcome => outcome.Shortfall.Value == 5m).Should().Be(1);
        outcomes.Count(outcome => outcome.Shortfall.Value == 0m).Should().Be(1);
        (await harness.ReadAvailableAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task Expired_holds_free_their_stock_with_an_expiry_row()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture);
        await harness.ReceiveAsync(10m);
        IReservationService reservations = harness.CreateService();

        harness.Clock.Advance(TimeSpan.FromHours(1));
        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, Guid.NewGuid(),
            expiresAt: harness.Clock.UtcNow.AddHours(72));

        outcome.Held.Value.Should().Be(4m);
        (await harness.ReadAvailableAsync()).Should().Be(6m);

        harness.Clock.Advance(TimeSpan.FromHours(73));
        int expired = await reservations.ExpireDueAsync();

        expired.Should().Be(1);
        (await harness.ReadAvailableAsync()).Should().Be(10m);
    }
}
