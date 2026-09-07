using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Quotes;

/// <summary>Something quote-owned was asked for that does not exist.</summary>
/// <param name="what">What was being looked for, for example <c>quote</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class QuotesNotFoundException(string what, Guid id)
    : DomainException("QUOTES_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>A quote business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class QuotesRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>Something was done to a quote that its status does not allow.</summary>
    /// <param name="actual">The status it is actually in.</param>
    public static QuotesRuleException QuoteNotDraft(QuoteStatus actual)
        => new("QUOTE_NOT_DRAFT", $"This operation requires a draft quote; the quote is {actual}.");

    /// <summary>A quote line named neither an item nor a variant, or named both.</summary>
    public static QuotesRuleException ExactlyOneItemOrVariantRequired()
        => new("QUOTE_EXACTLY_ONE_ITEM_OR_VARIANT", "A quote line identifies exactly one of an item or a variant.");

    /// <summary>A quantity that must be positive was zero or negative.</summary>
    public static QuotesRuleException QuantityMustBePositive()
        => new("QUOTE_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>An already-accepted quote was accepted again.</summary>
    public static QuotesRuleException QuoteAlreadyAccepted()
        => new("QUOTE_ALREADY_ACCEPTED", "This quote has already been accepted.");

    /// <summary>An already-expired quote was expired again.</summary>
    public static QuotesRuleException QuoteAlreadyExpired()
        => new("QUOTE_ALREADY_EXPIRED", "This quote has already expired.");

    /// <summary>Something was asked of a quote that only an issued quote allows.</summary>
    public static QuotesRuleException QuoteNotIssued()
        => new("QUOTE_NOT_ISSUED", "Only an issued quote can be accepted or rejected.");

    /// <summary>The validity window lapsed before the operation.</summary>
    public static QuotesRuleException QuoteExpired()
        => new("QUOTE_EXPIRED", "This quote has passed its validity period.");

    /// <summary>A lifecycle transition the state machine does not allow.</summary>
    /// <param name="current">The status it is in.</param>
    /// <param name="requested">The status that was asked for.</param>
    public static QuotesRuleException InvalidTransition(QuoteStatus current, QuoteStatus requested)
        => new("QUOTE_INVALID_TRANSITION", $"Cannot transition a quote from {current} to {requested}.");

    /// <summary>A quote was issued with nothing on it.</summary>
    public static QuotesRuleException NoLines()
        => new("QUOTE_NO_LINES", "A quote must have at least one line.");

    /// <summary>An invoice line was built without its pack size snapshot (ADR-112).</summary>
    public static QuotesRuleException PackSizeNotResolved()
        => new("QUOTE_PACK_SIZE_NOT_RESOLVED", "Could not resolve the pack size for this item.");
}
