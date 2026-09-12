using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// Vuma's view of a loyalty member (Stage 20). Identity lives in Stage 06, the points ledger in
/// Orbit — this row is the join: who the member is here, who they are there, and a cached
/// balance that is explicitly labelled with its age and never authoritative.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class LoyaltyMember : Entity
{
    private LoyaltyMember()
    {
    }

    /// <summary>Enrolls a customer.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company. Loyalty is per company.</param>
    /// <param name="customerId">The Stage 06 identity.</param>
    /// <param name="orbitMemberId">Orbit's member identifier.</param>
    /// <param name="enrolledAt">When, UTC. From <c>IClock</c>.</param>
    /// <param name="storeId">The store of enrolment, if any.</param>
    public LoyaltyMember(
        Guid tenantId,
        Guid companyId,
        Guid customerId,
        string orbitMemberId,
        DateTimeOffset enrolledAt,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orbitMemberId);
        AssignCompany(companyId);
        CustomerId = customerId;
        OrbitMemberId = orbitMemberId.Trim();
        EnrolledAt = enrolledAt;
        BalanceCache = 0m;
        BalanceCacheAsAt = enrolledAt;
    }

    /// <summary>The Stage 06 identity. Unique per company (one enrolment each).</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Orbit's member identifier.</summary>
    public string OrbitMemberId { get; private set; } = string.Empty;

    /// <summary>When enrolment happened, UTC.</summary>
    public DateTimeOffset EnrolledAt { get; private set; }

    /// <summary>Cached tier id from Orbit. Null until the first sync.</summary>
    public string? TierId { get; private set; }

    /// <summary>Cached point balance. Display only — burns validate against Orbit live.</summary>
    public decimal BalanceCache { get; private set; }

    /// <summary>When the cache was last confirmed against Orbit, UTC.</summary>
    public DateTimeOffset BalanceCacheAsAt { get; private set; }

    /// <summary>Last successful Orbit sync, UTC.</summary>
    public DateTimeOffset? LastSyncAt { get; private set; }

    /// <summary>Last provider webhook identity and monotonic version accepted for this member.</summary>
    public string? LastWebhookEventId { get; private set; }
    /// <summary>Last provider event version accepted for this member.</summary>
    public long? LastWebhookVersion { get; private set; }

    /// <summary>Records a confirmed sync from Orbit.</summary>
    /// <param name="balance">Orbit's reported balance.</param>
    /// <param name="tierId">Orbit's reported tier, if any.</param>
    /// <param name="syncedAt">When, UTC. From <c>IClock</c>.</param>
    public void RecordSync(decimal balance, string? tierId, DateTimeOffset syncedAt)
    {
        BalanceCache = balance;
        BalanceCacheAsAt = syncedAt;
        TierId = tierId;
        LastSyncAt = syncedAt;
    }

    /// <summary>Applies a provider event only once and never moves its version backwards.</summary>
    public bool RecordWebhook(string eventId, long? version, decimal balance, string? tierId, DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (string.Equals(LastWebhookEventId, eventId, StringComparison.Ordinal)
            || (version is { } incoming && LastWebhookVersion is { } current && incoming <= current))
        {
            return false;
        }

        RecordSync(balance, tierId, receivedAt);
        LastWebhookEventId = eventId.Trim();
        LastWebhookVersion = version;
        return true;
    }
}
