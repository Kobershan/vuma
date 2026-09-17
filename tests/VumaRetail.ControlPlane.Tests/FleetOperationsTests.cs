using FluentAssertions;
using Xunit;
using VumaRetail.ControlPlane;

namespace VumaRetail.ControlPlane.Tests;

public sealed class FleetOperationsTests
{
    [Fact]
    public void Rollout_selection_is_deterministic_and_halt_is_immediate()
    {
        var fleet = new FleetOperations(new FixedClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)));
        RolloutPlan plan = fleet.StartRollout("2.0", "stable", 25);
        fleet.IsSelected(plan, "node-a").Should().Be(fleet.IsSelected(plan, "node-a"));
        fleet.HaltRollout(plan.Id).Halted.Should().BeTrue();
        fleet.IsSelected(fleet.Rollouts.Single(), "node-a").Should().BeFalse();
    }

    [Fact]
    public void Health_marks_stale_nodes_and_retains_backup_failure()
    {
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var fleet = new FleetOperations(new FixedClock(now));
        fleet.Observe(new FleetNode("node-a", "tenant-a", "1.0", "stable", now.AddHours(-3), false));
        fleet.Health(TimeSpan.FromHours(2)).Should().ContainSingle().Which.Should().Match<FleetHealth>(x =>
            x.Offline && !x.BackupVerified && x.TenantId == "tenant-a");
    }

    [Fact]
    public void Commands_require_known_nodes_and_can_be_halted()
    {
        var fleet = new FleetOperations(new FixedClock(DateTimeOffset.UtcNow));
        fleet.Observe(new FleetNode("node-a", "tenant-a", "1.0", "stable", DateTimeOffset.UtcNow, true));
        RemoteCommand command = fleet.QueueCommand("node-a", "refresh_lease");
        fleet.HaltCommand(command.Id).Halted.Should().BeTrue();
        FluentActions.Invoking(() => fleet.QueueCommand("unknown", "refresh_lease"))
            .Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Rollout_and_health_inputs_fail_closed()
    {
        var fleet = new FleetOperations(new FixedClock(DateTimeOffset.UtcNow));
        FluentActions.Invoking(() => fleet.StartRollout("2.0", "stable", 101))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => fleet.Health(TimeSpan.Zero))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
