using System.Collections.Concurrent;
using VumaRetail.Application.Loyalty;

namespace VumaRetail.Infrastructure.Loyalty;

/// <summary>
/// The in-memory loyalty engine: a thread-safe atomic ledger standing in for Proxima Orbit
/// (Stage 20). The default registration — deterministic, idempotent, and the concurrency gate
/// the burn tests prove against. Real Orbit credentials are deferred (PROGRESS.md); when they
/// exist, <see cref="HttpOrbitClient"/> takes this registration slot.
/// </summary>
/// <remarks>
/// Idempotency is on the key, exactly like Orbit's own contract: the same key returns the
/// original result without re-applying, and the ledger debit under a per-member lock is what
/// serialises competing burns — never Vuma's cache.
/// </remarks>
public sealed class InMemoryOrbitClient : IOrbitClient
{
    private readonly ConcurrentDictionary<Guid, string> _members = new();
    private readonly ConcurrentDictionary<string, Ledger> _ledgers = new();
    private readonly ConcurrentDictionary<Guid, object> _results = new();
    private volatile bool _available = true;

    /// <summary>Takes the fake down. The next call throws <see cref="OrbitUnavailableException"/>.</summary>
    public void MakeUnavailable() => _available = false;

    /// <summary>Brings the fake back.</summary>
    public void MakeAvailable() => _available = true;

    /// <summary>Sets a member's ledger balance and tier directly. Tests only.</summary>
    /// <param name="orbitMemberId">Orbit's member id.</param>
    /// <param name="balance">The balance to set.</param>
    /// <param name="tierId">The tier to set.</param>
    public void SetLedger(string orbitMemberId, decimal balance, string? tierId = null)
    {
        Ledger ledger = _ledgers.GetOrAdd(orbitMemberId, _ => new Ledger());
        lock (ledger)
        {
            ledger.Balance = balance;
            ledger.TierId = tierId;
        }
    }

    /// <inheritdoc />
    public Task<string> EnsureMemberAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        string orbitId = _members.GetOrAdd(customerId, static customer => $"orbit-{customer:N}");
        _ledgers.GetOrAdd(orbitId, _ => new Ledger());
        return Task.FromResult(orbitId);
    }

    /// <inheritdoc />
    public Task<OrbitEarnResult> EarnAsync(OrbitEarnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDown();

        if (_results.TryGetValue(request.IdempotencyKey, out object? cached)
            && cached is OrbitEarnResult earn)
        {
            return Task.FromResult(earn);
        }

        Ledger ledger = _ledgers.GetOrAdd(request.OrbitMemberId, _ => new Ledger());
        OrbitEarnResult result;
        lock (ledger)
        {
            ledger.Balance += request.Points;
            result = new OrbitEarnResult(
                true, $"orbit-tx-{request.IdempotencyKey:N}", ledger.Balance, ledger.TierId);
        }

        _results.TryAdd(request.IdempotencyKey, result);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<OrbitRedeemResult> RedeemAsync(OrbitRedeemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDown();

        if (_results.TryGetValue(request.IdempotencyKey, out object? cached)
            && cached is OrbitRedeemResult redeem)
        {
            return Task.FromResult(redeem);
        }

        Ledger ledger = _ledgers.GetOrAdd(request.OrbitMemberId, _ => new Ledger());
        OrbitRedeemResult result;
        lock (ledger)
        {
            if (ledger.Balance < request.Points)
            {
                result = new OrbitRedeemResult(false, true, string.Empty, ledger.Balance, ledger.TierId);
            }
            else
            {
                ledger.Balance -= request.Points;
                result = new OrbitRedeemResult(
                    true, false, $"orbit-tx-{request.IdempotencyKey:N}", ledger.Balance, ledger.TierId);
            }
        }

        _results.TryAdd(request.IdempotencyKey, result);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<OrbitBalanceResult> GetBalanceAsync(string orbitMemberId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        Ledger ledger = _ledgers.GetOrAdd(orbitMemberId, _ => new Ledger());
        lock (ledger)
        {
            return Task.FromResult(new OrbitBalanceResult(ledger.Balance, ledger.TierId));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TierDefinition>> ListTiersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<TierDefinition>>(
        [
            new("bronze", "Bronze", "Bronze", 0m, 1m),
            new("silver", "Silver", "Silver", 1000m, 1.25m),
            new("gold", "Gold", "Gold", 5000m, 1.5m),
        ]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RewardDefinition>> ListRewardsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<RewardDefinition>>(
        [
            new("reward-5off", "R50 off", 500m, "R50 off your next shop.", "silver", true),
            new("reward-freecoffee", "Free coffee", 150m, "A free coffee at the in-store café.", null, true),
        ]);
    }

    private void ThrowIfDown()
    {
        if (!_available)
        {
            throw new OrbitUnavailableException("The loyalty engine is unreachable.");
        }
    }

    private sealed class Ledger
    {
        public decimal Balance;
        public string? TierId;
    }
}
