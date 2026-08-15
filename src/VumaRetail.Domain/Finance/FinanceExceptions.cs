using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>Raised when a journal's debits and credits do not agree, in one or more currencies.</summary>
/// <param name="variancePerCurrency">The out-of-balance amount per currency; each should be zero.</param>
public sealed class JournalNotBalancedException(IReadOnlyDictionary<string, decimal> variancePerCurrency)
    : DomainException(
        "FINANCE_JOURNAL_NOT_BALANCED",
        "This journal's debits and credits do not agree: "
        + string.Join(", ", variancePerCurrency.Select(pair => $"{pair.Key} {pair.Value:F4}")) + ".")
{
    /// <summary>The out-of-balance amount per currency.</summary>
    public IReadOnlyDictionary<string, decimal> VariancePerCurrency { get; } = variancePerCurrency;
}

/// <summary>Raised when a journal is posted with no lines at all.</summary>
public sealed class JournalHasNoLinesException()
    : DomainException("FINANCE_JOURNAL_HAS_NO_LINES", "A journal must have at least one line.");

/// <summary>
/// Raised when a journal line carries both a debit and a credit, or neither.
/// </summary>
/// <remarks>
/// A <see cref="DomainException"/> rather than the <see cref="ArgumentException"/>
/// <c>JournalLine.Create</c> raises for the same rule: a manual journal arrives from the API, so a
/// line with no amounts on it is a caller mistake that deserves a coded 400, not an unhandled 500.
/// <c>Journal.Post</c> checks every draft line before it computes any balance — the balance loop has
/// to read one side or the other to do its arithmetic, so validating afterwards would mean
/// dereferencing an amount that is not there.
/// </remarks>
/// <param name="lineNumber">The offending line's position in the posting, one-based.</param>
/// <param name="problem">What is wrong with it.</param>
public sealed class JournalLineSideException(int lineNumber, string problem)
    : DomainException(
        "FINANCE_JOURNAL_LINE_SIDE",
        $"Journal line {lineNumber} {problem}. Every line carries exactly one of a debit or a credit.",
        DomainProblemKind.Malformed);

/// <summary>Raised when a period is closed a second time.</summary>
/// <param name="periodId">The period.</param>
public sealed class PeriodAlreadyClosedException(Guid periodId)
    : DomainException("FINANCE_PERIOD_ALREADY_CLOSED", $"Accounting period {periodId} is already closed.");

/// <summary>Raised when a journal is posted against a date that falls in a closed or missing period.</summary>
/// <param name="date">The posting date that has no open period.</param>
public sealed class NoOpenPeriodException(DateOnly date)
    : DomainException(
        "FINANCE_NO_OPEN_PERIOD",
        $"There is no open accounting period covering {date:yyyy-MM-dd}.",
        DomainProblemKind.Rule);

/// <summary>
/// Raised when a period close is refused because a control account disagrees with its sub-ledger.
/// </summary>
/// <param name="variances">One entry per control account with a nonzero variance.</param>
public sealed class PeriodCloseBlockedException(IReadOnlyList<ControlAccountVariance> variances)
    : DomainException(
        "FINANCE_PERIOD_CLOSE_BLOCKED",
        "This period cannot close: " + string.Join("; ", variances.Select(v =>
            $"{v.ControlAccountType} account {v.AccountId} is {v.GlBalance:F4} but its sub-ledger is "
            + $"{v.SubLedgerBalance:F4}")) + ".")
{
    /// <summary>The unreconciled control accounts.</summary>
    public IReadOnlyList<ControlAccountVariance> Variances { get; } = variances;
}

/// <summary>One control account's disagreement with its sub-ledger.</summary>
/// <param name="AccountId">The GL control account.</param>
/// <param name="ControlAccountType">Which sub-ledger it should reconcile to.</param>
/// <param name="GlBalance">The account's balance per the general ledger.</param>
/// <param name="SubLedgerBalance">The balance the sub-ledger itself reports.</param>
public sealed record ControlAccountVariance(
    Guid AccountId,
    ControlAccountType ControlAccountType,
    decimal GlBalance,
    decimal SubLedgerBalance)
{
    /// <summary>The difference. Zero means reconciled.</summary>
    public decimal Variance => GlBalance - SubLedgerBalance;
}

/// <summary>Raised when a write is attempted against an AR/AP document that is no longer a draft.</summary>
/// <param name="documentType">The document's kind, for example <c>ArInvoice</c>.</param>
/// <param name="documentId">The document.</param>
public sealed class DocumentNotDraftException(string documentType, Guid documentId)
    : DomainException(
        "FINANCE_DOCUMENT_NOT_DRAFT",
        $"{documentType} {documentId} is no longer a draft and cannot be changed. "
        + "Correct it with a credit or debit note instead.");

/// <summary>Raised when a document with no lines is posted.</summary>
/// <param name="documentType">The document's kind.</param>
public sealed class DocumentHasNoLinesException(string documentType)
    : DomainException("FINANCE_DOCUMENT_HAS_NO_LINES", $"{documentType} must have at least one line before it can be posted.");

/// <summary>Raised when an allocation would take a receipt or payment past what remains unallocated.</summary>
/// <param name="requested">What the caller asked to allocate.</param>
/// <param name="available">What remains unallocated.</param>
public sealed class OverAllocationException(Money requested, Money available)
    : DomainException(
        "FINANCE_OVER_ALLOCATION",
        $"Cannot allocate {requested}: only {available} remains unallocated.");

/// <summary>Raised when the posting rules engine has no active rule for a raised event type.</summary>
/// <param name="eventType">The event type nobody configured a rule for.</param>
public sealed class PostingRuleNotFoundException(string eventType)
    : DomainException(
        "FINANCE_POSTING_RULE_NOT_FOUND",
        $"No active posting rule maps event type '{eventType}' to any GL account. "
        + "Configure one before this event can post.",
        DomainProblemKind.NotFound);

/// <summary>Raised when a posting rule line names an amount key the raised event does not carry.</summary>
/// <param name="eventType">The event type.</param>
/// <param name="amountKey">The missing key.</param>
public sealed class UnknownFinancialEventAmountException(string eventType, string amountKey)
    : DomainException(
        "FINANCE_UNKNOWN_EVENT_AMOUNT",
        $"Posting rule for '{eventType}' references amount '{amountKey}', which the event does not carry.");

/// <summary>Raised when an account code that must be unique per tenant is already in use.</summary>
/// <param name="code">The code already in use.</param>
public sealed class AccountCodeAlreadyInUseException(string code)
    : DomainException(
        "FINANCE_ACCOUNT_CODE_IN_USE", $"Account code '{code}' is already in use.", DomainProblemKind.Conflict);

/// <summary>Raised when a command references an account that does not exist.</summary>
/// <param name="accountId">The missing account.</param>
public sealed class AccountNotFoundException(Guid accountId)
    : DomainException("FINANCE_ACCOUNT_NOT_FOUND", $"Account {accountId} does not exist.", DomainProblemKind.NotFound);

/// <summary>Raised when a command references a document that does not exist.</summary>
/// <param name="documentType">The document's kind.</param>
/// <param name="documentId">The missing document.</param>
public sealed class FinanceDocumentNotFoundException(string documentType, Guid documentId)
    : DomainException(
        "FINANCE_DOCUMENT_NOT_FOUND", $"{documentType} {documentId} does not exist.", DomainProblemKind.NotFound);

/// <summary>Raised when no active tax rule matches a code as of a given date.</summary>
/// <param name="taxCode">The tax code.</param>
/// <param name="asOf">The date it was needed for.</param>
public sealed class TaxRuleNotFoundException(string taxCode, DateOnly asOf)
    : DomainException(
        "FINANCE_TAX_RULE_NOT_FOUND",
        $"No active tax rule '{taxCode}' is effective on {asOf:yyyy-MM-dd}.",
        DomainProblemKind.NotFound);
