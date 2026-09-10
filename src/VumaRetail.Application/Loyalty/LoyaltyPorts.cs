using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty;

/// <summary>Persists loyalty members (Stage 20).</summary>
public interface ILoyaltyMemberRepository
{
    /// <summary>Finds a member by customer in the ambient tenant.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyMember?> FindByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Finds a member by Orbit id in the ambient tenant (webhook path).</summary>
    /// <param name="orbitMemberId">Orbit's member id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyMember?> FindByOrbitIdAsync(string orbitMemberId, CancellationToken cancellationToken = default);

    /// <summary>Stages a member for insert. The pipeline commits.</summary>
    /// <param name="member">The member.</param>
    void Add(LoyaltyMember member);
}

/// <summary>Persists the Vuma-side loyalty transaction log (Stage 20).</summary>
public interface ILoyaltyTransactionRepository
{
    /// <summary>Finds a transaction by id.</summary>
    /// <param name="transactionId">The transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Finds a transaction by its idempotency key in a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="idempotencyKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyTransaction?> FindByKeyAsync(Guid companyId, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Lists a member's transactions, newest first, capped.</summary>
    /// <param name="customerId">The member.</param>
    /// <param name="limit">Maximum rows.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<LoyaltyTransaction>> ListForMemberAsync(Guid customerId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Lists queued transactions due for retry.</summary>
    /// <param name="notBefore">Only rows queued at or before this instant.</param>
    /// <param name="limit">Maximum rows.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<LoyaltyTransaction>> ListQueuedAsync(DateTimeOffset notBefore, int limit, CancellationToken cancellationToken = default);

    /// <summary>Sums confirmed earn/burn amounts for a member (reconciliation input).</summary>
    /// <param name="customerId">The member.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<LoyaltyTransaction>> ListConfirmedAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Stages a transaction for insert. The pipeline commits.</summary>
    /// <param name="transaction">The transaction.</param>
    void Add(LoyaltyTransaction transaction);
}

/// <summary>Persists cached tiers (Stage 20).</summary>
public interface ILoyaltyTierRepository
{
    /// <summary>Finds a cached tier by Orbit tier id in a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tierId">Orbit's tier id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyTier?> FindAsync(Guid companyId, string tierId, CancellationToken cancellationToken = default);

    /// <summary>Lists cached tiers for a company, threshold ascending.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<LoyaltyTier>> ListAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Stages a tier for insert. The pipeline commits.</summary>
    /// <param name="tier">The tier.</param>
    void Add(LoyaltyTier tier);
}

/// <summary>Persists cached rewards (Stage 20).</summary>
public interface ILoyaltyRewardRepository
{
    /// <summary>Finds a cached reward by Orbit reward id in a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rewardId">Orbit's reward id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltyReward?> FindAsync(Guid companyId, string rewardId, CancellationToken cancellationToken = default);

    /// <summary>Lists cached rewards for a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tierId">Restricts to a tier, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<LoyaltyReward>> ListAsync(Guid companyId, string? tierId, CancellationToken cancellationToken = default);

    /// <summary>Stages a reward for insert. The pipeline commits.</summary>
    /// <param name="reward">The reward.</param>
    void Add(LoyaltyReward reward);
}

/// <summary>Persists per-company loyalty settings (Stage 20).</summary>
public interface ILoyaltySettingsRepository
{
    /// <summary>Reads a company's settings, if configured.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LoyaltySettings?> FindAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Stages settings for insert. The pipeline commits.</summary>
    /// <param name="settings">The settings.</param>
    void Add(LoyaltySettings settings);
}

/// <summary>An earn request to Orbit (Stage 20).</summary>
/// <param name="OrbitMemberId">Orbit's member id.</param>
/// <param name="Points">Points to credit, scale 4.</param>
/// <param name="Currency">ISO 4217 code of the purchase.</param>
/// <param name="Reference">What caused it (e.g. sale id).</param>
/// <param name="IdempotencyKey">The at-most-once key.</param>
public sealed record OrbitEarnRequest(
    string OrbitMemberId, decimal Points, string Currency, string? Reference, Guid IdempotencyKey);

/// <summary>Orbit's answer to an earn (Stage 20).</summary>
/// <param name="Success">Whether Orbit applied it.</param>
/// <param name="OrbitTransactionId">Orbits transaction id, when applied.</param>
/// <param name="NewBalance">The ledger balance after applying.</param>
/// <param name="TierId">The member's tier after applying, if reported.</param>
public sealed record OrbitEarnResult(bool Success, string OrbitTransactionId, decimal NewBalance, string? TierId);

/// <summary>A redemption request to Orbit (Stage 20).</summary>
/// <param name="OrbitMemberId">Orbit's member id.</param>
/// <param name="Points">Points to debit, scale 4.</param>
/// <param name="Reference">What caused it.</param>
/// <param name="IdempotencyKey">The at-most-once key.</param>
public sealed record OrbitRedeemRequest(
    string OrbitMemberId, decimal Points, string? Reference, Guid IdempotencyKey);

/// <summary>Orbit's answer to a redemption (Stage 20).</summary>
/// <param name="Applied">Whether the ledger was debited.</param>
/// <param name="RefusedInsufficient">True when refused for insufficient ledger balance.</param>
/// <param name="OrbitTransactionId">Orbits transaction id, when applied.</param>
/// <param name="NewBalance">The ledger balance after applying.</param>
/// <param name="TierId">The member's tier after applying, if reported.</param>
public sealed record OrbitRedeemResult(
    bool Applied, bool RefusedInsufficient, string OrbitTransactionId, decimal NewBalance, string? TierId);

/// <summary>Orbit's reported balance (Stage 20).</summary>
/// <param name="Balance">Ledger balance.</param>
/// <param name="TierId">Current tier, if reported.</param>
public sealed record OrbitBalanceResult(decimal Balance, string? TierId);

/// <summary>One tier definition from Orbit.</summary>
/// <param name="TierId">Orbit's tier id.</param>
/// <param name="Name">Tier name.</param>
/// <param name="DisplayName">Customer-facing name.</param>
/// <param name="ThresholdPoints">Points threshold.</param>
/// <param name="Multiplier">Earn multiplier.</param>
public sealed record TierDefinition(
    string TierId, string Name, string DisplayName, decimal ThresholdPoints, decimal Multiplier);

/// <summary>One reward definition from Orbit.</summary>
/// <param name="RewardId">Orbit's reward id.</param>
/// <param name="Name">Reward name.</param>
/// <param name="CostInPoints">Cost in points.</param>
/// <param name="Description">Reward description.</param>
/// <param name="TierId">Required tier, if any.</param>
/// <param name="IsAvailable">Current availability.</param>
public sealed record RewardDefinition(
    string RewardId, string Name, decimal CostInPoints, string? Description, string? TierId, bool IsAvailable);

/// <summary>Orbit is unreachable or unusable right now (Stage 20).</summary>
/// <remarks>
/// An infrastructure failure, not a domain refusal: handlers catch this and queue for retry
/// (the till never blocks on the loyalty engine). Anything else propagates.
/// </remarks>
public sealed class OrbitUnavailableException(string detail) : Exception(detail);

/// <summary>
/// The loyalty engine boundary (Stage 20). Vuma owns routing, idempotency and error handling;
/// Orbit owns the ledger, tiers and fulfilment. The default registration is the in-memory fake;
/// the HTTP client is config-gated for a real Proxima endpoint.
/// </summary>
public interface IOrbitClient
{
    /// <summary>Ensures an Orbit member exists, creating one idempotently.</summary>
    /// <param name="customerId">The Vuma customer, for a stable mapping.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<string> EnsureMemberAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Credits points.</summary>
    /// <param name="request">The earn.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<OrbitEarnResult> EarnAsync(OrbitEarnRequest request, CancellationToken cancellationToken = default);

    /// <summary>Debits points. The ledger is the concurrency gate for competing burns.</summary>
    /// <param name="request">The redemption.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<OrbitRedeemResult> RedeemAsync(OrbitRedeemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the live ledger balance.</summary>
    /// <param name="orbitMemberId">Orbit's member id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<OrbitBalanceResult> GetBalanceAsync(string orbitMemberId, CancellationToken cancellationToken = default);

    /// <summary>Lists Orbit's tier definitions.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<IReadOnlyList<TierDefinition>> ListTiersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists Orbit's reward definitions.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OrbitUnavailableException">Orbit cannot be reached.</exception>
    Task<IReadOnlyList<RewardDefinition>> ListRewardsAsync(CancellationToken cancellationToken = default);
}
