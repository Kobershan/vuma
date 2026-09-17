#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Marketing;

public enum MarketingCampaignStatus { Draft, Scheduled, Cancelled }
public enum OutboundMessageStatus { Queued, Suppressed, Sent, Failed }
public enum MarketingMessageChannel { Email = 1, Sms = 2, WhatsApp = 3, Push = 4 }
public enum MarketingMessageClassification { Marketing = 1, Transactional = 2 }

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class MarketingCampaign : Entity
{
    private MarketingCampaign(Guid tenantId, Guid? storeId, Guid companyId, string name, string templateId,
        DateTimeOffset scheduledAt) : base(tenantId, storeId)
    { AssignCompany(companyId); Name = name.Trim(); TemplateId = templateId.Trim(); ScheduledAt = scheduledAt.ToUniversalTime(); }
    private MarketingCampaign() { }
    public string Name { get; private set; } = string.Empty;
    public string TemplateId { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public MarketingCampaignStatus Status { get; private set; } = MarketingCampaignStatus.Draft;
    public static MarketingCampaign Create(Guid tenantId, Guid? storeId, Guid companyId, string name, string templateId, DateTimeOffset scheduledAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and company are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return new MarketingCampaign(tenantId, storeId, companyId, name, templateId, scheduledAt);
    }
    public void Schedule(DateTimeOffset now)
    {
        if (Status != MarketingCampaignStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft campaign can be scheduled.");
        }
        if (ScheduledAt < now.ToUniversalTime())
        {
            throw new InvalidOperationException("A campaign cannot be scheduled in the past.");
        }
        Status = MarketingCampaignStatus.Scheduled;
    }
    public void Cancel()
    {
        if (Status == MarketingCampaignStatus.Cancelled)
        {
            throw new InvalidOperationException("The campaign is already cancelled.");
        }
        Status = MarketingCampaignStatus.Cancelled;
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class OutboundMessage : Entity, IImmutableRecord
{
    private OutboundMessage(Guid tenantId, Guid? storeId, Guid companyId, Guid campaignId, Guid customerId, string idempotencyKey, DateTimeOffset scheduledAt,
        MarketingMessageChannel channel, MarketingMessageClassification classification) : base(tenantId, storeId)
    { AssignCompany(companyId); CampaignId = campaignId; CustomerId = customerId; IdempotencyKey = idempotencyKey.Trim(); ScheduledAt = scheduledAt.ToUniversalTime(); Channel = channel; Classification = classification; }
    private OutboundMessage() { }
    public Guid CampaignId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public OutboundMessageStatus Status { get; private set; } = OutboundMessageStatus.Queued;
    public MarketingMessageChannel Channel { get; private set; } = MarketingMessageChannel.Email;
    public MarketingMessageClassification Classification { get; private set; } = MarketingMessageClassification.Marketing;
    public string? ProviderEventId { get; private set; }
    public string? ProviderPayloadFingerprint { get; private set; }
    public int DeliveryAttemptCount { get; private set; }
    public DateTimeOffset? LastDeliveryAttemptAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastDeliveryFailure { get; private set; }
    public static OutboundMessage Queue(Guid tenantId, Guid? storeId, Guid companyId, Guid campaignId, Guid customerId, string idempotencyKey, DateTimeOffset scheduledAt,
        MarketingMessageChannel channel = MarketingMessageChannel.Email, MarketingMessageClassification classification = MarketingMessageClassification.Marketing)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || campaignId == Guid.Empty || customerId == Guid.Empty)
        {
            throw new ArgumentException("Outbound message identities are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (!Enum.IsDefined(channel) || !Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
        return new OutboundMessage(tenantId, storeId, companyId, campaignId, customerId, idempotencyKey, scheduledAt, channel, classification);
    }
    public void Suppress()
    {
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued message can be suppressed.");
        }
        Status = OutboundMessageStatus.Suppressed;
    }
    public void MarkSent()
    {
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued message can be sent.");
        }
        Status = OutboundMessageStatus.Sent;
    }

    public void ApplyProviderResult(string providerEventId, string payloadFingerprint, bool delivered)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadFingerprint);
        if (ProviderEventId is not null)
        {
            if (ProviderEventId == providerEventId && ProviderPayloadFingerprint == payloadFingerprint)
            {
                return;
            }
            throw new InvalidOperationException("A provider event id cannot be reused with different content.");
        }
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued message can accept a provider result.");
        }
        ProviderEventId = providerEventId.Trim();
        ProviderPayloadFingerprint = payloadFingerprint.Trim();
        NextAttemptAtUtc = null;
        LastDeliveryFailure = delivered ? null : LastDeliveryFailure;
        Status = delivered ? OutboundMessageStatus.Sent : OutboundMessageStatus.Failed;
    }

    /// <summary>Records a provider attempt without changing the durable queued state.</summary>
    public void RecordDeliveryAttempt(DateTimeOffset attemptedAtUtc)
    {
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued message can be attempted.");
        }
        DeliveryAttemptCount++;
        LastDeliveryAttemptAtUtc = attemptedAtUtc.ToUniversalTime();
    }

    /// <summary>Records a retryable transport failure while leaving the message queued.</summary>
    public void RecordDeliveryFailure(string reason, DateTimeOffset failedAtUtc)
    {
        if (Status != OutboundMessageStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued message can record a delivery failure.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        LastDeliveryFailure = reason.Trim()[..Math.Min(reason.Trim().Length, 1024)];
        DateTimeOffset failedAt = failedAtUtc.ToUniversalTime();
        int exponent = Math.Clamp(DeliveryAttemptCount - 1, 0, 10);
        NextAttemptAtUtc = failedAt.AddMinutes(Math.Min(24 * 60, Math.Pow(2, exponent)));
    }
}
