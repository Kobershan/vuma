using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Sync.Clock;

namespace VumaRetail.UnitTests.Sync;

/// <summary>
/// The hybrid logical clock of ADR-007, at the two boundaries a plain timestamp gets wrong.
/// </summary>
/// <remarks>
/// Every one of these is testable only because the clock takes an <see cref="IClock"/>
/// (<c>CONVENTIONS.md</c> §6). A class that read <c>DateTimeOffset.UtcNow</c> could be tested for a
/// clock that stalls only by running two operations in the same millisecond and hoping, and for a
/// clock that goes backwards not at all.
/// </remarks>
public sealed class HybridLogicalClockTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advances_with_the_wall_clock()
    {
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp first = clock.Next();
        wall.Advance(TimeSpan.FromSeconds(1));
        HlcStamp second = clock.Next();

        second.PhysicalMilliseconds.Should().BeGreaterThan(first.PhysicalMilliseconds);
        // Physical time carried the order on its own, so the logical counter starts again.
        second.Counter.Should().Be(0);
    }

    [Fact]
    public void Stays_monotonic_while_the_wall_clock_stalls()
    {
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp[] stamps = [.. Enumerable.Range(0, 5).Select(_ => clock.Next())];

        stamps.Should().BeInAscendingOrder();
        stamps.Select(stamp => stamp.Counter).Should().Equal([0, 1, 2, 3, 4]);
    }

    [Fact]
    public void Stays_monotonic_when_the_wall_clock_goes_backwards()
    {
        // NTP correcting a store server that had drifted, or somebody changing the date on a
        // back-office PC. With plain timestamps the node re-issues values it has already used, and
        // last-writer-wins silently reverts every edit made during the overlap.
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp before = clock.Next();

        wall.Advance(TimeSpan.FromMinutes(-5));

        HlcStamp after = clock.Next();

        (after > before).Should().BeTrue();
        after.PhysicalMilliseconds.Should().Be(before.PhysicalMilliseconds, "physical time never rewinds");
        after.Counter.Should().Be(before.Counter + 1);
    }

    [Fact]
    public void Observing_a_stamp_from_the_future_moves_this_node_past_it()
    {
        // The store's clock is five minutes slow and the cloud sends a change stamped ahead of
        // anything the store can issue. Without this, the store keeps stamping *behind* a change it
        // has already applied — so its own next edit loses to the one it was meant to supersede, and
        // the change comes back from the dead on the next sync.
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp remote = new(Origin.AddMinutes(5).ToUnixTimeMilliseconds(), 3, "cloud");

        HlcStamp observed = clock.Observe(remote);

        (observed > remote).Should().BeTrue();

        HlcStamp next = clock.Next();

        (next > remote).Should().BeTrue("the node stays ahead of the remote stamp on every later issue");
        (next > observed).Should().BeTrue();
    }

    [Fact]
    public void Observing_a_stamp_from_the_past_does_not_rewind_this_node()
    {
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp local = clock.Next();
        HlcStamp stale = new(Origin.AddHours(-1).ToUnixTimeMilliseconds(), 0, "store:other");

        HlcStamp observed = clock.Observe(stale);

        (observed > local).Should().BeTrue();
        (observed > stale).Should().BeTrue();
    }

    [Fact]
    public void Observing_a_stamp_in_the_same_millisecond_still_orders_after_it()
    {
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp local = clock.Next();
        HlcStamp remote = new(local.PhysicalMilliseconds, local.Counter + 7, "cloud");

        HlcStamp observed = clock.Observe(remote);

        observed.PhysicalMilliseconds.Should().Be(remote.PhysicalMilliseconds);
        observed.Counter.Should().Be(remote.Counter + 1);
        (observed > remote).Should().BeTrue();
    }

    [Fact]
    public void Never_repeats_a_stamp_under_concurrent_callers()
    {
        // Two tills committing sales through one store server at the same instant. A duplicated
        // stamp is two changes the receiver cannot order, which is a coin toss over which sale
        // survives.
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp[] stamps = [.. Enumerable.Range(0, 500)
            .AsParallel()
            .Select(_ => clock.Next())];

        stamps.Distinct().Should().HaveCount(stamps.Length);
    }

    [Fact]
    public void Current_reports_without_issuing()
    {
        MovableClock wall = new(Origin);
        HybridLogicalClock clock = Build(wall);

        HlcStamp issued = clock.Next();

        clock.Current.Should().Be(issued);
        clock.Current.Should().Be(issued, "reading the clock twice must not advance it");
    }

    [Fact]
    public void Stamps_carry_this_node_id()
    {
        HybridLogicalClock clock = Build(new MovableClock(Origin), "store:jhb01");

        clock.Next().NodeId.Should().Be("store:jhb01");
    }

    private static HybridLogicalClock Build(IClock clock, string nodeId = "store:test")
        => new(clock, new FixedNode(nodeId, NodeKind.Store));

    private sealed class MovableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed record FixedNode(string NodeId, NodeKind Kind) : INodeIdentity;
}
