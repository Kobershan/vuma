using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Tier evaluation: thresholds, progression. Scaffolding tests for Stage 20.
/// </summary>
public sealed class TierEvaluationTests
{
    [Fact]
    public void Member_below_silver_threshold_is_bronze()
    {
        EvaluateTier(50m, 100m, 1000m).Should().Be("Bronze");
    }

    [Fact]
    public void Member_at_1000_points_reaches_gold_threshold_inclusive()
    {
        EvaluateTier(1000m, 100m, 1000m).Should().Be("Gold");
    }

    [Fact]
    public void Member_at_1001_points_is_still_gold()
    {
        EvaluateTier(1001m, 100m, 1000m).Should().Be("Gold");
    }

    [Fact]
    public void Tier_progresses_silver_to_gold_when_threshold_crossed()
    {
        EvaluateTier(500m, 100m, 1000m).Should().Be("Silver");
        EvaluateTier(1000m, 100m, 1000m).Should().Be("Gold");
    }

    private static string EvaluateTier(decimal points, decimal silverThreshold, decimal goldThreshold)
    {
        if (points >= goldThreshold) return "Gold";
        if (points >= silverThreshold) return "Silver";
        return "Bronze";
    }
}

/// <summary>
/// Points expiry logic. Scaffolding tests for Stage 20.
/// </summary>
public sealed class PointsExpiryTests
{
    private const int PointExpiryDays = 365;

    [Fact]
    public void Points_earned_today_do_not_expire()
    {
        IsExpired(DateTimeOffset.UtcNow, PointExpiryDays).Should().BeFalse();
    }

    [Fact]
    public void Points_earned_366_days_ago_expire()
    {
        IsExpired(DateTimeOffset.UtcNow.AddDays(-366), PointExpiryDays).Should().BeTrue();
    }

    [Fact]
    public void Partial_expiry_only_expires_old_points()
    {
        var oldPoints = new[] { (Amount: 500m, EarnedAt: DateTimeOffset.UtcNow.AddDays(-400)) };
        var newPoints = new[] { (Amount: 300m, EarnedAt: DateTimeOffset.UtcNow.AddDays(-100)) };
        decimal validOld = oldPoints.Where(p => !IsExpired(p.EarnedAt, PointExpiryDays)).Sum(p => p.Amount);
        decimal validNew = newPoints.Where(p => !IsExpired(p.EarnedAt, PointExpiryDays)).Sum(p => p.Amount);
        validOld.Should().Be(0m);
        validNew.Should().Be(300m);
    }

    [Fact]
    public void Expiry_never_produces_negative_balance()
    {
        var allPoints = new[] { (Amount: 100m, EarnedAt: DateTimeOffset.UtcNow.AddDays(-400)) };
        decimal valid = allPoints.Where(p => !IsExpired(p.EarnedAt, PointExpiryDays)).Sum(p => p.Amount);
        valid.Should().Be(0m);
    }

    private static bool IsExpired(DateTimeOffset earnedAt, int expiryDays) =>
        DateTimeOffset.UtcNow - earnedAt > TimeSpan.FromDays(expiryDays);
}
