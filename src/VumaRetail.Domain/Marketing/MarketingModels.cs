#pragma warning disable CS1591
using VumaRetail.Domain.Entities;

namespace VumaRetail.Domain.Marketing;

public enum MarketingCampaignStatus { Draft, Scheduled, Cancelled }
public enum OutboundMessageStatus { Queued, Suppressed, Sent, Failed }

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
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return new MarketingCampaign(tenantId, storeId, companyId, name, templateId, scheduledAt);
    }
    public void Schedule(DateTimeOffset now)
    {
        if (Status != MarketingCampaignStatus.Draft) throw new InvalidOperationException("Only a draft campaign can be scheduled.");
        if (ScheduledAt < now.ToUniversalTime()) throw new InvalidOperationException("A campaign cannot be scheduled in the past.");
        Status = MarketingCampaignStatus.Scheduled;
    }
    public void Cancel()
    {
        if (Status == MarketingCampaignStatus.Cancelled) throw new InvalidOperationException("The campaign is already cancelled.");
        Status = MarketingCampaignStatus.Cancelled;
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class OutboundMessage : Entity, IImmutableRecord
{
    private OutboundMessage(Guid tenantId, Guid? storeId, Guid companyId, Guid campaignId, Guid customerId, string idempotencyKey, DateTimeOffset scheduledAt) : base(tenantId, storeId)
    { AssignCompany(companyId); CampaignId = campaignId; CustomerId = customerId; IdempotencyKey = idempotencyKey.Trim(); ScheduledAt = scheduledAt.ToUniversalTime(); }
    private OutboundMessage() { }
    public Guid CampaignId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public OutboundMessageStatus Status { get; private set; } = OutboundMessageStatus.Queued;
    public static OutboundMessage Queue(Guid tenantId, Guid? storeId, Guid companyId, Guid campaignId, Guid customerId, string idempotencyKey, DateTimeOffset scheduledAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || campaignId == Guid.Empty || customerId == Guid.Empty) throw new ArgumentException("Outbound message identities are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return new OutboundMessage(tenantId, storeId, companyId, campaignId, customerId, idempotencyKey, scheduledAt);
    }
    public void Suppress()
    {
        if (Status != OutboundMessageStatus.Queued) throw new InvalidOperationException("Only a queued message can be suppressed.");
        Status = OutboundMessageStatus.Suppressed;
    }
    public void MarkSent()
    {
        if (Status != OutboundMessageStatus.Queued) throw new InvalidOperationException("Only a queued message can be sent.");
        Status = OutboundMessageStatus.Sent;
    }
}
