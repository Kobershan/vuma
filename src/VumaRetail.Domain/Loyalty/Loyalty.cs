#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>Consent record per (ConsentType, CustomerId).</summary>
public sealed class Consent : Entity
{
    private Consent() { }
    /// <summary>Creates.</summary>
    public Consent(Guid tenantId, Guid customerId, ConsentType type, DateTimeOffset? expiresAt = null) : base(tenantId)
    {
        CustomerId = customerId; Type = type; State = ConsentState.NotAsked; ExpiresAt = expiresAt;
    }
    /// <summary>Customer.</summary>
    public Guid CustomerId { get; }
    /// <summary>Type.</summary>
    public ConsentType Type { get; }
    /// <summary>State.</summary>
    public ConsentState State { get; private set; }
    /// <summary>Granted at.</summary>
    public DateTimeOffset? GrantedAt { get; private set; }
    /// <summary>Withdrawn at.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }
    /// <summary>Expires at.</summary>
    public DateTimeOffset? ExpiresAt { get; }
    /// <summary>Gives consent.</summary>
    public void Give()
    {
        if (State == ConsentState.Given)
        {
            throw new DuplicateConsentException();
        }

        State = ConsentState.Given; GrantedAt = DateTimeOffset.UtcNow;
    }
    /// <summary>Withdraws.</summary>
    public void Withdraw()
    {
        State = ConsentState.Withdrawn;
        WithdrawnAt = DateTimeOffset.UtcNow;
    }
    /// <summary>Valid now.</summary>
    public bool IsConsentValid() => State == ConsentState.Given && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}

/// <summary>Consent type.</summary>
public enum ConsentType { MarketingEmail, MarketingSms, MarketingPush, DataProcessing, ThirdPartySharing, Profiling }
/// <summary>Consent state.</summary>
public enum ConsentState { Given, Withdrawn, Expired, NotAsked }

/// <summary>Duplicate consent.</summary>
public sealed class DuplicateConsentException : DomainException
{
    /// <summary>Creates.</summary>
    public DuplicateConsentException() : base("CONSENT_DUPLICATE", "A consent record already exists for this customer and type.") { }
}

/// <summary>Insufficient points.</summary>
public sealed class InsufficientPointsException : DomainException
{
    /// <summary>Creates.</summary>
    public InsufficientPointsException() : base("INSUFFICIENT_POINTS", "The member does not have sufficient points.") { }
}

/// <summary>Invalid points.</summary>
public sealed class InvalidPointsException : DomainException
{
    /// <summary>Creates.</summary>
    public InvalidPointsException() : base("INVALID_POINTS", "The points amount is invalid.") { }
}

/// <summary>Loyalty member view.</summary>
public sealed class LoyaltyMember : Entity
{
    private LoyaltyMember() { }
    /// <summary>Creates.</summary>
    public LoyaltyMember(Guid tenantId, Guid customerId) : base(tenantId) { CustomerId = customerId; EnrolledAt = DateTimeOffset.UtcNow; }
    /// <summary>Customer.</summary>
    public Guid CustomerId { get; }
    /// <summary>Orbit member id.</summary>
    public string OrbitMemberId { get; private set; } = string.Empty;
    /// <summary>Enrolled at.</summary>
    public DateTimeOffset EnrolledAt { get; }
    /// <summary>Tier id (cached).</summary>
    public string? TierId { get; private set; }
    /// <summary>Balance cache.</summary>
    public decimal BalanceCache { get; private set; }
    /// <summary>Cache as-at.</summary>
    public DateTimeOffset BalanceCacheAsAt { get; private set; }
    /// <summary>Last sync.</summary>
    public DateTimeOffset? LastSyncAt { get; private set; }
}

/// <summary>Loyalty transaction event log.</summary>
public sealed class LoyaltyTransaction : Entity, IImmutableRecord
{
    private LoyaltyTransaction() { }
    /// <summary>Creates.</summary>
    public LoyaltyTransaction(Guid tenantId, Guid customerId, TransactionType type, decimal amount, string? orbitTransactionId = null) : base(tenantId)
    {
        CustomerId = customerId; TransactionType = type; Amount = amount; OrbitTransactionId = orbitTransactionId;
    }
    /// <summary>Customer.</summary>
    public Guid CustomerId { get; }
    /// <summary>Type.</summary>
    public TransactionType TransactionType { get; }
    /// <summary>Amount.</summary>
    public decimal Amount { get; }
    /// <summary>Orbit transaction id.</summary>
    public string? OrbitTransactionId { get; set; }
    /// <summary>Status.</summary>
    public TransactionStatus Status { get; private set; }
    /// <summary>Marks confirmed.</summary>
    public void MarkConfirmed(string orbitTransactionId) { Status = TransactionStatus.Confirmed; OrbitTransactionId = orbitTransactionId; }
    /// <summary>Marks queued.</summary>
    public void MarkQueuedForRetry() => Status = TransactionStatus.QueuedForRetry;
    /// <summary>Marks failed.</summary>
    public void MarkFailed() => Status = TransactionStatus.Failed;
}

/// <summary>Transaction type.</summary>
public enum TransactionType { Earn, Burn, Adjustment, Expiry }
/// <summary>Transaction status.</summary>
public enum TransactionStatus { Pending, Confirmed, QueuedForRetry, Failed }
#pragma warning restore CS1591
