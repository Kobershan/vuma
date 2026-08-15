using VumaRetail.Domain.Finance;

namespace VumaRetail.Application.Abstractions.Finance;

/// <summary>The chart of accounts.</summary>
public interface IAccountRepository
{
    /// <summary>Finds an account by id.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Account?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Finds an account by its code, unique per tenant.</summary>
    /// <param name="code">The account code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Account?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The account carrying a given control-account role, if the tenant has configured one.</summary>
    /// <param name="controlAccountType">Which sub-ledger to find the control account for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Account?> FindControlAccountAsync(
        ControlAccountType controlAccountType, CancellationToken cancellationToken = default);

    /// <summary>Every account marked as a control account — what the daily variance check walks.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Account>> ListControlAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every account in the chart of accounts, for a trial balance.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a new account.</summary>
    /// <param name="account">The account.</param>
    void Add(Account account);
}

/// <summary>The tenant's accounting calendar.</summary>
public interface IAccountingPeriodRepository
{
    /// <summary>Finds a period by id.</summary>
    /// <param name="periodId">The period.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AccountingPeriod?> FindByIdAsync(Guid periodId, CancellationToken cancellationToken = default);

    /// <summary>The open period covering a date, if one has been defined.</summary>
    /// <param name="date">The date to find a period for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AccountingPeriod?> FindOpenPeriodForDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Every currently open period, for the daily variance check.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<AccountingPeriod>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Defines a new period.</summary>
    /// <param name="period">The period.</param>
    void Add(AccountingPeriod period);
}

/// <summary>The immutable general ledger.</summary>
public interface IJournalRepository
{
    /// <summary>Finds a posted journal by id, with its lines.</summary>
    /// <param name="journalId">The journal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Journal?> FindByIdAsync(Guid journalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The GL balance of an account — the sum of its debits less its credits, across every posted
    /// journal line for the account.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <param name="asOf">Only journals posted on or before this date are included.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<decimal> GetAccountBalanceAsync(Guid accountId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>The most recently posted journals, newest first — the journal listing screen.</summary>
    /// <param name="limit">The maximum number to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Journal>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Records a new posted journal.</summary>
    /// <param name="journal">The journal.</param>
    void Add(Journal journal);
}

/// <summary>Tenant-configured mappings from a financial event type to GL postings.</summary>
public interface IPostingRuleRepository
{
    /// <summary>The active rule for an event type, if one is configured.</summary>
    /// <param name="eventType">The financial event type.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PostingRule?> FindActiveByEventTypeAsync(string eventType, CancellationToken cancellationToken = default);

    /// <summary>Records a new rule.</summary>
    /// <param name="rule">The rule.</param>
    void Add(PostingRule rule);
}

/// <summary>The AR sub-ledger's own documents.</summary>
public interface IArInvoiceRepository
{
    /// <summary>Finds an invoice by id, with its lines.</summary>
    /// <param name="invoiceId">The invoice.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ArInvoice?> FindByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Every posted invoice with a nonzero outstanding balance, for ageing and reconciliation.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ArInvoice>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a new invoice.</summary>
    /// <param name="invoice">The invoice.</param>
    void Add(ArInvoice invoice);
}

/// <summary>Customer receipts.</summary>
public interface IArReceiptRepository
{
    /// <summary>Finds a receipt by id, with its allocations.</summary>
    /// <param name="receiptId">The receipt.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ArReceipt?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);

    /// <summary>Records a new receipt.</summary>
    /// <param name="receipt">The receipt.</param>
    void Add(ArReceipt receipt);
}

/// <summary>The AP sub-ledger's own documents.</summary>
public interface IApInvoiceRepository
{
    /// <summary>Finds an invoice by id, with its lines.</summary>
    /// <param name="invoiceId">The invoice.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApInvoice?> FindByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Every posted invoice with a nonzero outstanding balance, for ageing and reconciliation.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApInvoice>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a new invoice.</summary>
    /// <param name="invoice">The invoice.</param>
    void Add(ApInvoice invoice);
}

/// <summary>Supplier payments.</summary>
public interface IApPaymentRepository
{
    /// <summary>Finds a payment by id, with its allocations.</summary>
    /// <param name="paymentId">The payment.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApPayment?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Records a new payment.</summary>
    /// <param name="payment">The payment.</param>
    void Add(ApPayment payment);
}

/// <summary>Bank accounts and their imported statement lines.</summary>
public interface IBankAccountRepository
{
    /// <summary>Finds a bank account by id.</summary>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BankAccount?> FindByIdAsync(Guid bankAccountId, CancellationToken cancellationToken = default);

    /// <summary>The bank account paired with a given GL control account, if one exists.</summary>
    /// <param name="glAccountId">The GL account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BankAccount?> FindByGlAccountIdAsync(Guid glAccountId, CancellationToken cancellationToken = default);

    /// <summary>Records a new bank account.</summary>
    /// <param name="account">The bank account.</param>
    void Add(BankAccount account);
}

/// <summary>Imported bank statement lines and their reconciliation state.</summary>
public interface IBankStatementLineRepository
{
    /// <summary>Finds a statement line by id.</summary>
    /// <param name="lineId">The line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BankStatementLine?> FindByIdAsync(Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if a line with this external reference has already been imported for this bank account —
    /// the batch import's de-duplication check.
    /// </summary>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="externalReference">The bank's own reference for the transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> ExistsAsync(Guid bankAccountId, string externalReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// The reconciled balance of a bank account: the sum of every matched statement line's amount.
    /// </summary>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<decimal> GetReconciledBalanceAsync(Guid bankAccountId, CancellationToken cancellationToken = default);

    /// <summary>Records a newly imported line.</summary>
    /// <param name="line">The line.</param>
    void Add(BankStatementLine line);
}

/// <summary>Tenant-configured tax rules.</summary>
public interface ITaxRuleRepository
{
    /// <summary>Finds a rule by id.</summary>
    /// <param name="taxRuleId">The rule.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TaxRule?> FindByIdAsync(Guid taxRuleId, CancellationToken cancellationToken = default);

    /// <summary>The rule effective for a code on a given date, if one is configured.</summary>
    /// <param name="code">The tax code.</param>
    /// <param name="asOf">The date it must be effective on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TaxRule?> FindEffectiveAsync(string code, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Records a new rule.</summary>
    /// <param name="rule">The rule.</param>
    void Add(TaxRule rule);
}

/// <summary>Where the daily reconciliation check leaves its evidence.</summary>
public interface IReconciliationVarianceFlagRepository
{
    /// <summary>Records one day's check.</summary>
    /// <param name="flag">The flag.</param>
    void Add(ReconciliationVarianceFlag flag);
}

/// <summary>
/// Issues gap-free, human-readable document numbers per series — <c>JNL</c>, <c>ARINV</c>,
/// <c>ARREC</c>, <c>APINV</c>, <c>APPAY</c>.
/// </summary>
/// <remarks>
/// A port rather than a formula on the entity because a number that survives concurrent posting
/// needs an atomic counter — a PostgreSQL sequence in Infrastructure's implementation — which the
/// domain and Application layers have no business knowing about.
/// </remarks>
public interface IDocumentNumberSequence
{
    /// <summary>The next number in the series, formatted <c>{series}-{6-digit sequence}</c>.</summary>
    /// <param name="series">Which counter to advance.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<string> NextAsync(string series, CancellationToken cancellationToken = default);
}
