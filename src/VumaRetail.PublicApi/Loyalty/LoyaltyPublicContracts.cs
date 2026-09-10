namespace VumaRetail.PublicApi.Loyalty;

/// <summary>Public loyalty DTOs (Stage 20). ADR-021: this host's shapes are its own — cost,
/// margin, supplier and other-customer data are structurally unreachable, not filtered.</summary>
public static class LoyaltyPublicContracts
{
    /// <summary>Enrolls a customer as a loyalty member.</summary>
    /// <param name="CompanyId">The company to enroll in.</param>
    /// <param name="StoreId">The store of enrolment, if any.</param>
    public sealed record EnrollMemberRequest(Guid CompanyId, Guid? StoreId = null);

    /// <summary>Earns points for a member.</summary>
    /// <param name="CompanyId">The company earning in.</param>
    /// <param name="PurchaseAmount">The purchase amount in <paramref name="Currency"/>.</param>
    /// <param name="Currency">ISO 4217 code.</param>
    /// <param name="Reference">What caused it (e.g. sale id).</param>
    /// <param name="IdempotencyKey">Client-supplied UUID v7. Replays collapse onto one outcome.</param>
    /// <param name="IsMarketingBonus">True for campaign bonuses — gated on marketing consent.</param>
    public sealed record EarnRequest(
        Guid CompanyId,
        decimal PurchaseAmount,
        string Currency,
        string? Reference,
        Guid IdempotencyKey,
        bool IsMarketingBonus = false);

    /// <summary>Redeems points for a member.</summary>
    /// <param name="CompanyId">The company redeeming in.</param>
    /// <param name="Points">Points to redeem.</param>
    /// <param name="Reference">What caused it.</param>
    /// <param name="IdempotencyKey">Client-supplied UUID v7.</param>
    public sealed record RedeemRequest(
        Guid CompanyId,
        decimal Points,
        string? Reference,
        Guid IdempotencyKey);

    /// <summary>One member, as the public surface returns it.</summary>
    /// <param name="CustomerId">The member.</param>
    /// <param name="EnrolledAt">When enrolment happened, UTC.</param>
    /// <param name="TierId">Current tier, if known.</param>
    /// <param name="Balance">Cached balance at 2dp.</param>
    /// <param name="BalanceAsAt">When the cache was confirmed, UTC.</param>
    /// <param name="BalanceStale">True when the cache is older than five minutes.</param>
    public sealed record MemberResponse(
        Guid CustomerId,
        DateTimeOffset EnrolledAt,
        string? TierId,
        decimal Balance,
        DateTimeOffset BalanceAsAt,
        bool BalanceStale);

    /// <summary>What an earn produced.</summary>
    /// <param name="TransactionId">The Vuma transaction.</param>
    /// <param name="Points">Points requested.</param>
    /// <param name="NewBalance">Balance after applying, at 2dp.</param>
    /// <param name="TierId">Tier after applying, if reported.</param>
    /// <param name="Queued">True when the engine was unreachable: accepted, retrying.</param>
    public sealed record EarnResponse(
        Guid TransactionId, decimal Points, decimal NewBalance, string? TierId, bool Queued);

    /// <summary>What a redemption produced.</summary>
    /// <param name="TransactionId">The Vuma transaction.</param>
    /// <param name="Points">Points debited.</param>
    /// <param name="NewBalance">Balance after applying, at 2dp.</param>
    /// <param name="Queued">True when the engine was unreachable: accepted, retrying.</param>
    public sealed record RedeemResponse(Guid TransactionId, decimal Points, decimal NewBalance, bool Queued);

    /// <summary>A member's balance, honestly labelled.</summary>
    /// <param name="CustomerId">The member.</param>
    /// <param name="Balance">Cached balance at 2dp.</param>
    /// <param name="AsAt">When the cache was confirmed, UTC.</param>
    /// <param name="IsStale">True when older than five minutes — show "may be updated".</param>
    public sealed record BalanceResponse(Guid CustomerId, decimal Balance, DateTimeOffset AsAt, bool IsStale);

    /// <summary>One transaction, as the public surface returns it.</summary>
    /// <param name="Id">The transaction.</param>
    /// <param name="Type">Earn, burn, adjustment or expiry.</param>
    /// <param name="Points">Points at 2dp.</param>
    /// <param name="Status">Pending, confirmed, queued or failed.</param>
    /// <param name="Reference">What caused it.</param>
    /// <param name="OccurredAt">When it happened, UTC.</param>
    public sealed record TransactionResponse(
        Guid Id, string Type, decimal Points, string Status, string? Reference, DateTimeOffset OccurredAt);

    /// <summary>One tier, as the public surface returns it.</summary>
    /// <param name="TierId">Orbit's tier id.</param>
    /// <param name="Name">Tier name.</param>
    /// <param name="DisplayName">Customer-facing name.</param>
    /// <param name="ThresholdPoints">Points threshold.</param>
    /// <param name="Multiplier">Earn multiplier.</param>
    public sealed record TierResponse(
        string TierId, string Name, string DisplayName, decimal ThresholdPoints, decimal Multiplier);

    /// <summary>A member's tier and progress.</summary>
    /// <param name="CustomerId">The member.</param>
    /// <param name="TierId">Current tier, if known.</param>
    /// <param name="TierName">Current tier name, if known.</param>
    /// <param name="NextTierId">Next tier, if any.</param>
    /// <param name="PointsToNext">Points to the next tier, if known.</param>
    /// <param name="Balance">Cached balance at 2dp.</param>
    public sealed record MemberTierResponse(
        Guid CustomerId, string? TierId, string? TierName, string? NextTierId,
        decimal? PointsToNext, decimal Balance);

    /// <summary>One reward, as the public surface returns it.</summary>
    /// <param name="RewardId">Orbit's reward id.</param>
    /// <param name="Name">Reward name.</param>
    /// <param name="Description">Reward description.</param>
    /// <param name="CostInPoints">Cost in points.</param>
    /// <param name="TierId">Required tier, if any.</param>
    /// <param name="IsAvailable">Cached availability — re-verified at redemption.</param>
    public sealed record RewardResponse(
        string RewardId, string Name, string? Description, decimal CostInPoints,
        string? TierId, bool IsAvailable);

    /// <summary>What a catalogue sync pulled.</summary>
    /// <param name="Tiers">Tier rows touched.</param>
    /// <param name="Rewards">Reward rows touched.</param>
    public sealed record CatalogueSyncResponse(int Tiers, int Rewards);

    /// <summary>Pulls Orbit's catalogue into the local cache.</summary>
    /// <param name="CompanyId">The company.</param>
    public sealed record SyncCatalogueRequest(Guid CompanyId);

    /// <summary>Configures the loyalty programme for a company.</summary>
    /// <param name="CompanyId">The company.</param>
    /// <param name="Currency">ISO 4217 code earn amounts are quoted in.</param>
    /// <param name="EarnRate">Base points per currency unit.</param>
    /// <param name="PointExpiryDays">Days before points expire.</param>
    /// <param name="Enabled">Whether members can earn and burn.</param>
    public sealed record ConfigureLoyaltyRequest(
        Guid CompanyId,
        string Currency,
        decimal EarnRate,
        int PointExpiryDays,
        bool Enabled);

    /// <summary>An Orbit-originated webhook notification.</summary>
    /// <param name="CompanyId">The company.</param>
    /// <param name="OrbitMemberId">Orbit's member id.</param>
    /// <param name="Balance">Reported balance, if carried.</param>
    /// <param name="TierId">Reported tier, if carried.</param>
    /// <param name="EventType">Orbit's event name.</param>
    public sealed record WebhookNotificationRequest(
        Guid CompanyId,
        string OrbitMemberId,
        decimal? Balance,
        string? TierId,
        string EventType);
}
