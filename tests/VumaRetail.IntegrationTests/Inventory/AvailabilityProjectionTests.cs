using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Inventory;

/// <summary>
/// The group availability projection against real PostgreSQL: direct publish on write, the
/// outbox-tail relay as catch-up, stale-contributor disclosure, and the rebuild-equals-incremental
/// proof after 500 random movements and reservations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AvailabilityProjectionTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Binds the shared PostgreSQL fixture.</summary>
    public AvailabilityProjectionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_reserve_is_visible_in_the_group_view_with_its_as_at()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(12m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();

        await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(5m, "EA"),
            ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);

        var reader = new RegistryAvailabilityReader(harness.Registry, harness.Clock);
        GroupAvailabilityView view = await reader
            .ReadAsync(harness.ItemId, null, TimeSpan.FromMinutes(15)).ConfigureAwait(false);

        GroupAvailabilityContribution contribution = view.Contributions.Should().ContainSingle().Subject;
        contribution.Promise.Available.Value.Should().Be(7m);
        contribution.Promise.OnHand.Value.Should().Be(12m);
        contribution.Promise.Reserved.Value.Should().Be(5m);
        contribution.IsStale.Should().BeFalse();
        view.HasStaleContributors.Should().BeFalse();
    }

    [Fact]
    public async Task A_quiet_contributor_is_named_stale_not_summed_silently()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(12m).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();

        await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(5m, "EA"),
            ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);

        harness.Clock.Advance(TimeSpan.FromMinutes(16));

        var reader = new RegistryAvailabilityReader(harness.Registry, harness.Clock);
        GroupAvailabilityView view = await reader
            .ReadAsync(harness.ItemId, null, TimeSpan.FromMinutes(15)).ConfigureAwait(false);

        view.HasStaleContributors.Should().BeTrue();
        view.StaleContributorCodes.Should().ContainSingle().Which.Should().Be("SH");
        view.TotalFreshAvailable.Should().Be(0m);
    }

    [Fact]
    public async Task The_relay_heals_a_projection_the_direct_publish_never_reached()
    {
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        await harness.ReceiveAsync(12m).ConfigureAwait(false);

        // No direct publish on this service: the projection starts empty, the way a crash
        // between the company commit and the publish would leave it.
        IReservationService reservations = harness.CreateService(publish: false);

        await reservations.ReserveAsync(
            harness.LocationId, harness.ItemId, null,
            new Quantity(5m, "EA"),
            ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);

        harness.Registry.GroupAvailabilityRows.Should().BeEmpty();

        await using var companyDb = harness.OpenCompanyDb();
        int applied = await harness.Relay.RelayCompanyAsync(
            companyDb, harness.Registry, harness.TenantId, harness.CompanyId,
            harness.Clock.UtcNow).ConfigureAwait(false);

        applied.Should().BeGreaterThan(0);

        var reader = new RegistryAvailabilityReader(harness.Registry, harness.Clock);
        GroupAvailabilityView view = await reader
            .ReadAsync(harness.ItemId, null, TimeSpan.FromMinutes(15)).ConfigureAwait(false);

        view.Contributions.Should().ContainSingle().Subject.Promise.Available.Value.Should().Be(7m);
    }

    [Fact]
    public async Task Rebuild_from_scratch_equals_the_incremental_projection_after_500_random_operations()
    {
        // Fixed seed: a failure reproduces exactly.
        await using var harness = await AvailabilityHarness.CreateAsync(_fixture).ConfigureAwait(false);
        IReservationService reservations = harness.CreateService();
        var random = new Random(0x08C);

        var openHolds = new List<Guid>();

        for (int step = 0; step < 500; step++)
        {
            int roll = random.Next(100);
            if (roll < 40)
            {
                await harness.ReceiveAsync(random.Next(1, 11)).ConfigureAwait(false);
            }
            else if (roll < 75)
            {
                ReserveOutcome outcome = await reservations.ReserveAsync(
                    harness.LocationId, harness.ItemId, null,
                    new Quantity(random.Next(1, 9), "EA"),
                    ReservationSource.Order, Guid.NewGuid()).ConfigureAwait(false);
                if (outcome.ReservationId.HasValue)
                {
                    openHolds.Add(outcome.ReservationId.Value);
                }
            }
            else if (openHolds.Count > 0)
            {
                int pick = random.Next(openHolds.Count);
                Guid hold = openHolds[pick];
                openHolds.RemoveAt(pick);
                if (roll < 85)
                {
                    await reservations.ReleaseAsync(hold, "random release").ConfigureAwait(false);
                }
                else
                {
                    await reservations.ConsumeAsync(hold, Guid.NewGuid()).ConfigureAwait(false);
                }
            }
        }

        // Incremental truth, straight from the company tables.
        await using var companyDb = harness.OpenCompanyDb();
        StockBalance? balance = await companyDb.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.LocationId == harness.LocationId && b.ItemId == harness.ItemId)
            .ConfigureAwait(false);
        AvailableBalance? position = await companyDb.AvailableBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.LocationId == harness.LocationId && b.ItemId == harness.ItemId)
            .ConfigureAwait(false);

        decimal expectedOnHand = balance?.QuantityOnHand.Value ?? 0m;
        decimal expectedReserved = position?.Reserved.Value ?? 0m;

        // Wipe the projection and rebuild from scratch: the relay path, not the incremental one.
        harness.Registry.GroupAvailabilityRows.RemoveRange(harness.Registry.GroupAvailabilityRows);
        await harness.Registry.SaveChangesAsync().ConfigureAwait(false);

        await harness.Relay.RebuildAsync(
            new Dictionary<Guid, VumaRetail.Infrastructure.Persistence.VumaRetailDbContext>
            {
                [harness.CompanyId] = companyDb,
            },
            harness.Registry,
            harness.TenantId,
            harness.Clock.UtcNow).ConfigureAwait(false);

        var reader = new RegistryAvailabilityReader(harness.Registry, harness.Clock);
        GroupAvailabilityView view = await reader
            .ReadAsync(harness.ItemId, null, TimeSpan.FromMinutes(15)).ConfigureAwait(false);

        GroupAvailabilityContribution rebuilt = view.Contributions.Should().ContainSingle().Subject;
        rebuilt.Promise.OnHand.Value.Should().Be(expectedOnHand);
        rebuilt.Promise.Reserved.Value.Should().Be(expectedReserved);
        rebuilt.Promise.Available.Value.Should().Be(expectedOnHand - expectedReserved);
    }
}
