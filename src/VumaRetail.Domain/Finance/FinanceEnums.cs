namespace VumaRetail.Domain.Finance;

/// <summary>The five classes of account a chart of accounts is built from.</summary>
public enum AccountType
{
    /// <summary>What the business owns or is owed. Normal balance debit.</summary>
    Asset = 0,

    /// <summary>What the business owes. Normal balance credit.</summary>
    Liability = 1,

    /// <summary>The owners' residual claim. Normal balance credit.</summary>
    Equity = 2,

    /// <summary>Income earned. Normal balance credit.</summary>
    Revenue = 3,

    /// <summary>Costs incurred. Normal balance debit.</summary>
    Expense = 4,
}

/// <summary>Which side of a journal line increases an account's balance.</summary>
public enum NormalBalance
{
    /// <summary>A debit increases the balance.</summary>
    Debit = 0,

    /// <summary>A credit increases the balance.</summary>
    Credit = 1,
}

/// <summary>
/// Marks an account as the single point a sub-ledger reconciles to (ADR-016).
/// </summary>
/// <remarks>
/// The period-close variance check reads this to know which GL balance to compare each sub-ledger
/// against. <see cref="None"/> is the default for every ordinary account; only one account per
/// tenant should normally carry <see cref="AccountsReceivable"/> or <see cref="AccountsPayable"/>,
/// though the model does not forbid more for a tenant that genuinely runs separate control accounts
/// per currency or division.
/// </remarks>
public enum ControlAccountType
{
    /// <summary>An ordinary account. Not reconciled against a sub-ledger.</summary>
    None = 0,

    /// <summary>The GL balance this must equal the sum of open AR invoice balances.</summary>
    AccountsReceivable = 1,

    /// <summary>The GL balance this must equal the sum of open AP invoice balances.</summary>
    AccountsPayable = 2,

    /// <summary>The GL balance this must equal a bank account's reconciled balance.</summary>
    Bank = 3,
}

/// <summary>Whether an accounting period accepts postings.</summary>
public enum PeriodStatus
{
    /// <summary>Journals may post into this period.</summary>
    Open = 0,

    /// <summary>
    /// Closed. No further posting; the period's own control-account balances become the opening
    /// position for the next one.
    /// </summary>
    Closed = 1,
}

/// <summary>Where an AR or AP document sits in its own lifecycle.</summary>
public enum DocumentStatus
{
    /// <summary>Being built. Lines may still be added, changed or removed.</summary>
    Draft = 0,

    /// <summary>Posted to the GL. Lines are frozen; only allocation against it may still change.</summary>
    Posted = 1,

    /// <summary>Fully paid or fully allocated. No balance remains.</summary>
    Settled = 2,
}

/// <summary>Whether a tax rule's rate is applied on top of, or extracted from, the stated amount.</summary>
public enum TaxTreatment
{
    /// <summary>The stated amount already includes tax (CLAUDE.md §9's default for `en-ZA`).</summary>
    Inclusive = 0,

    /// <summary>The stated amount excludes tax; tax is added on top.</summary>
    Exclusive = 1,
}
