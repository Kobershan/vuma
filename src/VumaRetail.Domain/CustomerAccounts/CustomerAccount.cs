using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A customer credit account: the limit, terms and standing that Stage 07's AR sub-ledger trades
/// against. The account itself holds no money — invoices, receipts and allocations live in
/// <c>finance.ar_invoices</c> / <c>ar_receipts</c>; exposure is always computed, never stored, so
/// the limit check and the ledger cannot disagree about what is owed.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class CustomerAccount : Entity
{
    private CustomerAccount(
        Guid tenantId,
        Guid? storeId,
        string accountNumber,
        Guid partnerId,
        Money creditLimit,
        int termsDays,
        AccountStatus status)
        : base(tenantId, storeId)
    {
        AccountNumber = accountNumber;
        PartnerId = partnerId;
        CreditLimit = creditLimit;
        TermsDays = termsDays;
        Status = status;
    }

    private CustomerAccount()
    {
    }

    /// <summary>The human-readable number, series <c>ACT</c>, per company.</summary>
    public string AccountNumber { get; private set; } = string.Empty;

    /// <summary>The account holder. A bare id, never a cross-schema key (CONVENTIONS.md §2).</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The most the customer may owe at any instant, AR outstanding plus queued offline.</summary>
    public Money CreditLimit { get; private set; }

    /// <summary>Days from invoice date to due date.</summary>
    public int TermsDays { get; private set; }

    /// <summary>Whether the account trades, is frozen, or is closed.</summary>
    public AccountStatus Status { get; private set; }

    /// <summary>Why the account was held. Null unless held.</summary>
    public string? HoldReason { get; private set; }

    /// <summary>Opens an account for a customer partner.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the account belongs to one.</param>
    /// <param name="accountNumber">The next number in the company's <c>ACT</c> series.</param>
    /// <param name="partnerId">The customer partner. Must not be empty.</param>
    /// <param name="creditLimit">The approved limit. Must be positive.</param>
    /// <param name="termsDays">Days to due date. Must be positive.</param>
    /// <param name="companyId">The owning company, stamped for exports and projections.</param>
    public static CustomerAccount Open(
        Guid tenantId,
        Guid? storeId,
        string accountNumber,
        Guid partnerId,
        Money creditLimit,
        int termsDays,
        Guid? companyId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An account must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        if (partnerId == Guid.Empty)
        {
            throw new ArgumentException("An account must have a customer partner.", nameof(partnerId));
        }

        if (creditLimit.Amount <= 0m)
        {
            throw CustomerAccountExceptions.LimitMustBePositive();
        }

        if (termsDays <= 0)
        {
            throw CustomerAccountExceptions.TermsMustBePositive();
        }

        var account = new CustomerAccount(
            tenantId, storeId, accountNumber.Trim(), partnerId, creditLimit, termsDays,
            AccountStatus.Active);

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            account.AssignCompany(companyId.Value);
        }

        return account;
    }

    /// <summary>Replaces the limit. Approval is enforced by the command, not here.</summary>
    /// <param name="newLimit">The approved limit. Must be positive.</param>
    public void SetLimit(Money newLimit)
    {
        if (newLimit.Amount <= 0m)
        {
            throw CustomerAccountExceptions.LimitMustBePositive();
        }

        CreditLimit = newLimit;
    }

    /// <summary>Freezes the account: tender refuses until release.</summary>
    /// <param name="reason">Why it was held. Required — a silent hold is how disputes start.</param>
    public void Hold(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == AccountStatus.Closed)
        {
            throw CustomerAccountExceptions.ClosedAccountCannotHold();
        }

        Status = AccountStatus.OnHold;
        HoldReason = reason.Trim();
    }

    /// <summary>Unfreezes the account.</summary>
    public void Release()
    {
        if (Status == AccountStatus.Closed)
        {
            throw CustomerAccountExceptions.ClosedAccountCannotRelease();
        }

        Status = AccountStatus.Active;
        HoldReason = null;
    }

    /// <summary>Permanently closes the account. Balances still collect; nothing new charges.</summary>
    public void Close()
    {
        Status = AccountStatus.Closed;
    }

    /// <summary>Throws unless the account may take a new charge right now.</summary>
    public void EnsureChargeable()
    {
        if (Status != AccountStatus.Active)
        {
            throw CustomerAccountExceptions.AccountNotChargeable(Status);
        }
    }
}
