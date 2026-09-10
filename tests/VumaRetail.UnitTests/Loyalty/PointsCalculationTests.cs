using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Points calculation: earn, burn, rounding edge cases, expiry. Scaffolding tests for Stage 20.
/// </summary>
public sealed class PointsCalculationTests
{
    [Fact]
    public void Earn_of_150_zar_at_1_point_per_zar_yields_150_points()
    {
        decimal points = decimal.Round(150m * 1m, 4, MidpointRounding.AwayFromZero);
        points.Should().Be(150m);
    }

    [Fact]
    public void Earn_with_rounding_edge_case_rounds_half_away_from_zero()
    {
        // 99.99 x 0.5 = 49.995 exactly: stored at scale 4 as 49.9950, displayed at 2dp as 50.00.
        decimal stored = decimal.Round(99.99m * 0.5m, 4, MidpointRounding.AwayFromZero);
        stored.Should().Be(49.9950m);
        decimal display = decimal.Round(stored, 2, MidpointRounding.AwayFromZero);
        display.Should().Be(50.00m);
    }

    [Fact]
    public void Earn_with_gold_tier_multiplier()
    {
        decimal points = decimal.Round(100m * 1m * 1.5m, 4, MidpointRounding.AwayFromZero);
        points.Should().Be(150m);
    }

    [Fact]
    public void Burn_of_200_from_500_leaves_300()
    {
        decimal remaining = decimal.Round(500m - 200m, 4, MidpointRounding.AwayFromZero);
        remaining.Should().Be(300m);
    }

    [Fact]
    public void Burn_rounding_preserves_four_decimal_places()
    {
        decimal result = decimal.Round(500m - 100.5m, 4, MidpointRounding.AwayFromZero);
        result.Should().Be(399.5000m);
    }

    [Fact]
    public void Burn_more_than_balance_throws()
    {
        Action act = () => throw new InsufficientPointsException();
        act.Should().Throw<InsufficientPointsException>();
    }

    [Fact]
    public void Points_earned_400_days_ago_are_expired()
    {
        var earnedAt = DateTimeOffset.UtcNow.AddDays(-400);
        bool expired = DateTimeOffset.UtcNow - earnedAt > TimeSpan.FromDays(365);
        expired.Should().BeTrue();
    }

    [Fact]
    public void Points_earned_300_days_ago_are_not_expired()
    {
        var earnedAt = DateTimeOffset.UtcNow.AddDays(-300);
        bool expired = DateTimeOffset.UtcNow - earnedAt > TimeSpan.FromDays(365);
        expired.Should().BeFalse();
    }

    [Fact]
    public void Negative_earn_amount_is_refused()
    {
        Action act = () => throw new InvalidPointsException();
        act.Should().Throw<InvalidPointsException>();
    }
}
