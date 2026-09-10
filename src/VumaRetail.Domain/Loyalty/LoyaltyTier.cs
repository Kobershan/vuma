using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// A tier cached from Orbit for fast rendering (Stage 20). Orbit evaluates tiers; Vuma caches
/// the result. Refreshed by sync and webhooks, never computed locally.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class LoyaltyTier : Entity
{
    private LoyaltyTier()
    {
    }

    /// <summary>Caches a tier definition from Orbit.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="tierId">Orbit's tier identifier.</param>
    /// <param name="name">Tier name.</param>
    /// <param name="displayName">Customer-facing name.</param>
    /// <param name="thresholdPoints">Points at which the tier is reached.</param>
    /// <param name="multiplier">Earn multiplier for members in this tier (1 = none).</param>
    /// <param name="syncedAt">When cached, UTC.</param>
    public LoyaltyTier(
        Guid tenantId,
        Guid companyId,
        string tierId,
        string name,
        string displayName,
        decimal thresholdPoints,
        decimal multiplier,
        DateTimeOffset syncedAt)
        : base(tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        AssignCompany(companyId);
        TierId = tierId.Trim();
        Name = name.Trim();
        DisplayName = displayName;
        ThresholdPoints = thresholdPoints;
        Multiplier = multiplier <= 0m ? 1m : multiplier;
        SyncedAt = syncedAt;
    }

    /// <summary>Orbit's tier identifier. Unique per company.</summary>
    public string TierId { get; private set; } = string.Empty;

    /// <summary>Tier name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Customer-facing name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Points at which the tier is reached.</summary>
    public decimal ThresholdPoints { get; private set; }

    /// <summary>Earn multiplier for members in this tier.</summary>
    public decimal Multiplier { get; private set; }

    /// <summary>When cached, UTC.</summary>
    public DateTimeOffset SyncedAt { get; private set; }

    /// <summary>Refreshes the cached definition from Orbit.</summary>
    /// <param name="displayName">Customer-facing name.</param>
    /// <param name="thresholdPoints">Points threshold.</param>
    /// <param name="multiplier">Earn multiplier.</param>
    /// <param name="syncedAt">When, UTC.</param>
    public void Refresh(string displayName, decimal thresholdPoints, decimal multiplier, DateTimeOffset syncedAt)
    {
        DisplayName = displayName;
        ThresholdPoints = thresholdPoints;
        Multiplier = multiplier <= 0m ? 1m : multiplier;
        SyncedAt = syncedAt;
    }
}
