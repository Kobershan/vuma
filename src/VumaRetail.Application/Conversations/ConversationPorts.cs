#pragma warning disable CS1591
#pragma warning disable IDE0011
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    Task<DocumentDeliveryToken> MintAsync(ContactBinding binding, string documentReference, DateTimeOffset at, CancellationToken cancellationToken = default);
    bool TryFetch(DocumentDeliveryToken token, DateTimeOffset at);
    Task<string?> FetchAsync(string token, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>Persistence boundary for one-time document delivery tokens.</summary>
public interface IDocumentDeliveryTokenStore
{
    Task AddAsync(DocumentDeliveryToken token, CancellationToken cancellationToken = default);
    Task<DocumentDeliveryToken?> FindAsync(string token, CancellationToken cancellationToken = default);
    Task SaveAsync(DocumentDeliveryToken token, CancellationToken cancellationToken = default);
}

/// <summary>Persistence boundary for conversation state and append-only transcript entries.</summary>
public interface IConversationStore
{
    Task<Conversation> GetOrCreateAsync(ContactBinding binding, ConversationChannel channel, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task<ConversationTurn?> FindTurnByExternalMessageIdAsync(Guid conversationId, string externalMessageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationTurn>> ListTurnsAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default);
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

/// <summary>Pure HMAC verification for transport adapters.</summary>
public static class ConversationWebhookSecurity
{
    public static bool Verify(string body, string? presentedSignature, string secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secret);
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(presentedSignature)
            || !presentedSignature.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        try
        {
            byte[] actual = Convert.FromHexString(presentedSignature["sha256=".Length..]);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
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
        Dictionary<string, string> entities = new(StringComparer.OrdinalIgnoreCase);
        if (intent == ConversationIntent.OrderStatus)
        {
            Match orderNumber = Regex.Match(message, @"(?:order|#)\s*([A-Za-z0-9][A-Za-z0-9/-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (orderNumber.Success)
            {
                entities["orderNumber"] = orderNumber.Groups[1].Value;
            }
        }

        return Task.FromResult(new IntentClassification(intent, entities, intent == ConversationIntent.Unknown ? 0m : 1m));
    }
}

/// <summary>Safe default composer: it never invents facts and never calls an external model.</summary>
public sealed class TemplateReplyComposer : IReplyComposer
{
    public Task<string> ComposeAsync(ReplyFacts facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string result = facts.Facts.Count == 0 ? facts.FallbackText : string.Join(" ", facts.Facts);
        return Task.FromResult(
            facts.Facts.Count == 0 || ReplySafety.ContainsOnlyApiFacts(result, facts)
                ? result
                : facts.FallbackText);
    }
}
