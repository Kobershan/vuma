using VumaRetail.Application.Loyalty;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Tiers: cached definitions, multiplier floor, catalogue ordering from the engine boundary.
/// </summary>
public sealed class TierEvaluationTests
{
    [Fact]
    public void Non_positive_multiplier_falls_back_to_one()
    {
        var tier = new LoyaltyTier(
            Guid.NewGuid(), Guid.NewGuid(), "broken", "Broken", "Broken",
            0m, 0m, DateTimeOffset.UtcNow);
        tier.Multiplier.Should().Be(1m);
    }

    [Fact]
    public void Catalogue_lists_tiers_threshold_ascending()
    {
        var orbit = new Infrastructure.Loyalty.InMemoryOrbitClient();
        IReadOnlyList<TierDefinition> tiers = orbit.ListTiersAsync().GetAwaiter().GetResult();
        tiers.Select(tier => tier.ThresholdPoints)
            .Should().BeInAscendingOrder();
        tiers.Should().Contain(tier => tier.TierId == "gold" && tier.Multiplier == 1.5m);
    }

    [Fact]
    public void Member_at_gold_threshold_keeps_gold_on_sync()
    {
        var orbit = new Infrastructure.Loyalty.InMemoryOrbitClient();
        var customer = Guid.NewGuid();
        string orbitId = orbit.EnsureMemberAsync(customer).GetAwaiter().GetResult();
        orbit.SetLedger(orbitId, 5000m, "gold");

        OrbitBalanceResult balance = orbit.GetBalanceAsync(orbitId).GetAwaiter().GetResult();
        balance.TierId.Should().Be("gold");
    }

    [Fact]
    public void Tier_refresh_updates_cache()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var tier = new LoyaltyTier(
            Guid.NewGuid(), Guid.NewGuid(), "silver", "Silver", "Silver",
            1000m, 1.25m, now);
        tier.Refresh("Silver Plus", 900m, 1.3m, now.AddHours(1));
        tier.DisplayName.Should().Be("Silver Plus");
        tier.ThresholdPoints.Should().Be(900m);
        tier.SyncedAt.Should().Be(now.AddHours(1));
    }
}

/// <summary>
/// Points expiry logic through the real calculator.
/// </summary>
public sealed class PointsExpiryTests
{
    private const int PointExpiryDays = 365;

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Points_earned_today_do_not_expire()
    {
        LoyaltyCalculator.IsExpired(Now, PointExpiryDays, Now).Should().BeFalse();
    }

    [Fact]
    public void Points_earned_366_days_ago_expire()
    {
        LoyaltyCalculator.IsExpired(Now.AddDays(-366), PointExpiryDays, Now).Should().BeTrue();
    }

    [Fact]
    public void Partial_expiry_only_expires_old_points()
    {
        var oldPoints = new[] { (Amount: 500m, EarnedAt: Now.AddDays(-400)) };
        var newPoints = new[] { (Amount: 300m, EarnedAt: Now.AddDays(-100)) };
        decimal validOld = oldPoints
            .Where(p => !LoyaltyCalculator.IsExpired(p.EarnedAt, PointExpiryDays, Now))
            .Sum(p => p.Amount);
        decimal validNew = newPoints
            .Where(p => !LoyaltyCalculator.IsExpired(p.EarnedAt, PointExpiryDays, Now))
            .Sum(p => p.Amount);
        validOld.Should().Be(0m);
        validNew.Should().Be(300m);
    }

    [Fact]
    public void Expiry_boundary_is_inclusive()
    {
        LoyaltyCalculator.IsExpired(Now.AddDays(-365), PointExpiryDays, Now).Should().BeTrue();
    }
}
