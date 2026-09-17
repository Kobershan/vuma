#pragma warning disable CS1591
using VumaRetail.Domain.Entities;

namespace VumaRetail.Domain.Conversations;

/// <summary>The outcome of one attempt to deliver a conversation reply through a transport.</summary>
public enum ConversationDeliveryStatus
{
    Sent = 0,
    Failed = 1,
}

/// <summary>Tenant-scoped, durable delivery audit for conversational replies.</summary>
public sealed class ConversationDeliveryAttempt : Entity
{
    private ConversationDeliveryAttempt() { }

    public ConversationDeliveryAttempt(
        Guid tenantId,
        Guid conversationId,
        ConversationChannel channel,
        ConversationDeliveryStatus status,
        DateTimeOffset attemptedAt,
        string? failureReason = null)
        : base(tenantId)
    {
        ConversationId = conversationId;
        Channel = channel;
        Status = status;
        AttemptedAt = attemptedAt;
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim()[..Math.Min(1000, failureReason.Trim().Length)];
    }

    public Guid ConversationId { get; private set; }
    public ConversationChannel Channel { get; private set; }
    public ConversationDeliveryStatus Status { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
    public string? FailureReason { get; private set; }
}
