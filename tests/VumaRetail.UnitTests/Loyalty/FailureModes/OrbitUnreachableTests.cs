using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Application.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty.FailureModes;

/// <summary>
/// Orbit unreachable / retry behaviour: queue-and-retry, never silent drop, never double-apply.
/// </summary>
public sealed class OrbitUnreachableEarnTests
{
    [Fact]
    public async Task Orbit_unreachable_during_earn_queues_for_retry()
    {
        var driver = new LoyaltyDriver();
        driver.Orbit.MakeUnavailable();

        EarnOutcome outcome = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));

        outcome.Queued.Should().BeTrue();
        driver.Member.BalanceCache.Should().Be(0m, "nothing is credited until Orbit confirms");
    }

    [Fact]
    public async Task Orbit_unreachable_during_burn_queues_without_deducting_cache()
    {
        var driver = new LoyaltyDriver();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 500m, "ZAR", "sale-1", Guid.NewGuid()));
        driver.Orbit.MakeUnavailable();

        RedeemOutcome outcome = await driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 200m, "reward-1", Guid.NewGuid()));

        outcome.Queued.Should().BeTrue();
        driver.Member.BalanceCache.Should().Be(500m, "the provisional check never deducts");
    }
}

/// <summary>Retry eventually succeeds, exactly once.</summary>
public sealed class RetrySuccessTests
{
    [Fact]
    public async Task Queued_earn_retry_succeeds_after_orbit_comes_back()
    {
        var driver = new LoyaltyDriver();
        driver.Orbit.MakeUnavailable();
        EarnOutcome queued = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));
        queued.Queued.Should().BeTrue();

        driver.Orbit.MakeAvailable();
        RetryDisposition disposition = await driver.Retries.HandleAsync(
            new RetryLoyaltyTransactionCommand(queued.TransactionId));

        disposition.Should().Be(RetryDisposition.Confirmed);
        driver.Member.BalanceCache.Should().Be(150m);

        OrbitBalanceResult balance = await driver.Orbit.GetBalanceAsync(driver.Member.OrbitMemberId);
        balance.Balance.Should().Be(150m, "the earn applied exactly once across outage and retry");
    }

    [Fact]
    public async Task Retry_after_24_hours_fails_terminally()
    {
        var driver = new LoyaltyDriver();
        driver.Orbit.MakeUnavailable();
        EarnOutcome queued = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));

        driver.Advance(TimeSpan.FromHours(25));
        RetryDisposition disposition = await driver.Retries.HandleAsync(
            new RetryLoyaltyTransactionCommand(queued.TransactionId));

        disposition.Should().Be(RetryDisposition.Expired);
    }
}

/// <summary>Duplicate retry workers: only one Orbit application (idempotency).</summary>
public sealed class DuplicateRetryTests
{
    [Fact]
    public async Task Two_retry_workers_picking_same_queued_request_apply_once()
    {
        var driver = new LoyaltyDriver();
        driver.Orbit.MakeUnavailable();
        EarnOutcome queued = await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", Guid.NewGuid()));
        driver.Orbit.MakeAvailable();

        RetryDisposition[] outcomes = await Task.WhenAll(
            driver.Retries.HandleAsync(new RetryLoyaltyTransactionCommand(queued.TransactionId)),
            driver.Retries.HandleAsync(new RetryLoyaltyTransactionCommand(queued.TransactionId)));

        // The second worker either confirms an already-confirmed row or still-queues; what must
        // never happen is a double credit.
        outcomes.Should().Contain(RetryDisposition.Confirmed);
        OrbitBalanceResult balance = await driver.Orbit.GetBalanceAsync(driver.Member.OrbitMemberId);
        balance.Balance.Should().Be(100m, "the same idempotency key credits exactly once");
    }
}
