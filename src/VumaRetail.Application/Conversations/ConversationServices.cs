#pragma warning disable CS1591
#pragma warning disable IDE0011
using System.Collections.Concurrent;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Bounded in-memory contact resolver used by the default store installation and tests.</summary>
public sealed class InMemoryContactResolver : IContactResolver
{
    private readonly ConcurrentDictionary<string, ContactBinding> bindings = new(StringComparer.OrdinalIgnoreCase);
    public void Add(ContactBinding binding) { ArgumentNullException.ThrowIfNull(binding); bindings[$"{binding.Channel}:{binding.Address}"] = binding; }
    public Task<ContactBinding?> ResolveAsync(ConversationChannel channel, string address, CancellationToken cancellationToken = default)
    { ArgumentException.ThrowIfNullOrWhiteSpace(address); return Task.FromResult(bindings.TryGetValue($"{channel}:{address.Trim()}", out ContactBinding? binding) ? binding : null); }
}

public sealed class VerificationService : IVerificationService
{
    public VerificationChallenge Issue(ContactBinding binding, string otp, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(otp);
        return new VerificationChallenge(Guid.Empty, binding.Id, otp, at);
    }

    public bool Verify(ContactBinding binding, VerificationChallenge challenge, string otp, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(challenge);
        if (!challenge.Verify(otp, at)) { return false; }
        binding.Verify(at);
        return true;
    }
}

public sealed class DocumentDeliveryService : IDocumentDeliveryService
{
    public DocumentDeliveryToken Mint(ContactBinding binding, string documentReference, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.IsUsable(at)) throw new InvalidOperationException("A verified binding is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(documentReference);
        return new DocumentDeliveryToken(Guid.Empty, binding.Id, documentReference, at);
    }
    public bool TryFetch(DocumentDeliveryToken token, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!token.IsAvailable(at)) { return false; }
        token.AuditFetch(at);
        return true;
    }
}

/// <summary>Applies universal commands and keeps state transitions explicit.</summary>
public sealed class ConversationStateMachine(IIntentClassifier classifier) : IConversationStateMachine
{
    public async Task<ConversationState> HandleAsync(Conversation conversation, string message, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        string command = message.Trim().ToUpperInvariant();
        if (command is "AGENT" or "HELP") { conversation.Escalate(at); return conversation.State; }
        if (command is "STOP") { conversation.Escalate(at); return conversation.State; }
        if (command is "CANCEL") { conversation.Reset(at); return conversation.State; }
        if (conversation.State == ConversationState.Idle)
        {
            IntentClassification classification = await classifier.ClassifyAsync(message, cancellationToken).ConfigureAwait(false);
            if (classification.Intent == ConversationIntent.Unknown || classification.Confidence < 0.7m)
            {
                conversation.Escalate(at);
                return conversation.State;
            }
            conversation.BeginVerification(classification.Intent, at);
        }
        return conversation.State;
    }
}
