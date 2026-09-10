using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty;

/// <summary>
/// Upserts cached tiers and rewards from Orbit definitions (Stage 20). Extracted so the sync
/// commands and the catalogue pull share one write path rather than calling each other
/// (CONVENTIONS.md §4 — a handler does one thing).
/// </summary>
/// <param name="tiers">Tier persistence.</param>
/// <param name="rewards">Reward persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class LoyaltyCacheWriter(
    ILoyaltyTierRepository tiers,
    ILoyaltyRewardRepository rewards,
    ITenantContext tenant,
    IClock clock)
{
    /// <summary>Upserts cached tiers.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="definitions">Orbit's current definitions.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many rows were touched.</returns>
    public async Task<int> UpsertTiersAsync(
        Guid companyId, IReadOnlyList<TierDefinition> definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        DateTimeOffset now = clock.UtcNow;
        int touched = 0;

        foreach (TierDefinition definition in definitions)
        {
            LoyaltyTier? existing = await tiers
                .FindAsync(companyId, definition.TierId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                tiers.Add(new LoyaltyTier(
                    tenant.TenantId, companyId, definition.TierId, definition.Name,
                    definition.DisplayName, definition.ThresholdPoints, definition.Multiplier, now));
            }
            else
            {
                existing.Refresh(
                    definition.DisplayName, definition.ThresholdPoints, definition.Multiplier, now);
            }

            touched++;
        }

        return touched;
    }

    /// <summary>Upserts cached rewards.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="definitions">Orbit's current definitions.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many rows were touched.</returns>
    public async Task<int> UpsertRewardsAsync(
        Guid companyId, IReadOnlyList<RewardDefinition> definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        DateTimeOffset now = clock.UtcNow;
        int touched = 0;

        foreach (RewardDefinition definition in definitions)
        {
            LoyaltyReward? existing = await rewards
                .FindAsync(companyId, definition.RewardId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                rewards.Add(new LoyaltyReward(
                    tenant.TenantId, companyId, definition.RewardId, definition.Name,
                    definition.CostInPoints, definition.Description, definition.TierId,
                    null, definition.IsAvailable, now));
            }
            else
            {
                existing.RefreshAvailability(definition.IsAvailable, now);
            }

            touched++;
        }

        return touched;
    }
}
