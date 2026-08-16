using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>Something sales-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>price list</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class SalesNotFoundException(string what, Guid id)
    : DomainException("SALES_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something sales-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class SalesConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A price list code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static SalesConflictException PriceListCode(string code)
        => new("SALES_PRICE_LIST_CODE_TAKEN", $"Price list code '{code}' is already used in this tenant.");

    /// <summary>A promotion code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static SalesConflictException PromotionCode(string code)
        => new("SALES_PROMOTION_CODE_TAKEN", $"Promotion code '{code}' is already used in this tenant.");

    /// <summary>A price already exists for this item at this quantity break on this list.</summary>
    public static SalesConflictException DuplicatePriceListLine()
        => new(
            "SALES_DUPLICATE_PRICE_LIST_LINE",
            "This price list already prices that item at that minimum quantity. Update the existing "
            + "line rather than adding a second one — two prices for the same break is not a price.");
}

/// <summary>A sales business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class SalesRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A priced row named neither an item nor a variant, or named both.</summary>
    public static SalesRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "SALES_EXACTLY_ONE_ITEM_OR_VARIANT",
            "A priced row identifies exactly one of an item (when it has no variants) or a variant "
            + "— never both, never neither.");

    /// <summary>A quantity that must be positive was zero or negative.</summary>
    public static SalesRuleException QuantityMustBePositive()
        => new("SALES_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>A price was negative. A price list does not pay customers.</summary>
    public static SalesRuleException PriceMustNotBeNegative()
        => new("SALES_PRICE_MUST_NOT_BE_NEGATIVE", "A unit price cannot be negative.");

    /// <summary>An effective window ended before it began.</summary>
    public static SalesRuleException EffectiveWindowInverted()
        => new(
            "SALES_EFFECTIVE_WINDOW_INVERTED",
            "The effective-to date cannot fall before the effective-from date.");

    /// <summary>A percentage reward was outside 0–100.</summary>
    public static SalesRuleException PercentageOutOfRange()
        => new("SALES_PERCENTAGE_OUT_OF_RANGE", "A discount percentage must be between 0 and 100.");

    /// <summary>A promotion's reward parameter did not match the kind it declares.</summary>
    /// <param name="kind">The kind that was declared.</param>
    /// <param name="expected">What that kind needs, in words.</param>
    public static SalesRuleException PromotionParameterMismatch(PromotionKind kind, string expected)
        => new(
            "SALES_PROMOTION_PARAMETER_MISMATCH",
            $"A {kind} promotion {expected}.");

    /// <summary>A price list line was priced in a different currency than its list.</summary>
    /// <param name="expected">The list's currency.</param>
    /// <param name="actual">The currency the line tried to use.</param>
    public static SalesRuleException CurrencyMismatch(string expected, string actual)
        => new(
            "SALES_CURRENCY_MISMATCH",
            $"This price list is denominated in {expected}; the line supplied {actual}. Converting "
            + "needs an explicit rate and rate date, which is Finance's job, not a price list's.");

    /// <summary>A return was raised against a sale that never completed.</summary>
    /// <param name="status">What the sale's status actually is.</param>
    public static SalesRuleException OnlyCompletedSalesCanBeReturned(string status)
        => new(
            "SALES_RETURN_REQUIRES_COMPLETED_SALE",
            $"Only a completed sale can be returned; this one is {status}.");

    /// <summary>A return line tried to send back more than was ever sold.</summary>
    /// <param name="sold">How many were sold on the original line.</param>
    /// <param name="alreadyReturned">How many have already come back.</param>
    /// <param name="requested">How many this return is asking for.</param>
    public static SalesRuleException ReturnExceedsQuantitySold(
        decimal sold, decimal alreadyReturned, decimal requested)
        => new(
            "SALES_RETURN_EXCEEDS_QUANTITY_SOLD",
            $"That line sold {sold}, of which {alreadyReturned} has already been returned; this return "
            + $"asks for {requested}. A return can never exceed what was sold.");

    /// <summary>A return document was completed with nothing on it.</summary>
    public static SalesRuleException ReturnHasNoLines()
        => new("SALES_RETURN_HAS_NO_LINES", "A return must have at least one line before it completes.");

    /// <summary>Something was done to a return that its status does not allow.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static SalesRuleException UnexpectedReturnStatus(SalesReturnStatus status)
        => new("SALES_UNEXPECTED_RETURN_STATUS", $"That is not allowed while the return is {status}.");

    /// <summary>The same original sale line was put on one return document twice.</summary>
    public static SalesRuleException DuplicateReturnLine()
        => new(
            "SALES_DUPLICATE_RETURN_LINE",
            "That sale line is already on this return. Change its quantity rather than adding it twice "
            + "— two rows for one line is how an over-return slips past a per-line check.");
}
