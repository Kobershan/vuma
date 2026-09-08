using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Inventory;
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
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(12m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();

        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(20m, "EA"),
            ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);

        outcome.Held.Value.Should().Be(12m);
        outcome.Shortfall.Value.Should().Be(8m);
        outcome.ReservationId.Should().NotBeNull();
        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(0m);
    }

    [Fact]
    public async Task Reserve_release_reserve_leaves_availability_where_it_started_with_three_rows()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(10m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();
        Guid orderId = Guid.NewGuid();

        ReserveOutcome first = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, orderId).ConfigureAwait(false);

        await reservations.ReleaseAsync(first.ReservationId!.Value, "Customer cancelled").ConfigureAwait(false);

        ReserveOutcome second = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, orderId).ConfigureAwait(false);

        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(6m);

        await using var check = harness.OpenCompanyDb();
        int rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            check.StockReservations).ConfigureAwait(false);
        rows.Should().Be(3);
    }

    [Fact]
    public async Task Consume_closes_the_hold_and_keeps_it_closed()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(10m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();

        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(6m, "EA"),
            ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);

        await reservations.ConsumeAsync(outcome.ReservationId!.Value, Guid.NewGuid()).ConfigureAwait(false);

        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(10m);

        Func<Task> again = () => reservations.ConsumeAsync(outcome.ReservationId!.Value, Guid.NewGuid());
        await again.Should().ThrowAsync<InventoryRuleException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Two_concurrent_orders_for_the_last_five_units_yield_one_winner_and_one_backorder()
    {
        // Genuinely parallel transactions on two connections: a barrier starts both at once, and
        // the invariant asserted is structural (5 held total, nothing negative), never timing.
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(5m).ConfigureAwait(false);

        IReservationService firstService = harness.CreateService();
        IReservationService secondService = harness.CreateService();

        using var barrier = new Barrier(2);

        Task<ReserveOutcome> First() => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await firstService.ReserveAsync(
                harness.LocationId, harness.ItemId, null,
                new Quantity(5m, "EA"),
                ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);
        });

        Task<ReserveOutcome> Second() => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await secondService.ReserveAsync(
                harness.LocationId, harness.ItemId, null,
                new Quantity(5m, "EA"),
                ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);
        });

        ReserveOutcome[] outcomes = await Task.WhenAll(First(), Second()).ConfigureAwait(false);

        outcomes.Sum(outcome => outcome.Held.Value).Should().Be(5m);
        outcomes.Count(outcome => outcome.Shortfall.Value == 5m).Should().Be(1);
        outcomes.Count(outcome => outcome.Shortfall.Value == 0m).Should().Be(1);
        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(0m);
    }

    [Fact]
    public async Task Expired_holds_free_their_stock_with_an_expiry_row()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(10m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();

        harness.Clock.Advance(TimeSpan.FromHours(1));
        ReserveOutcome outcome = await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(4m, "EA"),
            ReservationSource.Order, Guid.NewGuid(),
            expiresAt: harness.Clock.UtcNow.AddHours(72)).ConfigureAwait(false);

        outcome.Held.Value.Should().Be(4m);
        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(6m);

        harness.Clock.Advance(TimeSpan.FromHours(73));
        int expired = await reservations.ExpireDueAsync().ConfigureAwait(false);

        expired.Should().Be(1);
        (await harness.ReadAvailableAsync().ConfigureAwait(false)).Should().Be(10m);
    }
}
