using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Application.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Loyalty.Concurrency;

/// <summary>
/// Two simultaneous burns against a balance that can only satisfy one. The gate is Orbit's
/// ledger — Vuma's cache never decides alone.
/// </summary>
public sealed class SimultaneousBurnTests
{
    [Fact]
    public async Task Two_simultaneous_burns_of_60_against_balance_of_100_only_one_succeeds()
    {
        var driver = new LoyaltyDriver();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", Guid.NewGuid()));

        Task<RedeemOutcome> first = driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 60m, "reward-1", Guid.NewGuid()));
        Task<RedeemOutcome> second = driver.Redeems.HandleAsync(
            new RedeemPointsCommand(
                driver.CompanyId, driver.CustomerId, 60m, "reward-2", Guid.NewGuid()));

        int wins = 0;
        foreach (Task<RedeemOutcome> attempt in new[] { first, second })
        {
            try
            {
                await attempt;
                wins++;
            }
            catch (InsufficientPointsException)
            {
                // Expected for exactly one of the two.
            }
        }

        wins.Should().Be(1, "exactly one of the two concurrent burns must succeed");
    }

    [Fact]
    public async Task No_over_redemption_occurs_under_concurrent_requests()
    {
        var driver = new LoyaltyDriver();
        await driver.Earns.HandleAsync(
            new EarnPointsCommand(
                driver.CompanyId, driver.CustomerId, 100m, "ZAR", "sale-1", Guid.NewGuid()));

        var attempts = new List<Task>();
        for (int index = 0; index < 5; index++)
        {
            int captured = index;
            attempts.Add(Task.Run(async () =>
            {
                try
                {
                    await driver.Redeems.HandleAsync(
                        new RedeemPointsCommand(
                            driver.CompanyId, driver.CustomerId, 60m, $"reward-{captured}",
                            Guid.NewGuid()));
                }
                catch (InsufficientPointsException)
                {
                }
            }));
        }

        await Task.WhenAll(attempts);

        OrbitBalanceResult balance = await driver.Orbit.GetBalanceAsync(driver.Member.OrbitMemberId);
        balance.Balance.Should().BeGreaterThanOrEqualTo(0m);
        balance.Balance.Should().Be(40m, "exactly one 60-point burn applied to the 100-point ledger");
    }
}
