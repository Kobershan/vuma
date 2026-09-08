using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Rule violations for customer credit accounts and their holders.</summary>
public sealed class CustomerAccountExceptions(string code, string message) : DomainException(code, message)
{
    /// <summary>The limit is not positive.</summary>
    public static CustomerAccountExceptions LimitMustBePositive()
        => new("ACCOUNT_LIMIT_MUST_BE_POSITIVE", "The credit limit must be greater than zero.");

    /// <summary>The terms are not positive.</summary>
    public static CustomerAccountExceptions TermsMustBePositive()
        => new("ACCOUNT_TERMS_MUST_BE_POSITIVE", "The payment terms must be greater than zero days.");

    /// <summary>A holder's per-charge cap is not positive.</summary>
    public static CustomerAccountExceptions HolderLimitMustBePositive()
        => new("ACCOUNT_HOLDER_LIMIT_MUST_BE_POSITIVE", "An authorised buyer's charge limit must be greater than zero.");

    /// <summary>A closed account cannot be held.</summary>
    public static CustomerAccountExceptions ClosedAccountCannotHold()
        => new("ACCOUNT_CLOSED_CANNOT_HOLD", "A closed account cannot be held.");

    /// <summary>A closed account cannot be released.</summary>
    public static CustomerAccountExceptions ClosedAccountCannotRelease()
        => new("ACCOUNT_CLOSED_CANNOT_RELEASE", "A closed account cannot be released.");

    /// <summary>The account may not take this charge.</summary>
    /// <param name="actual">Where the account stands.</param>
    public static CustomerAccountExceptions AccountNotChargeable(AccountStatus actual)
        => new("ACCOUNT_NOT_CHARGEABLE", $"This account cannot take a charge while it is {actual}.");

    /// <summary>The buyer may not put this much on one charge.</summary>
    public static CustomerAccountExceptions HolderLimitExceeded()
        => new("ACCOUNT_HOLDER_LIMIT_EXCEEDED", "This charge exceeds the buyer's personal limit.");

    /// <summary>The action needs an approval that has not been granted.</summary>
    public static CustomerAccountExceptions ApprovalRequired()
        => new("ACCOUNT_APPROVAL_REQUIRED", "This action needs an approval before it can proceed.");
}
