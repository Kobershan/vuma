using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Rule violations for lay-by agreements.</summary>
public sealed class LayByExceptions(string code, string message) : DomainException(code, message)
{
    /// <summary>The agreement is not in the status the operation needs.</summary>
    /// <param name="actual">Where it actually stands.</param>
    public static LayByExceptions UnexpectedStatus(LayByStatus actual)
        => new("LAYBY_UNEXPECTED_STATUS", $"This operation cannot be performed while the agreement is {actual}.");

    /// <summary>An instalment would take paid past the agreed total.</summary>
    public static LayByExceptions Overpayment()
        => new("LAYBY_OVERPAYMENT", "This instalment would take paid past the agreed total.");

    /// <summary>Completion was attempted before the agreed total was paid.</summary>
    public static LayByExceptions NotFullyPaid()
        => new("LAYBY_NOT_FULLY_PAID", "A lay-by completes only once the agreed total is paid in full.");

    /// <summary>The refund and fee do not account for every cent paid.</summary>
    public static LayByExceptions CancellationMustAccount()
        => new("LAYBY_CANCELLATION_MUST_ACCOUNT", "Refund plus fee must equal exactly what was paid.");

    /// <summary>Exactly one of an item or a variant is required on a line.</summary>
    public static LayByExceptions ExactlyOneItemOrVariantRequired()
        => new("LAYBY_EXACTLY_ONE_ITEM_OR_VARIANT", "A lay-by line identifies exactly one of an item or a variant.");

    /// <summary>The quantity is not positive.</summary>
    public static LayByExceptions QuantityMustBePositive()
        => new("LAYBY_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>The pack size could not be resolved.</summary>
    public static LayByExceptions PackSizeNotResolved()
        => new("LAYBY_PACK_SIZE_NOT_RESOLVED", "Could not resolve the pack size for this lay-by line.");

    /// <summary>The agreement number is missing.</summary>
    public static LayByExceptions MissingNumber()
        => new("LAYBY_MISSING_NUMBER", "A lay-by agreement must carry its series number.");

    /// <summary>The tenant has no customer-finance terms row.</summary>
    public static LayByExceptions TermsNotConfigured()
        => new("LAYBY_TERMS_NOT_CONFIGURED", "The tenant has no customer-finance terms configured.");

    /// <summary>The requested term is longer than the tenant offers.</summary>
    /// <param name="maxMonths">The longest term on offer.</param>
    public static LayByExceptions TermExceedsMaximum(int maxMonths)
        => new("LAYBY_TERM_EXCEEDS_MAXIMUM", $"The longest lay-by term on offer is {maxMonths} months.");

    /// <summary>No stock location answers to that code.</summary>
    /// <param name="code">The code that was asked for.</param>
    public static LayByExceptions LocationNotFound(string code)
        => new("LAYBY_LOCATION_NOT_FOUND", $"No stock location answers to '{code}'.");

    /// <summary>The shelf could not cover the full lay-by quantity.</summary>
    public static LayByExceptions InsufficientStock()
        => new("LAYBY_INSUFFICIENT_STOCK", "The shelf could not cover the full lay-by quantity.");

    /// <summary>Completion was captured offline.</summary>
    public static LayByExceptions CompletionNeedsConnectivity()
        => new("LAYBY_COMPLETION_NEEDS_CONNECTIVITY", "A lay-by completes only with connectivity.");
}
