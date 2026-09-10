using VumaRetail.PublicApi.Loyalty;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// The public surface's fixed-window rate limiter: per-caller buckets, honest Retry-After.
/// </summary>
public sealed class RateLimiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_hundred_till_requests_pass()
    {
        var limiter = new LoyaltyRateLimiter();
        for (int index = 0; index < 100; index++)
        {
            limiter.TryAcquire("terminal:till-1", 100, Now, out _).Should().BeTrue();
        }
    }

    [Fact]
    public void Hundred_and_first_request_is_refused_with_retry_after()
    {
        var limiter = new LoyaltyRateLimiter();
        for (int index = 0; index < 100; index++)
        {
            limiter.TryAcquire("terminal:till-1", 100, Now, out _);
        }

        limiter.TryAcquire("terminal:till-1", 100, Now, out TimeSpan retryAfter)
            .Should().BeFalse();
        retryAfter.Should().BePositive();
        retryAfter.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Window_resets_after_a_minute()
    {
        var limiter = new LoyaltyRateLimiter();
        for (int index = 0; index < 100; index++)
        {
            limiter.TryAcquire("terminal:till-1", 100, Now, out _);
        }

        limiter.TryAcquire("terminal:till-1", 100, Now.AddMinutes(1).AddSeconds(1), out _)
            .Should().BeTrue();
    }

    [Fact]
    public void Buckets_are_per_caller()
    {
        var limiter = new LoyaltyRateLimiter();
        for (int index = 0; index < 30; index++)
        {
            limiter.TryAcquire("caller:member-a", 30, Now, out _);
        }

        limiter.TryAcquire("caller:member-a", 30, Now, out _).Should().BeFalse();
        limiter.TryAcquire("caller:member-b", 30, Now, out _).Should().BeTrue();
    }
}

/// <summary>
/// Public display rounding matches the domain's rule without reaching into it (ADR-021).
/// </summary>
public sealed class LoyaltyDisplayTests
{
    [Fact]
    public void Display_rounds_half_away_from_zero()
    {
        LoyaltyDisplay.ToDisplay(49.9950m).Should().Be(50.00m);
    }

    [Fact]
    public void Display_matches_domain_rule()
    {
        LoyaltyDisplay.ToDisplay(123.454m).Should().Be(
            Domain.Loyalty.LoyaltyCalculator.ToDisplay(123.454m));
    }
}
