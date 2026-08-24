using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>Something POS-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>sale</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class PosNotFoundException(string what, Guid id)
    : DomainException("POS_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something POS-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class PosConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A terminal already has a till session open, and a second would make the cash-up meaningless.</summary>
    /// <param name="terminalId">The terminal.</param>
    public static PosConflictException TillSessionAlreadyOpen(Guid terminalId)
        => new(
            "POS_TILL_SESSION_ALREADY_OPEN",
            $"Terminal {terminalId} already has an open till session. Close it before opening another — "
            + "two open sessions on one drawer means neither one's expected cash is a real number.");
}

/// <summary>
/// A caller was correctly authenticated and the request was well formed, and they still may not do
/// this (<c>DomainProblemKind.Forbidden</c>, 403) — the in-handler counterpart to a
/// <c>RequirePermission</c> refusal, for the one POS write whose legality only the handler can see
/// (§4.15).
/// </summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">Why.</param>
public sealed class PosForbiddenException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>An operator without <c>pos.receipt.reprint</c> tried to print a sale's receipt again.</summary>
    public static PosForbiddenException ReceiptReprintNotPermitted()
        => new(
            "POS_RECEIPT_REPRINT_NOT_PERMITTED",
            "This receipt has already been printed once. Reprinting it needs the pos.receipt.reprint "
            + "permission — the oldest till fraud there is starts with a receipt printed twice.");
}

/// <summary>A POS business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class PosRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A sale line named neither an item nor a variant, or named both.</summary>
    public static PosRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "POS_EXACTLY_ONE_ITEM_OR_VARIANT",
            "A sale line sells exactly one of an item (when it has no variants) or a variant — never "
            + "both, never neither.");

    /// <summary>A sale was mutated after it stopped being open.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static PosRuleException SaleIsNotOpen(SaleStatus status)
        => new(
            "POS_SALE_NOT_OPEN",
            $"This sale is {status} and can no longer be changed. A completed sale is corrected by a "
            + "return, not an edit (CLAUDE.md §7 rule 7).");

    /// <summary>A sale with nothing on it was asked to complete.</summary>
    public static PosRuleException SaleHasNoLines()
        => new("POS_SALE_HAS_NO_LINES", "A sale with no lines cannot be completed.");

    /// <summary>Tenders did not cover the sale.</summary>
    /// <param name="gross">What is owed.</param>
    /// <param name="tendered">What has been taken.</param>
    public static PosRuleException SaleNotFullyTendered(Money gross, Money tendered)
        => new(
            "POS_SALE_NOT_FULLY_TENDERED",
            $"{gross} is owed and {tendered} has been tendered. A sale completes when it is paid for.");

    /// <summary>Change was owed beyond what was taken in cash.</summary>
    /// <param name="change">The change owed.</param>
    /// <param name="cash">The cash taken.</param>
    public static PosRuleException ChangeExceedsCashTendered(Money change, Money cash)
        => new(
            "POS_CHANGE_EXCEEDS_CASH",
            $"{change} change is owed but only {cash} was taken in cash. Change comes out of the drawer, "
            + "so a card or voucher overpayment is a refund the customer arranges, not change at the till.");

    /// <summary>A quantity, price or tender amount was zero or negative where it must be positive.</summary>
    /// <param name="what">Which value, for example <c>quantity</c>.</param>
    public static PosRuleException MustBePositive(string what)
        => new("POS_MUST_BE_POSITIVE", $"The {what} must be greater than zero.");

    /// <summary>A line discount was more than the line is worth.</summary>
    /// <param name="discount">The discount asked for.</param>
    /// <param name="extended">Quantity times unit price.</param>
    public static PosRuleException DiscountExceedsLine(Money discount, Money extended)
        => new(
            "POS_DISCOUNT_EXCEEDS_LINE",
            $"A discount of {discount} was applied to a line worth {extended}. A line cannot be sold for "
            + "less than nothing — give the item away at zero, or take it off the sale.");

    /// <summary>A line's stated net, tax and gross do not agree with each other.</summary>
    /// <param name="net">The net.</param>
    /// <param name="tax">The tax.</param>
    /// <param name="gross">The gross.</param>
    public static PosRuleException LineDoesNotBalance(Money net, Money tax, Money gross)
        => new(
            "POS_LINE_DOES_NOT_BALANCE",
            $"A line's net ({net}) plus tax ({tax}) must equal its gross ({gross}).");

    /// <summary>A line, tender or sale was denominated in a currency the sale is not.</summary>
    /// <param name="expected">The sale's currency.</param>
    /// <param name="actual">The currency that arrived.</param>
    public static PosRuleException CurrencyMismatch(string expected, string actual)
        => new(
            "POS_CURRENCY_MISMATCH",
            $"This sale is in {expected}; {actual} cannot be added to it. Multi-currency tendering needs "
            + "an explicit rate and rate date, which is not this stage's.");

    /// <summary>A line was voided twice, or a voided line was asked to do something.</summary>
    public static PosRuleException LineAlreadyVoided()
        => new("POS_LINE_ALREADY_VOIDED", "This line is already voided.");

    /// <summary>A sale was voided after it completed.</summary>
    public static PosRuleException CompletedSaleCannotBeVoided()
        => new(
            "POS_COMPLETED_SALE_CANNOT_BE_VOIDED",
            "A completed sale is money that changed hands. Reverse it with a return (Stage 10), which "
            + "leaves both documents standing, rather than voiding the record of what happened.");

    /// <summary>A till session was operated on after it closed.</summary>
    public static PosRuleException TillSessionIsClosed()
        => new(
            "POS_TILL_SESSION_CLOSED",
            "This till session is closed. A closed cash-up is a signed count and is never reopened — "
            + "a correction is a new session or a documented adjustment.");

    /// <summary>A till session was closed while sales were still open against it.</summary>
    /// <param name="openSales">How many sales are still open.</param>
    public static PosRuleException TillSessionHasOpenSales(int openSales)
        => new(
            "POS_TILL_SESSION_HAS_OPEN_SALES",
            $"{openSales} sale(s) are still open on this session. Counting the drawer against a total "
            + "that is still moving produces a variance that means nothing — complete, park or void "
            + "them first.");

    /// <summary>A sale was opened against a session belonging to a different terminal.</summary>
    public static PosRuleException SaleTerminalMismatch()
        => new(
            "POS_SALE_TERMINAL_MISMATCH",
            "A sale belongs to the terminal whose till session it was rung up on.");

    /// <summary>A parked sale was resumed when it was not parked, or parked when it was not open.</summary>
    /// <param name="status">The status it is actually in.</param>
    public static PosRuleException UnexpectedSaleStatus(SaleStatus status)
        => new("POS_UNEXPECTED_SALE_STATUS", $"This operation is not available on a {status} sale.");
}
