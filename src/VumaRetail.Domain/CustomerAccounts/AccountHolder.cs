using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A named person allowed to charge to a business account, with their own limit and their own
/// trace on every transaction. The account in the till shows who charged, not just what was
/// charged — that is the whole point of the entity.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class AccountHolder : Entity
{
    private AccountHolder(
        Guid tenantId,
        Guid? storeId,
        Guid accountId,
        Guid userId,
        string displayName,
        Money chargeLimit)
        : base(tenantId, storeId)
    {
        AccountId = accountId;
        UserId = userId;
        DisplayName = displayName;
        ChargeLimit = chargeLimit;
    }

    private AccountHolder()
    {
    }

    /// <summary>The account this person may charge to.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The operator or login this buyer authenticates as.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Who the receipt and the statement name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The most this buyer may put on one charge.</summary>
    public Money ChargeLimit { get; private set; }

    /// <summary>Authorises a buyer on an account.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the account belongs to one.</param>
    /// <param name="accountId">The account.</param>
    /// <param name="userId">The buyer.</param>
    /// <param name="displayName">The name for receipts and statements.</param>
    /// <param name="chargeLimit">The per-charge cap. Must be positive.</param>
    public static AccountHolder Authorise(
        Guid tenantId,
        Guid? storeId,
        Guid accountId,
        Guid userId,
        string displayName,
        Money chargeLimit)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A holder must belong to a tenant.", nameof(tenantId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A holder must belong to an account.", nameof(accountId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A holder must be a person.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (chargeLimit.Amount <= 0m)
        {
            throw CustomerAccountExceptions.HolderLimitMustBePositive();
        }

        return new AccountHolder(
            tenantId, storeId, accountId, userId, displayName.Trim(), chargeLimit);
    }
}
