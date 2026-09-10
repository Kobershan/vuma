using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;
using NSubstitute;

namespace VumaRetail.UnitTests.Loyalty.FailureModes;

/// <summary>
/// Orbit unreachable / retry behaviour: queue-and-retry, never silent drop.
/// Scaffolding failure-mode tests for Stage 20.
/// </summary>
public sealed class OrbitUnreachableEarnTests
{
    [Fact]
    public void Orbit_unreachable_during_earn_queues_for_retry()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.When(x => x.EarnAsync(Arg.Any<EarnRequest>()))
            .Do(_ => throw new TimeoutException("Orbit unreachable"));

        var handler = new LoyaltyEarnHandler(orbitClient);
        var result = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 150m });

        result.Status.Should().Be(TransactionStatus.QueuedForRetry);
        result.PointsEarned.Should().Be(0m);
    }
}

/// <summary>Retry eventually succeeds.</summary>
public sealed class RetrySuccessTests
{
    [Fact]
    public void Queued_earn_retry_succeeds_after_orbit_comes_back()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.EarnAsync(Arg.Any<EarnRequest>())
            .Returns(new OrbitEarnResponse { Success = true, PointsEarned = 150m, NewBalance = 150m });

        var handler = new LoyaltyEarnHandler(orbitClient);
        var result = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 150m });

        result.Status.Should().Be(TransactionStatus.Confirmed);
    }
}

/// <summary>Duplicate retry workers: only one Orbit call (idempotency).</summary>
public sealed class DuplicateRetryTests
{
    [Fact]
    public void Two_retry_workers_picking_same_queued_request_only_one_succeeds()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.EarnAsync(Arg.Any<EarnRequest>())
            .Returns(new OrbitEarnResponse { Success = true, PointsEarned = 100m, NewBalance = 100m });

        var handler = new LoyaltyEarnHandler(orbitClient);
        var key = Guid.NewGuid();
        handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 100m, IdempotencyKey = key });
        handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 100m, IdempotencyKey = key });

        orbitClient.Received(1).EarnAsync(Arg.Any<EarnRequest>());
    }
}
