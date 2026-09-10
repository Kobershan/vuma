using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;
using NSubstitute;

namespace VumaRetail.UnitTests.Loyalty.Concurrency;

/// <summary>
/// Two simultaneous burns against a balance that can only satisfy one.
/// Gate enforced by Orbit's ledger; Vuma defers to Orbit. Scaffolding for Stage 20.
/// </summary>
public sealed class SimultaneousBurnTests
{
    [Fact]
    public void Two_simultaneous_burns_of_60_against_balance_of_100_only_one_succeeds()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        int calls = 0;
        orbitClient.RedeemAsync(Arg.Any<RedeemRequest>()).Returns(_ =>
        {
            if (System.Threading.Interlocked.Increment(ref calls) == 1)
            {
                return new OrbitRedeemResponse { Success = true, PointsBurned = 60m, NewBalance = 40m };
            }

            return new OrbitRedeemResponse { Success = false, PointsBurned = 0m, NewBalance = 100m };
        });

        var handler = new LoyaltyRedeemHandler(orbitClient);
        var memberId = Guid.NewGuid();
        var first = handler.HandleRedeemAsync(new RedeemRequest { CustomerId = memberId, Points = 60m });
        var second = handler.HandleRedeemAsync(new RedeemRequest { CustomerId = memberId, Points = 60m });

        // Exactly one of the two burns succeeded on Orbit's ledger; the other was refused.
        (first.Success ^ second.Success).Should().BeTrue("exactly one of the two concurrent burns must succeed");
    }

    [Fact]
    public void No_over_redemption_occurs_under_concurrent_requests()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        int successfulRedemptions = 0;
        orbitClient.RedeemAsync(Arg.Any<RedeemRequest>()).Returns(_ =>
        {
            if (System.Threading.Interlocked.Increment(ref successfulRedemptions) == 1)
            {
                return new OrbitRedeemResponse { Success = true, PointsBurned = 60m, NewBalance = 40m };
            }

            System.Threading.Interlocked.Decrement(ref successfulRedemptions);
            return new OrbitRedeemResponse { Success = false, PointsBurned = 0m, NewBalance = 100m };
        });

        var handler = new LoyaltyRedeemHandler(orbitClient);
        var memberId = Guid.NewGuid();
        var first = handler.HandleRedeemAsync(new RedeemRequest { CustomerId = memberId, Points = 60m });
        var second = handler.HandleRedeemAsync(new RedeemRequest { CustomerId = memberId, Points = 60m });

        successfulRedemptions.Should().Be(1);
        (first.Success ^ second.Success).Should().BeTrue("exactly one of the two concurrent burns must succeed");
    }
}
