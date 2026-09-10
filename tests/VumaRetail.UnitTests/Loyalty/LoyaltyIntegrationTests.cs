using NSubstitute;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Earn and redeem through the real handlers: idempotent intent first, Orbit second,
/// queue-and-retry when Orbit is down.
/// </summary>
public sealed class EarnRedeemHandlerTests
{
    [Fact]
    public async Task Earn_creates_transaction_calls_orbit_and_updates_cache()
    {
        var driver = new LoyaltyDriver();
        EarnOutcome outcome = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));

        outcome.Queued.Should().BeFalse();
        outcome.Points.Should().Be(150m);
        outcome.NewBalance.Should().Be(150m);
        driver.Member.BalanceCache.Should().Be(150m);
        driver.Logged.Should().Be(1);
    }

    [Fact]
    public async Task Earn_replay_with_same_key_returns_original_without_reapplying()
    {
        var driver = new LoyaltyDriver();
        var key = Guid.NewGuid();
        EarnOutcome first = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", key));
        EarnOutcome second = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", key));

        second.TransactionId.Should().Be(first.TransactionId);
        second.NewBalance.Should().Be(first.NewBalance);
        driver.Logged.Should().Be(1);
        driver.Member.BalanceCache.Should().Be(100m, "a replay must not credit twice");
    }

    [Fact]
    public async Task Earn_same_key_different_body_is_refused()
    {
        var driver = new LoyaltyDriver();
        var key = Guid.NewGuid();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", key));

        Func<Task> act = () => driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 200m, "ZAR", "sale-2", key));

        await act.Should().ThrowAsync<DuplicateLoyaltyTransactionException>();
    }

    [Fact]
    public async Task Earn_while_original_in_flight_is_refused()
    {
        var driver = new LoyaltyDriver();
        var key = Guid.NewGuid();
        driver.Seed(new LoyaltyTransaction(
            driver.TenantId, driver.CompanyId, driver.CustomerId, TransactionType.Earn,
            100m, "ZAR", key, "sale-1", DateTimeOffset.UtcNow));

        Func<Task> act = () => driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", key));

        await act.Should().ThrowAsync<LoyaltyRequestInProgressException>();
    }

    [Fact]
    public async Task Earn_when_orbit_unreachable_queues_without_touching_cache()
    {
        var driver = new LoyaltyDriver();
        driver.Orbit.MakeUnavailable();

        EarnOutcome outcome = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));

        outcome.Queued.Should().BeTrue();
        driver.Member.BalanceCache.Should().Be(0m);
        driver.Logged.Should().Be(1);
    }

    [Fact]
    public async Task Earn_marketing_bonus_without_consent_is_refused()
    {
        var driver = new LoyaltyDriver();
        driver.Consents.IsValidAsync(
                Arg.Any<Guid>(), Arg.Any<ConsentType>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        Func<Task> act = () => driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "campaign-1", Guid.NewGuid(),
                IsMarketingBonus: true));

        await act.Should().ThrowAsync<ConsentNotGivenException>();
        driver.Logged.Should().Be(0, "a refused earn writes nothing");
    }

    [Fact]
    public async Task Earn_applies_tier_multiplier()
    {
        var driver = new LoyaltyDriver();
        driver.Member.RecordSync(5000m, "gold", DateTimeOffset.UtcNow);
        driver.TierRepo.FindAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LoyaltyTier(
                driver.TenantId, driver.CompanyId, "gold", "Gold", "Gold",
                5000m, 1.5m, DateTimeOffset.UtcNow));

        EarnOutcome outcome = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", Guid.NewGuid()));

        outcome.Points.Should().Be(150m);
    }

    [Fact]
    public async Task Redeem_debits_orbit_and_updates_cache()
    {
        var driver = new LoyaltyDriver();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 500m, "ZAR", "sale-1", Guid.NewGuid()));

        RedeemOutcome outcome = await driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 200m, "reward-1", Guid.NewGuid()));

        outcome.Queued.Should().BeFalse();
        outcome.NewBalance.Should().Be(300m);
        driver.Member.BalanceCache.Should().Be(300m);
    }

    [Fact]
    public async Task Redeem_beyond_cache_is_refused_before_orbit()
    {
        var driver = new LoyaltyDriver();

        Func<Task> act = () => driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 200m, "reward-1", Guid.NewGuid()));

        await act.Should().ThrowAsync<InsufficientPointsException>();
        driver.Logged.Should().Be(0, "a refused redemption writes nothing");
    }

    [Fact]
    public async Task Redeem_refused_by_orbit_fails_terminally()
    {
        var driver = new LoyaltyDriver();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 500m, "ZAR", "sale-1", Guid.NewGuid()));

        // The ledger diverged below the cache (e.g. an adjustment in Orbit): the provisional
        // check passes, the authoritative debit refuses.
        driver.Orbit.SetLedger(driver.Member.OrbitMemberId, 100m);

        Func<Task> act = () => driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 200m, "reward-1", Guid.NewGuid()));

        await act.Should().ThrowAsync<InsufficientPointsException>();
    }
}
