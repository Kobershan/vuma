#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class CheckoutIntent : Entity
{
    private CheckoutIntent(Guid tenantId, Guid companyId, Guid channelId, Guid basketId, string ownerKey,
        string idempotencyKey, string contentFingerprint, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        ChannelConnectionId = channelId;
        BasketId = basketId;
        OwnerKey = ownerKey.Trim();
        IdempotencyKey = idempotencyKey.Trim();
        ContentFingerprint = contentFingerprint.Trim();
        CreatedAtUtc = createdAt;
        ExpiresAtUtc = expiresAt;
        Status = CheckoutIntentStatus.Pending;
    }

    private CheckoutIntent() { }

    public Guid ChannelConnectionId { get; private set; }
    public Guid BasketId { get; private set; }
    public string OwnerKey { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ContentFingerprint { get; private set; } = string.Empty;
    public CheckoutIntentStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    public static CheckoutIntent Submit(Guid tenantId, Guid companyId, Guid channelId, Guid basketId,
        string ownerKey, string idempotencyKey, string contentFingerprint, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || channelId == Guid.Empty || basketId == Guid.Empty)
        {
            throw new ArgumentException("A checkout requires tenant, company, channel and basket identities.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint);
        return new CheckoutIntent(tenantId, companyId, channelId, basketId, ownerKey, idempotencyKey,
            contentFingerprint, createdAt, createdAt.AddHours(24));
    }

    public void Confirm(DateTimeOffset at)
    {
        EnsurePending();
        Status = CheckoutIntentStatus.Confirmed;
        DecidedAtUtc = at;
    }

    public void Reject(string reason, DateTimeOffset at)
    {
        EnsurePending();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = CheckoutIntentStatus.Rejected;
        DecisionReason = reason.Trim();
        DecidedAtUtc = at;
    }

    public void Expire(DateTimeOffset at)
    {
        if (Status == CheckoutIntentStatus.Pending && at >= ExpiresAtUtc)
        {
            Status = CheckoutIntentStatus.Expired;
            DecidedAtUtc = at;
        }
    }

    public void MarkCompensationPending() 
    {
        EnsurePending();
        Status = CheckoutIntentStatus.CompensationPending;
    }

    private void EnsurePending()
    {
        if (Status != CheckoutIntentStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending checkout intent can be decided.");
        }
    }
}

public enum CheckoutIntentStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    Expired = 4,
    CompensationPending = 5
}
