using FluentAssertions;
using Xunit;
using VumaRetail.ControlPlane;

namespace VumaRetail.ControlPlane.Tests;

public sealed class BillingServicesTests
{
    [Fact]
    public void Rollups_are_deduplicated_and_aggregated_by_tenant()
    {
        var aggregator = new UsageRollupAggregator();
        var row = new UsageRollup("tenant", "node", new DateOnly(2026, 9, 1), 10, 2, 1, 100,
            new Dictionary<string, long> { ["sales"] = 3 });
        aggregator.Add(row).Should().BeTrue();
        aggregator.Add(row).Should().BeFalse();
        aggregator.ForTenant("tenant", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))
            .Should().BeEquivalentTo(new BillingUsage(10, 2, 1, 100, new Dictionary<string, long> { ["sales"] = 3 }));
    }

    [Fact]
    public void Billing_calculates_overage_and_proration()
    {
        BillingPlan plan = new("pro", 100m, 10, 2.5m);
        BillingCalculator.Calculate(plan, new BillingUsage(14, 0, 0, 0, new Dictionary<string, long>()))
            .Should().Be(new BillingCharge(100m, 10m, 110m));
        BillingCalculator.Prorate(100m, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 16))
            .Should().Be(50m);
    }

    [Fact]
    public void Dunning_pauses_until_notifications_are_delivered()
    {
        var tracker = new DunningTracker();
        tracker.RecordNotice(new DunningNotice(Guid.NewGuid(), new DateOnly(2026, 9, 3), false));
        tracker.IsPaused.Should().BeTrue();
        tracker.CanAdvance(new DateOnly(2026, 9, 4)).Should().BeFalse();
        tracker.RecordNotice(new DunningNotice(Guid.NewGuid(), new DateOnly(2026, 9, 4), true));
        tracker.CanAdvance(new DateOnly(2026, 9, 4)).Should().BeFalse();
    }
}
