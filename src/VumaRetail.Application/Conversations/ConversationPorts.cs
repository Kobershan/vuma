#pragma warning disable CS1591
#pragma warning disable IDE0011
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Classification result. The classifier has no data or tool access.</summary>
public sealed record IntentClassification(ConversationIntent Intent, IReadOnlyDictionary<string, string> Entities, decimal Confidence);

public interface IIntentClassifier
{
    Task<IntentClassification> ClassifyAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>Only API-returned facts may be supplied to the reply composer.</summary>
public sealed record ReplyFacts(IReadOnlyList<string> Facts, string FallbackText);
public interface IReplyComposer
{
    Task<string> ComposeAsync(ReplyFacts facts, CancellationToken cancellationToken = default);
}

public interface IContactResolver
{
    Task<ContactBinding?> ResolveAsync(ConversationChannel channel, string address, CancellationToken cancellationToken = default);
}

public interface IVerificationService
{
    VerificationChallenge Issue(ContactBinding binding, string otp, DateTimeOffset at);
    bool Verify(ContactBinding binding, VerificationChallenge challenge, string otp, DateTimeOffset at);
}

public interface IDocumentDeliveryService
{
    DocumentDeliveryToken Mint(ContactBinding binding, string documentReference, DateTimeOffset at);
    bool TryFetch(DocumentDeliveryToken token, DateTimeOffset at);
}

/// <summary>Result returned by an intent handler; it contains API facts only.</summary>
public sealed record IntentResult(Guid ResultId, IReadOnlyList<string> Facts, bool RequiresConfirmation = false, string? IdempotencyKey = null);

/// <summary>One existing-module operation exposed to the deterministic conversation router.</summary>
public interface IConversationIntentHandler
{
    ConversationIntent Intent { get; }
    Task<IntentResult> HandleAsync(Conversation conversation, IReadOnlyDictionary<string, string> entities, string idempotencyKey, CancellationToken cancellationToken = default);
}

/// <summary>Routes only the six allow-listed intents; unknown intents are never handled.</summary>
public interface IConversationIntentRouter
{
    Task<IntentResult> RouteAsync(Conversation conversation, IntentClassification classification, string idempotencyKey, CancellationToken cancellationToken = default);
}

public interface IConversationStateMachine
{
    Task<ConversationState> HandleAsync(Conversation conversation, string message, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic keyword fallback used when no model provider is configured.</summary>
public sealed class KeywordIntentClassifier : IIntentClassifier
{
    public Task<IntentClassification> ClassifyAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        string lower = message.ToLowerInvariant();
        ConversationIntent intent = lower switch
        {
            var x when x.Contains("statement") || x.Contains("balance") => ConversationIntent.RequestStatement,
            var x when x.Contains("pod") || x.Contains("proof of delivery") => ConversationIntent.RequestPod,
            var x when x.Contains("invoice") => ConversationIntent.RequestInvoiceCopy,
            var x when x.Contains("credit note") || x.Contains("credit") => ConversationIntent.RequestCreditNote,
            var x when x.Contains("status") || x.Contains("where is") => ConversationIntent.OrderStatus,
            var x when x.Contains("order") || x.Contains("buy") || x.Contains("please send") => ConversationIntent.PlaceOrder,
            _ => ConversationIntent.Unknown
        };
        return Task.FromResult(new IntentClassification(intent, new Dictionary<string, string>(), intent == ConversationIntent.Unknown ? 0m : 1m));
    }
}

/// <summary>Safe default composer: it never invents facts and never calls an external model.</summary>
public sealed class TemplateReplyComposer : IReplyComposer
{
    public Task<string> ComposeAsync(ReplyFacts facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string result = facts.Facts.Count == 0 ? facts.FallbackText : string.Join(" ", facts.Facts);
        return Task.FromResult(result);
    }
}
