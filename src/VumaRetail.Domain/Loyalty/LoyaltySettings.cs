using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// Per-company loyalty configuration (Stage 20). Earn rate and expiry are Vuma's; tier
/// thresholds may be cached from Orbit on <c>LoyaltyTier</c>.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class LoyaltySettings : Entity
{
    private LoyaltySettings()
    {
    }

    /// <summary>Creates default settings for a company. Loyalty starts disabled.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company. One row per company.</param>
    /// <param name="currency">ISO 4217 code earn amounts are quoted in.</param>
    public LoyaltySettings(Guid tenantId, Guid companyId, string currency)
        : base(tenantId)
    {
        AssignCompany(companyId);
        Currency = currency;
        IsEnabled = false;
        EarnRate = 1m;
        PointExpiryDays = 365;
    }

    /// <summary>ISO 4217 code earn amounts are quoted in.</summary>
    public string Currency { get; private set; } = "ZAR";

    /// <summary>Whether members can earn and burn in this company.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Base points per one currency unit (e.g. 1 point per ZAR).</summary>
    public decimal EarnRate { get; private set; }

    /// <summary>Days after earning before points expire.</summary>
    public int PointExpiryDays { get; private set; }

    /// <summary>Enables the programme with a rate and expiry.</summary>
    /// <param name="earnRate">Base points per currency unit. Must be positive.</param>
    /// <param name="pointExpiryDays">Days before points expire. Must be positive.</param>
    /// <exception cref="InvalidPointsException">Non-positive rate or expiry.</exception>
    public void Enable(decimal earnRate, int pointExpiryDays)
    {
        if (earnRate <= 0m)
        {
            throw new InvalidPointsException("The earn rate must be positive.");
        }

        if (pointExpiryDays <= 0)
        {
            throw new InvalidPointsException("Point expiry days must be positive.");
        }

        IsEnabled = true;
        EarnRate = earnRate;
        PointExpiryDays = pointExpiryDays;
    }

    /// <summary>Disables earning and burning. Reads keep serving from cache.</summary>
    public void Disable() => IsEnabled = false;
}
