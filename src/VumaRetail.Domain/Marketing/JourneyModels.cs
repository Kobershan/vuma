#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Marketing;

/// <summary>Lifecycle of a versioned marketing journey definition.</summary>
public enum JourneyDefinitionStatus { Draft, Published, Retired }

/// <summary>Versioned, tenant-owned journey definition. The JSON is declarative and contains no executable code.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class JourneyDefinition : Entity
{
    private JourneyDefinition(Guid tenantId, Guid companyId, string name, int version, string definitionJson)
        : base(tenantId) { AssignCompany(companyId); Name = name.Trim(); Version = version; DefinitionJson = definitionJson.Trim(); }
    private JourneyDefinition() { }
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string DefinitionJson { get; private set; } = string.Empty;
    public JourneyDefinitionStatus Status { get; private set; } = JourneyDefinitionStatus.Draft;
    public static JourneyDefinition Create(Guid tenantId, Guid companyId, string name, int version, string definitionJson)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) { throw new ArgumentException("Tenant and company are required."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);
        if (version < 1) { throw new ArgumentOutOfRangeException(nameof(version)); }
        return new JourneyDefinition(tenantId, companyId, name, version, definitionJson);
    }
    public void Publish() { if (Status != JourneyDefinitionStatus.Draft) { throw new InvalidOperationException("Only draft journeys can be published."); } Status = JourneyDefinitionStatus.Published; }
    public void Retire() { if (Status == JourneyDefinitionStatus.Retired) { throw new InvalidOperationException("The journey is already retired."); } Status = JourneyDefinitionStatus.Retired; }
}

/// <summary>Durable customer enrollment in a published journey.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class JourneyEnrollment : Entity
{
    private JourneyEnrollment(Guid tenantId, Guid companyId, Guid journeyId, Guid customerId, string idempotencyKey, DateTimeOffset nextRunAt)
        : base(tenantId) { AssignCompany(companyId); JourneyDefinitionId = journeyId; CustomerId = customerId; IdempotencyKey = idempotencyKey.Trim(); NextRunAt = nextRunAt.ToUniversalTime(); }
    private JourneyEnrollment() { }
    public Guid JourneyDefinitionId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset NextRunAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public static JourneyEnrollment Create(Guid tenantId, Guid companyId, Guid journeyId, Guid customerId, string idempotencyKey, DateTimeOffset nextRunAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || journeyId == Guid.Empty || customerId == Guid.Empty) { throw new ArgumentException("Journey enrollment identities are required."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return new JourneyEnrollment(tenantId, companyId, journeyId, customerId, idempotencyKey, nextRunAt);
    }
    public void Pause() => IsActive = false;
    public void Resume(DateTimeOffset nextRunAt) { IsActive = true; NextRunAt = nextRunAt.ToUniversalTime(); }
}

/// <summary>Append-only campaign attribution event; it contains counts and references, not message content.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class AttributionEvent : Entity, IImmutableRecord
{
    private AttributionEvent(Guid tenantId, Guid companyId, Guid? campaignId, Guid? messageId, Guid customerId, string eventType, DateTimeOffset occurredAt)
        : base(tenantId) { AssignCompany(companyId); CampaignId = campaignId; OutboundMessageId = messageId; CustomerId = customerId; EventType = eventType.Trim(); OccurredAt = occurredAt.ToUniversalTime(); }
    private AttributionEvent() { }
    public Guid? CampaignId { get; private set; }
    public Guid? OutboundMessageId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public static AttributionEvent Record(Guid tenantId, Guid companyId, Guid? campaignId, Guid? messageId, Guid customerId, string eventType, DateTimeOffset occurredAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || customerId == Guid.Empty) { throw new ArgumentException("Attribution identities are required."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new AttributionEvent(tenantId, companyId, campaignId, messageId, customerId, eventType, occurredAt);
    }
}
