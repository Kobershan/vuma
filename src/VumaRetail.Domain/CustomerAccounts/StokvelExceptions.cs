using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Rule violations for stokvel groups, members, contributions, benefits and payouts.</summary>
public sealed class StokvelExceptions(string code, string message) : DomainException(code, message)
{
    /// <summary>No such group in this tenant.</summary>
    public static StokvelExceptions GroupNotFound(Guid id)
        => new("STOKVEL_GROUP_NOT_FOUND", $"No stokvel group with id {id}.");

    /// <summary>No such member in this group.</summary>
    public static StokvelExceptions MemberNotFound(Guid id)
        => new("STOKVEL_MEMBER_NOT_FOUND", $"No stokvel member with id {id}.");

    /// <summary>No such payout.</summary>
    public static StokvelExceptions PayoutNotFound(Guid id)
        => new("STOKVEL_PAYOUT_NOT_FOUND", $"No stokvel payout with id {id}.");

    /// <summary>No such hamper basket.</summary>
    public static StokvelExceptions HamperNotFound(Guid id)
        => new("STOKVEL_HAMPER_NOT_FOUND", $"No hamper basket with id {id}.");

    /// <summary>The group is not in the status the operation needs.</summary>
    public static StokvelExceptions UnexpectedStatus(StokvelStatus actual)
        => new("STOKVEL_UNEXPECTED_STATUS", $"This operation cannot be performed while the group is {actual}.");

    /// <summary>The payout is not in the status the operation needs.</summary>
    public static StokvelExceptions UnexpectedPayoutStatus(StokvelPayoutStatus actual)
        => new("STOKVEL_PAYOUT_UNEXPECTED_STATUS", $"This operation cannot be performed while the payout is {actual}.");

    /// <summary>A member who left may not transact.</summary>
    public static StokvelExceptions MemberLeft()
        => new("STOKVEL_MEMBER_LEFT", "This member has left the group and may not transact.");

    /// <summary>The payout exceeds what the member has available.</summary>
    public static StokvelExceptions InsufficientAvailable()
        => new("STOKVEL_INSUFFICIENT_AVAILABLE", "The payout exceeds the member's available balance.");

    /// <summary>A stale balance may not settle offline.</summary>
    public static StokvelExceptions StaleBalanceNeedsConnectivity()
        => new("STOKVEL_STALE_BALANCE_NEEDS_CONNECTIVITY", "This payout was evaluated against a stale balance and requires connectivity.");

    /// <summary>The action needs an approval that has not been granted.</summary>
    public static StokvelExceptions ApprovalRequired()
        => new("STOKVEL_APPROVAL_REQUIRED", "This payout needs an approval before it can proceed.");

    /// <summary>A goods or hamper payout needs a basket to know what goods.</summary>
    public static StokvelExceptions BasketRequired()
        => new("STOKVEL_BASKET_REQUIRED", "A goods or hamper payout settles a hamper basket; pass one.");

    /// <summary>A caller may not read these rows.</summary>
    public static StokvelExceptions VisibilityDenied()
        => new("STOKVEL_VISIBILITY_DENIED", "This caller may not read these stokvel rows.");

    /// <summary>The receipt reference is already in use for this member.</summary>
    public static StokvelExceptions DuplicateReceipt()
        => new("STOKVEL_DUPLICATE_RECEIPT", "This receipt reference was already recorded for this member.");

    /// <summary>No company could be resolved for the operation.</summary>
    public static StokvelExceptions CompanyRequired()
        => new("STOKVEL_COMPANY_REQUIRED", "A company is required: pass one or bind an acting company.");

    /// <summary>The request names a different company than the scope holds.</summary>
    public static StokvelExceptions CompanyMismatch()
        => new("STOKVEL_COMPANY_MISMATCH", "The request names a different company than the scope already holds.");
}

/// <summary>A caller tried to read rows they may not see. Distinct from the coded refusal so the
/// visibility wall reads as a wall in a test.</summary>
public sealed class StokvelVisibilityException(string message)
    : DomainException("STOKVEL_VISIBILITY_DENIED", message)
{
}
