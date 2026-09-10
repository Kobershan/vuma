using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Points calculation through the real calculator: earn, burn, rounding, expiry.
/// </summary>
public sealed class PointsCalculationTests
{
    [Fact]
    public void Earn_of_150_zar_at_1_point_per_zar_yields_150_points()
    {
        LoyaltyCalculator.ComputeEarn(150m, 1m).Should().Be(150m);
    }

    [Fact]
    public void Earn_with_rounding_edge_case_rounds_half_away_from_zero()
    {
        // 99.99 x 0.5 = 49.995 exactly: stored at scale 4 as 49.9950, displayed at 2dp as 50.00.
        decimal stored = LoyaltyCalculator.ComputeEarn(99.99m, 0.5m);
        stored.Should().Be(49.9950m);
        LoyaltyCalculator.ToDisplay(stored).Should().Be(50.00m);
    }

    [Fact]
    public void Earn_with_gold_tier_multiplier()
    {
        LoyaltyCalculator.ComputeEarn(100m, 1m, 1.5m).Should().Be(150m);
    }

    [Fact]
    public void Burn_of_200_from_500_leaves_300()
    {
        LoyaltyCalculator.ApplyBurn(500m, 200m).Should().Be(300m);
    }

    [Fact]
    public void Burn_rounding_preserves_four_decimal_places()
    {
        LoyaltyCalculator.ApplyBurn(500m, 100.5m).Should().Be(399.5000m);
    }

    [Fact]
    public void Burn_more_than_balance_throws()
    {
        Action act = () => LoyaltyCalculator.ApplyBurn(100m, 200m);
        act.Should().Throw<InsufficientPointsException>();
    }

    [Fact]
    public void Points_earned_400_days_ago_are_expired()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        LoyaltyCalculator.IsExpired(now.AddDays(-400), 365, now).Should().BeTrue();
    }

    [Fact]
    public void Points_earned_300_days_ago_are_not_expired()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        LoyaltyCalculator.IsExpired(now.AddDays(-300), 365, now).Should().BeFalse();
    }

    [Fact]
    public void Negative_earn_amount_is_refused()
    {
        Action act = () => LoyaltyCalculator.ComputeEarn(-10m, 1m);
        act.Should().Throw<InvalidPointsException>();
    }
}
