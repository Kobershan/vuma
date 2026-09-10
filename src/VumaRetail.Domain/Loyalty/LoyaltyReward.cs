using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// A reward cached from Orbit for catalogue rendering (Stage 20). Cost and availability are
/// verified against Orbit at redemption time — this row is display, not authority.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class LoyaltyReward : Entity
{
    private LoyaltyReward()
    {
    }

    /// <summary>Caches a reward from Orbit.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="rewardId">Orbit's reward identifier.</param>
    /// <param name="name">Reward name.</param>
    /// <param name="costInPoints">Cost in points, scale 4.</param>
    /// <param name="description">Reward description.</param>
    /// <param name="tierId">Required tier, if any.</param>
    /// <param name="imageUrl">Image URL, if any.</param>
    /// <param name="isAvailable">Cached availability.</param>
    /// <param name="syncedAt">When cached, UTC.</param>
    public LoyaltyReward(
        Guid tenantId,
        Guid companyId,
        string rewardId,
        string name,
        decimal costInPoints,
        string? description = null,
        string? tierId = null,
        string? imageUrl = null,
        bool isAvailable = true,
        DateTimeOffset syncedAt = default)
        : base(tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rewardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (costInPoints <= 0m)
        {
            throw new InvalidPointsException("A reward must cost a positive number of points.");
        }

        AssignCompany(companyId);
        RewardId = rewardId.Trim();
        Name = name.Trim();
        CostInPoints = decimal.Round(costInPoints, Money.Scale, Money.Rounding);
        Description = description;
        TierId = tierId;
        ImageUrl = imageUrl;
        IsAvailable = isAvailable;
        SyncedAt = syncedAt;
    }

    /// <summary>Orbit's reward identifier. Unique per company.</summary>
    public string RewardId { get; private set; } = string.Empty;

    /// <summary>Reward name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Reward description.</summary>
    public string? Description { get; private set; }

    /// <summary>Cost in points, scale 4.</summary>
    public decimal CostInPoints { get; private set; }

    /// <summary>Required tier, if any.</summary>
    public string? TierId { get; private set; }

    /// <summary>Image URL, if any.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Cached availability. Re-verified at redemption.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>When cached, UTC.</summary>
    public DateTimeOffset SyncedAt { get; private set; }

    /// <summary>Refreshes cached availability from Orbit.</summary>
    /// <param name="isAvailable">Current availability.</param>
    /// <param name="syncedAt">When, UTC.</param>
    public void RefreshAvailability(bool isAvailable, DateTimeOffset syncedAt)
    {
        IsAvailable = isAvailable;
        SyncedAt = syncedAt;
    }
}
