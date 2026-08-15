namespace VumaRetail.Contracts.Finance;

/// <summary>Opens a new chart-of-accounts entry.</summary>
/// <param name="Code">The account code, unique per tenant.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">One of <c>Asset</c>, <c>Liability</c>, <c>Equity</c>, <c>Revenue</c>, <c>Expense</c>.</param>
/// <param name="Currency">The ISO 4217 currency it is denominated in.</param>
/// <param name="ControlAccountType">
/// <c>None</c>, <c>AccountsReceivable</c>, <c>AccountsPayable</c> or <c>Bank</c>.
/// </param>
/// <param name="ParentAccountId">The parent account, for a hierarchy.</param>
public sealed record CreateAccountRequest(
    string Code,
    string Name,
    string Type,
    string Currency,
    string ControlAccountType = "None",
    Guid? ParentAccountId = null);

/// <summary>An account, as returned by the API.</summary>
/// <param name="Id">The account.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">Which of the five classes it belongs to.</param>
/// <param name="ControlAccountType">What sub-ledger, if any, this reconciles to.</param>
/// <param name="Currency">The ISO 4217 currency it is denominated in.</param>
/// <param name="IsActive">Whether the account may still be posted to.</param>
public sealed record AccountResponse(
    Guid Id, string Code, string Name, string Type, string ControlAccountType, string Currency, bool IsActive);

/// <summary>A newly created account's id.</summary>
/// <param name="Id">The account.</param>
public sealed record AccountIdResponse(Guid Id);

/// <summary>Opens a new accounting period.</summary>
/// <param name="PeriodStart">The first day, inclusive.</param>
/// <param name="PeriodEnd">The last day, inclusive.</param>
public sealed record OpenAccountingPeriodRequest(DateOnly PeriodStart, DateOnly PeriodEnd);

/// <summary>A newly opened period's id.</summary>
/// <param name="Id">The period.</param>
public sealed record AccountingPeriodIdResponse(Guid Id);

/// <summary>One control account's disagreement (or agreement) with its sub-ledger.</summary>
/// <param name="AccountId">The control account.</param>
/// <param name="ControlAccountType">Which sub-ledger it was checked against.</param>
/// <param name="GlBalance">The GL balance.</param>
/// <param name="SubLedgerBalance">The sub-ledger's own balance.</param>
/// <param name="Variance">The difference. Zero means reconciled.</param>
public sealed record ControlAccountVarianceResponse(
    Guid AccountId, string ControlAccountType, decimal GlBalance, decimal SubLedgerBalance, decimal Variance);

/// <summary>One line of a manual journal, before it is posted.</summary>
/// <param name="AccountId">The GL account.</param>
/// <param name="Debit">The debit amount, or <c>null</c> if this line is a credit.</param>
/// <param name="Credit">The credit amount, or <c>null</c> if this line is a debit.</param>
/// <param name="Description">A line-level narration.</param>
/// <param name="DepartmentId">The department analysis dimension, bare opaque id.</param>
/// <param name="CostCentreId">The cost-centre analysis dimension, bare opaque id.</param>
/// <param name="ProjectId">The project analysis dimension, bare opaque id.</param>
/// <param name="ChannelId">The channel analysis dimension, bare opaque id.</param>
/// <param name="EmployeeId">The employee analysis dimension, bare opaque id.</param>
public sealed record ManualJournalLineRequest(
    Guid AccountId,
    decimal? Debit,
    decimal? Credit,
    string Description = "",
    Guid? DepartmentId = null,
    Guid? CostCentreId = null,
    Guid? ProjectId = null,
    Guid? ChannelId = null,
    Guid? EmployeeId = null);

/// <summary>Posts a journal an accountant entered directly.</summary>
/// <param name="PostingDate">The date to post into. Must fall inside an open period.</param>
/// <param name="Currency">The ISO 4217 currency every line is denominated in.</param>
/// <param name="Lines">The journal's lines. Must balance.</param>
/// <param name="Narration">A narration for the journal as a whole.</param>
/// <param name="Reference">A free-text reference, for example a source document number.</param>
public sealed record PostManualJournalRequest(
    DateOnly PostingDate,
    string Currency,
    IReadOnlyList<ManualJournalLineRequest> Lines,
    string Narration = "",
    string Reference = "");

/// <summary>Reverses a posted journal.</summary>
/// <param name="Reason">Why it is being reversed.</param>
public sealed record ReverseJournalRequest(string Reason);

/// <summary>A newly posted journal's id.</summary>
/// <param name="Id">The journal.</param>
public sealed record JournalIdResponse(Guid Id);

/// <summary>One journal, summarised for a listing.</summary>
/// <param name="Id">The journal.</param>
/// <param name="JournalNumber">Its human-readable number.</param>
/// <param name="PostedAt">When it posted, UTC.</param>
/// <param name="SourceModule">The raising module, or <c>manual</c>.</param>
/// <param name="SourceEventType">The financial event type, or <c>manual</c>.</param>
/// <param name="Narration">The journal's narration.</param>
/// <param name="LineCount">How many lines it carries.</param>
public sealed record JournalSummaryResponse(
    Guid Id, string JournalNumber, DateTimeOffset PostedAt, string SourceModule, string SourceEventType,
    string Narration, int LineCount);

/// <summary>One line of a journal, as read for the journal detail screen.</summary>
/// <param name="LineNumber">The line's position.</param>
/// <param name="AccountId">The GL account.</param>
/// <param name="Debit">The debit amount, or <c>null</c> if this line is a credit.</param>
/// <param name="Credit">The credit amount, or <c>null</c> if this line is a debit.</param>
/// <param name="Description">A line-level narration.</param>
public sealed record JournalLineResponse(int LineNumber, Guid AccountId, decimal? Debit, decimal? Credit, string Description);

/// <summary>A journal in full, with its lines.</summary>
/// <param name="Id">The journal.</param>
/// <param name="JournalNumber">Its human-readable number.</param>
/// <param name="PostedAt">When it posted, UTC.</param>
/// <param name="PostedBy">Who or what posted it.</param>
/// <param name="SourceModule">The raising module, or <c>manual</c>.</param>
/// <param name="SourceEventType">The financial event type, or <c>manual</c>.</param>
/// <param name="SourceReference">A reference back to the source document.</param>
/// <param name="Narration">The journal's narration.</param>
/// <param name="ReversalOfJournalId">The journal this one reverses, if any.</param>
/// <param name="Lines">The journal's lines.</param>
public sealed record JournalDetailResponse(
    Guid Id, string JournalNumber, DateTimeOffset PostedAt, string PostedBy, string SourceModule,
    string SourceEventType, string SourceReference, string Narration, Guid? ReversalOfJournalId,
    IReadOnlyList<JournalLineResponse> Lines);

/// <summary>One row of a trial balance.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">Which of the five classes it belongs to.</param>
/// <param name="Debit">The debit balance, or zero if the account carries a credit balance.</param>
/// <param name="Credit">The credit balance, or zero if the account carries a debit balance.</param>
public sealed record TrialBalanceLineResponse(Guid AccountId, string Code, string Name, string Type, decimal Debit, decimal Credit);

/// <summary>One posting line to add to a new posting rule.</summary>
/// <param name="AccountId">The GL account this line posts to.</param>
/// <param name="Side">Either <c>Debit</c> or <c>Credit</c>.</param>
/// <param name="AmountKey">Which named amount on the incoming event this line takes its value from.</param>
/// <param name="InheritDimensions">Whether this line copies the event's analysis dimensions.</param>
/// <param name="Description">A line-level narration template.</param>
public sealed record PostingRuleLineRequest(
    Guid AccountId, string Side, string AmountKey, bool InheritDimensions = true, string Description = "");

/// <summary>Configures how a financial event type is turned into a balanced journal.</summary>
/// <param name="EventType">The financial event type this rule answers, for example <c>ar.invoice.posted</c>.</param>
/// <param name="Lines">The postings the rule produces, in order.</param>
/// <param name="Description">A description for the person configuring it.</param>
public sealed record DefinePostingRuleRequest(
    string EventType, IReadOnlyList<PostingRuleLineRequest> Lines, string Description = "");

/// <summary>A newly defined posting rule's id.</summary>
/// <param name="Id">The rule.</param>
public sealed record PostingRuleIdResponse(Guid Id);

/// <summary>One line of a customer invoice being drafted.</summary>
/// <param name="Description">What is being billed.</param>
/// <param name="Amount">The amount as entered — net or gross depends on the tax code's own configured treatment.</param>
/// <param name="TaxCode">The tax rule code to apply, or empty for none.</param>
public sealed record ArInvoiceLineRequest(string Description, decimal Amount, string TaxCode = "");

/// <summary>Opens a draft customer invoice with its lines.</summary>
/// <param name="PartnerId">The customer, by bare id.</param>
/// <param name="InvoiceDate">The date on the invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The ISO 4217 currency for the whole document.</param>
/// <param name="Lines">The invoice's lines.</param>
public sealed record CreateArInvoiceRequest(
    Guid PartnerId, DateOnly InvoiceDate, DateOnly DueDate, string Currency, IReadOnlyList<ArInvoiceLineRequest> Lines);

/// <summary>A newly created AR invoice's id.</summary>
/// <param name="Id">The invoice.</param>
public sealed record ArInvoiceIdResponse(Guid Id);

/// <summary>One open customer invoice, as read for a listing.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="PartnerId">The customer.</param>
/// <param name="InvoiceNumber">The invoice number.</param>
/// <param name="InvoiceDate">The date on the invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The invoice's currency.</param>
/// <param name="Total">The invoice total including tax.</param>
/// <param name="OutstandingBalance">What remains unpaid.</param>
/// <param name="Status">Where the invoice sits in its lifecycle.</param>
public sealed record ArInvoiceResponse(
    Guid Id, Guid PartnerId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate,
    string Currency, decimal Total, decimal OutstandingBalance, string Status);

/// <summary>One allocation of a customer receipt against one invoice.</summary>
/// <param name="ArInvoiceId">The invoice.</param>
/// <param name="Amount">How much of the receipt is allocated to it.</param>
public sealed record ArReceiptAllocationRequest(Guid ArInvoiceId, decimal Amount);

/// <summary>Records a payment received from a customer and allocates it against invoices.</summary>
/// <param name="PartnerId">The customer, by bare id.</param>
/// <param name="Currency">The receipt's currency.</param>
/// <param name="Allocations">Which invoices the receipt is allocated to, and how much each.</param>
/// <param name="BankAccountId">The bank account the funds landed in, if known.</param>
public sealed record RecordArReceiptRequest(
    Guid PartnerId, string Currency, IReadOnlyList<ArReceiptAllocationRequest> Allocations, Guid? BankAccountId = null);

/// <summary>A newly recorded AR receipt's id.</summary>
/// <param name="Id">The receipt.</param>
public sealed record ArReceiptIdResponse(Guid Id);

/// <summary>One line of a supplier invoice being captured.</summary>
/// <param name="Description">What was billed.</param>
/// <param name="Amount">The amount as entered — net or gross depends on the tax code's own configured treatment.</param>
/// <param name="TaxCode">The tax rule code to apply, or empty for none.</param>
public sealed record ApInvoiceLineRequest(string Description, decimal Amount, string TaxCode = "");

/// <summary>Captures a draft supplier invoice with its lines.</summary>
/// <param name="PartnerId">The supplier, by bare id.</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on the supplier's invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The ISO 4217 currency for the whole document.</param>
/// <param name="Lines">The invoice's lines.</param>
public sealed record CreateApInvoiceRequest(
    Guid PartnerId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate,
    string Currency, IReadOnlyList<ApInvoiceLineRequest> Lines);

/// <summary>A newly captured AP invoice's id.</summary>
/// <param name="Id">The invoice.</param>
public sealed record ApInvoiceIdResponse(Guid Id);

/// <summary>One open supplier invoice, as read for a listing.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on the supplier's invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The invoice's currency.</param>
/// <param name="Total">The invoice total including tax.</param>
/// <param name="OutstandingBalance">What remains unpaid.</param>
/// <param name="Status">Where the invoice sits in its lifecycle.</param>
public sealed record ApInvoiceResponse(
    Guid Id, Guid PartnerId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate,
    string Currency, decimal Total, decimal OutstandingBalance, string Status);

/// <summary>One allocation of a supplier payment against one invoice.</summary>
/// <param name="ApInvoiceId">The invoice.</param>
/// <param name="Amount">How much of the payment is allocated to it.</param>
public sealed record ApPaymentAllocationRequest(Guid ApInvoiceId, decimal Amount);

/// <summary>Records a payment made to a supplier and allocates it against invoices.</summary>
/// <param name="PartnerId">The supplier, by bare id.</param>
/// <param name="Currency">The payment's currency.</param>
/// <param name="Allocations">Which invoices the payment is allocated to, and how much each.</param>
/// <param name="BankAccountId">The bank account the funds were paid from, if known.</param>
public sealed record RecordApPaymentRequest(
    Guid PartnerId, string Currency, IReadOnlyList<ApPaymentAllocationRequest> Allocations, Guid? BankAccountId = null);

/// <summary>A newly recorded AP payment's id.</summary>
/// <param name="Id">The payment.</param>
public sealed record ApPaymentIdResponse(Guid Id);

/// <summary>One partner's ageing, bucketed by how overdue each open invoice is.</summary>
/// <param name="PartnerId">The customer or supplier.</param>
/// <param name="Current">Not yet due.</param>
/// <param name="Days30">1–30 days overdue.</param>
/// <param name="Days60">31–60 days overdue.</param>
/// <param name="Days90">61–90 days overdue.</param>
/// <param name="Days90Plus">More than 90 days overdue.</param>
/// <param name="Total">The sum of every bucket.</param>
public sealed record AgeingRowResponse(
    Guid PartnerId, decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus, decimal Total);

/// <summary>Opens a bank account record, paired with its GL control account.</summary>
/// <param name="GlAccountId">The GL account this reconciles to.</param>
/// <param name="Name">A label for the account.</param>
/// <param name="AccountNumber">The account number.</param>
/// <param name="Currency">The ISO 4217 currency it is held in.</param>
public sealed record CreateBankAccountRequest(Guid GlAccountId, string Name, string AccountNumber, string Currency);

/// <summary>A newly opened bank account's id.</summary>
/// <param name="Id">The bank account.</param>
public sealed record BankAccountIdResponse(Guid Id);

/// <summary>One statement line to import.</summary>
/// <param name="TransactionDate">The date the bank posted the transaction.</param>
/// <param name="Description">The bank's own description.</param>
/// <param name="Amount">The signed amount: positive for money in, negative for money out.</param>
/// <param name="ExternalReference">The bank's own reference, for de-duplication.</param>
public sealed record BankStatementLineRequest(
    DateOnly TransactionDate, string Description, decimal Amount, string ExternalReference);

/// <summary>Imports a batch of bank statement lines.</summary>
/// <param name="BankAccountId">The bank account the lines belong to.</param>
/// <param name="Lines">The lines to import.</param>
public sealed record ImportBankStatementLinesRequest(Guid BankAccountId, IReadOnlyList<BankStatementLineRequest> Lines);

/// <summary>How many lines a batch import actually added.</summary>
/// <param name="Imported">Lines added, excluding duplicates already on file.</param>
public sealed record ImportBankStatementLinesResponse(int Imported);

/// <summary>Matches an imported statement line against a GL journal line.</summary>
/// <param name="JournalLineId">The journal line it corresponds to.</param>
public sealed record MatchBankStatementLineRequest(Guid JournalLineId);

/// <summary>Compares a bank account's GL balance against its reconciled (matched) statement lines.</summary>
/// <param name="BankAccountId">The bank account.</param>
/// <param name="GlBalance">The GL balance, as of today.</param>
/// <param name="ReconciledBalance">The sum of every matched statement line.</param>
/// <param name="Variance">The difference. Zero means fully reconciled.</param>
public sealed record BankReconciliationSummaryResponse(
    Guid BankAccountId, decimal GlBalance, decimal ReconciledBalance, decimal Variance);

/// <summary>Defines a new tax rule.</summary>
/// <param name="Code">The code documents reference, for example <c>STANDARD</c>.</param>
/// <param name="Name">A description for the person configuring it.</param>
/// <param name="Rate">The rate, as a fraction — <c>0.15</c> for 15%.</param>
/// <param name="Treatment">Either <c>Inclusive</c> or <c>Exclusive</c>.</param>
/// <param name="EffectiveFrom">The first date this rule applies from.</param>
/// <param name="EffectiveTo">The last date this rule applies to, or <c>null</c> if open-ended.</param>
public sealed record CreateTaxRuleRequest(
    string Code, string Name, decimal Rate, string Treatment, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);

/// <summary>A newly defined tax rule's id.</summary>
/// <param name="Id">The rule.</param>
public sealed record TaxRuleIdResponse(Guid Id);

/// <summary>What a tax code and amount would calculate to.</summary>
/// <param name="TaxCode">The rule's code.</param>
/// <param name="NetAmount">The amount excluding tax.</param>
/// <param name="TaxAmount">The tax.</param>
/// <param name="GrossAmount">Net plus tax.</param>
/// <param name="Rate">The rate that was applied, as a fraction.</param>
public sealed record TaxCalculationResponse(string TaxCode, decimal NetAmount, decimal TaxAmount, decimal GrossAmount, decimal Rate);
