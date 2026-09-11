#pragma warning disable CS1591
#pragma warning disable IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Conversations;

/// <summary>Tenant-scoped state for one WhatsApp or email conversation.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class Conversation : Entity
{
    private Conversation() { }

    public Conversation(Guid tenantId, Guid bindingId, ConversationChannel channel, DateTimeOffset at)
        : base(tenantId)
    {
        ContactBindingId = bindingId;
        Channel = channel;
        State = ConversationState.Idle;
        LastActivityAt = at;
    }

    public Guid ContactBindingId { get; private set; }
    public ConversationChannel Channel { get; private set; }
    public ConversationState State { get; private set; }
    public ConversationIntent CurrentIntent { get; private set; }
    public Guid? CompanyIdInPlay { get => CompanyId; private set => CompanyId = value; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? EscalatedAt { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void BeginVerification(ConversationIntent intent, DateTimeOffset at)
    {
        EnsureNotEscalated();
        CurrentIntent = intent;
        State = ConversationState.Verifying;
        Touch(at);
    }

    public void VerificationPassed(DateTimeOffset at)
    {
        EnsureState(ConversationState.Verifying);
        State = ConversationState.Collecting;
        Touch(at);
    }

    public void BeginConfirmation(Guid? companyId, string idempotencyKey, DateTimeOffset at)
    {
        EnsureState(ConversationState.Collecting);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        CompanyIdInPlay = companyId;
        IdempotencyKey = idempotencyKey;
        State = ConversationState.Confirming;
        Touch(at);
    }

    public void Confirmed(DateTimeOffset at)
    {
        EnsureState(ConversationState.Confirming);
        State = ConversationState.Submitting;
        Touch(at);
    }

    public void Completed(DateTimeOffset at)
    {
        EnsureState(ConversationState.Submitting);
        State = ConversationState.Done;
        Touch(at);
    }

    public void Escalate(DateTimeOffset at)
    {
        if (State == ConversationState.Done) { return; }
        State = ConversationState.Escalated;
        EscalatedAt = at;
        Touch(at);
    }

    public void Reset(DateTimeOffset at)
    {
        if (State == ConversationState.Escalated) { throw new InvalidOperationException("An escalated conversation requires a human."); }
        State = ConversationState.Idle;
        CurrentIntent = ConversationIntent.Unknown;
        Touch(at);
    }

    private void EnsureNotEscalated() => EnsureState(ConversationState.Idle);
    private void EnsureState(ConversationState expected)
    {
        if (State != expected) { throw new InvalidOperationException($"Conversation is {State}; expected {expected}."); }
    }
    private void Touch(DateTimeOffset at) => LastActivityAt = at;
}

/// <summary>Immutable inbound or outbound transcript entry.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ConversationTurn : Entity, IImmutableRecord
{
    private ConversationTurn() { }
    public ConversationTurn(Guid tenantId, Guid conversationId, ConversationTurnDirection direction, string text, DateTimeOffset at, string? externalMessageId = null)
        : base(tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ConversationId = conversationId;
        Direction = direction;
        Text = text.Trim();
        HappenedAt = at;
        ExternalMessageId = string.IsNullOrWhiteSpace(externalMessageId) ? null : externalMessageId.Trim();
    }
    public Guid ConversationId { get; private set; }
    public ConversationTurnDirection Direction { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public ConversationIntent? ClassifiedIntent { get; private set; }
    public string? ExtractedEntities { get; private set; }
    public Guid? PhrasedFromResultId { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public DateTimeOffset HappenedAt { get; private set; }
}
