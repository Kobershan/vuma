using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;
using NSubstitute;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Full earn flow: Vuma API → mocked Orbit → balance update. Scaffolding integration tests for Stage 20.
/// </summary>
public sealed class FullEarnFlowIntegrationTests
{
    [Fact]
    public void Earn_creates_transaction_calls_orbit_and_updates_balance()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.EarnAsync(Arg.Any<EarnRequest>()).Returns(new OrbitEarnResponse
        {
            Success = true, PointsEarned = 150m, NewBalance = 150m
        });

        var handler = new LoyaltyEarnHandler(orbitClient);
        var result = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 150m });

        result.Status.Should().Be(TransactionStatus.Confirmed);
        result.PointsEarned.Should().Be(150m);
        result.Balance.Should().Be(150m);
        orbitClient.Received(1).EarnAsync(Arg.Any<EarnRequest>());
    }

    [Fact]
    public void Earn_when_orbit_unreachable_queues_for_retry()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.When(x => x.EarnAsync(Arg.Any<EarnRequest>()))
            .Do(x => throw new TimeoutException("Orbit unreachable"));

        var handler = new LoyaltyEarnHandler(orbitClient);
        var result = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 150m });

        result.Status.Should().Be(TransactionStatus.QueuedForRetry);
        result.PointsEarned.Should().Be(0m);
        result.Balance.Should().Be(0m);
    }

    [Fact]
    public void Earn_with_duplicate_idempotency_key_calls_orbit_once()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.EarnAsync(Arg.Any<EarnRequest>())
            .Returns(new OrbitEarnResponse { Success = true, PointsEarned = 100m, NewBalance = 100m, TransactionId = "orbit-txn-1" });

        var handler = new LoyaltyEarnHandler(orbitClient);
        var key = Guid.NewGuid();
        var first = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 100m, IdempotencyKey = key });
        var second = handler.HandleEarnAsync(new EarnRequest { CustomerId = Guid.NewGuid(), Amount = 100m, IdempotencyKey = key });

        first.TransactionId.Should().Be("orbit-txn-1");
        second.TransactionId.Should().Be("orbit-txn-1");
        orbitClient.Received(1).EarnAsync(Arg.Any<EarnRequest>());
    }
}

/// <summary>
/// Full redeem flow: Orbit ledger debit → reward fulfilment. Scaffolding for Stage 20.
/// </summary>
public sealed class FullRedeemFlowIntegrationTests
{
    [Fact]
    public void Redeem_debits_orbit_and_updates_balance()
    {
        var orbitClient = Substitute.For<IOrbitClient>();
        orbitClient.RedeemAsync(Arg.Any<RedeemRequest>()).Returns(new OrbitRedeemResponse
        {
            Success = true, PointsBurned = 200m, NewBalance = 800m
        });

        var handler = new LoyaltyRedeemHandler(orbitClient);
        var result = handler.HandleRedeemAsync(new RedeemRequest { CustomerId = Guid.NewGuid(), Points = 200m });

        result.Status.Should().Be(TransactionStatus.Confirmed);
        result.PointsBurned.Should().Be(200m);
        result.NewBalance.Should().Be(800m);
    }
}

/// <summary>
/// Idempotency: same earn twice → single effect. Scaffolding for Stage 20.
/// </summary>
public sealed class IdempotencyIntegrationTests
{
    [Fact]
    public void Submit_same_earn_twice_with_same_key_asserts_single_effect()
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

// Scaffolding types — real implementations live in Stage 20.
public interface IOrbitClient
{
    System.Threading.Tasks.Task<OrbitEarnResponse> EarnAsync(EarnRequest request);
    System.Threading.Tasks.Task<OrbitRedeemResponse> RedeemAsync(RedeemRequest request);
    System.Threading.Tasks.Task<OrbitBalanceResponse> GetBalanceAsync(Guid memberId);
}

public sealed class EarnRequest { public Guid CustomerId { get; init; } public decimal Amount { get; init; } public Guid IdempotencyKey { get; init; } = Guid.NewGuid(); }
public sealed class RedeemRequest { public Guid CustomerId { get; init; } public decimal Points { get; init; } public Guid IdempotencyKey { get; init; } = Guid.NewGuid(); }
public sealed class OrbitEarnResponse { public bool Success { get; init; } public decimal PointsEarned { get; init; } public decimal NewBalance { get; init; } public string? TransactionId { get; init; } }
public sealed class OrbitRedeemResponse { public bool Success { get; init; } public decimal PointsBurned { get; init; } public decimal NewBalance { get; init; } }
public sealed class OrbitBalanceResponse { public decimal Balance { get; init; } }

public sealed class LoyaltyEarnHandler
{
    private readonly IOrbitClient _orbitClient;
    private readonly Dictionary<Guid, EarnResult> _seen = new();
    public LoyaltyEarnHandler(IOrbitClient orbitClient) => _orbitClient = orbitClient;

    public EarnResult HandleEarnAsync(EarnRequest request)
    {
        // Vuma-side idempotency: the LoyaltyTransaction table (here an in-memory stand-in)
        // is the single source of truth for "was this request already handled".
        if (_seen.TryGetValue(request.IdempotencyKey, out EarnResult? cached))
        {
            return cached;
        }

        try
        {
            var response = _orbitClient.EarnAsync(request).Result;
            var result = new EarnResult
            {
                Status = TransactionStatus.Confirmed,
                PointsEarned = response.PointsEarned,
                Balance = response.NewBalance,
                TransactionId = response.TransactionId
            };
            _seen[request.IdempotencyKey] = result;
            return result;
        }
        catch
        {
            return new EarnResult { Status = TransactionStatus.QueuedForRetry, PointsEarned = 0m, Balance = 0m };
        }
    }
}

public sealed class LoyaltyRedeemHandler
{
    private readonly IOrbitClient _orbitClient;
    public LoyaltyRedeemHandler(IOrbitClient orbitClient) => _orbitClient = orbitClient;

    public RedeemResult HandleRedeemAsync(RedeemRequest request)
    {
        var response = _orbitClient.RedeemAsync(request).Result;
        return new RedeemResult
        {
            Status = TransactionStatus.Confirmed,
            Success = response.Success,
            PointsBurned = response.PointsBurned,
            NewBalance = response.NewBalance
        };
    }
}

public sealed class EarnResult
{
    public TransactionStatus Status { get; init; }
    public decimal PointsEarned { get; init; }
    public decimal Balance { get; init; }
    public string? TransactionId { get; init; }
}

public sealed class RedeemResult
{
    public TransactionStatus Status { get; init; }
    public bool Success { get; init; }
    public decimal PointsBurned { get; init; }
    public decimal NewBalance { get; init; }
}
