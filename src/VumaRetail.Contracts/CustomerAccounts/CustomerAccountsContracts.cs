namespace VumaRetail.Contracts.CustomerAccounts;

/// <summary>Opens a customer credit account.</summary>
/// <param name="PartnerId">The customer partner.</param>
/// <param name="CreditLimitAmount">The approved limit.</param>
/// <param name="CreditLimitCurrency">The ISO 4217 currency.</param>
/// <param name="TermsDays">Days to due date.</param>
/// <param name="CompanyId">The company, or null for the acting company.</param>
public sealed record CreateAccountRequest(
    Guid PartnerId,
    decimal CreditLimitAmount,
    string CreditLimitCurrency,
    int TermsDays,
    Guid? CompanyId = null);

/// <summary>An account, as returned by the API.</summary>
/// <param name="Id">The account.</param>
/// <param name="AccountNumber">The human-readable number.</param>
/// <param name="PartnerId">The customer partner.</param>
/// <param name="CreditLimit">The limit amount.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="TermsDays">Days to due date.</param>
/// <param name="Status">Active, OnHold or Closed.</param>
/// <param name="HoldReason">Why it was held, if it was.</param>
public sealed record AccountResponse(
    Guid Id,
    string AccountNumber,
    Guid PartnerId,
    decimal CreditLimit,
    string Currency,
    int TermsDays,
    string Status,
    string? HoldReason);

/// <summary>Sets a new credit limit (approval-gated).</summary>
/// <param name="Amount">The approved limit.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
public sealed record SetLimitRequest(decimal Amount, string Currency);

/// <summary>Holds an account (approval-gated).</summary>
/// <param name="Reason">Why. Required.</param>
public sealed record HoldRequest(string Reason);

/// <summary>Records a payment against the account.</summary>
/// <param name="Amount">What was paid.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Channel">Till, EFT, debit order, storefront.</param>
/// <param name="ReceiptReference">The caller's receipt reference.</param>
/// <param name="Allocations">Which invoices it settles. Must total the amount.</param>
public sealed record RecordPaymentRequest(
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    IReadOnlyList<PaymentAllocationRequest> Allocations);

/// <summary>One invoice a payment settles.</summary>
/// <param name="ArInvoiceId">The invoice.</param>
/// <param name="Amount">How much of it.</param>
public sealed record PaymentAllocationRequest(Guid ArInvoiceId, decimal Amount);

/// <summary>Authorises a buyer on a business account.</summary>
/// <param name="UserId">The buyer.</param>
/// <param name="DisplayName">The name for receipts and statements.</param>
/// <param name="ChargeLimitAmount">The per-charge cap.</param>
/// <param name="ChargeLimitCurrency">The ISO 4217 currency.</param>
public sealed record AuthoriseHolderRequest(
    Guid UserId,
    string DisplayName,
    decimal ChargeLimitAmount,
    string ChargeLimitCurrency);

/// <summary>An authorised buyer, as returned by the API.</summary>
/// <param name="Id">The holder row.</param>
/// <param name="UserId">The buyer.</param>
/// <param name="DisplayName">The name for receipts and statements.</param>
/// <param name="ChargeLimit">The per-charge cap.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
public sealed record AccountHolderResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    decimal ChargeLimit,
    string Currency);

/// <summary>One statement row.</summary>
/// <param name="Date">The document date.</param>
/// <param name="Description">What it is.</param>
/// <param name="Debit">What was charged.</param>
/// <param name="Credit">What was paid.</param>
/// <param name="RunningBalance">What is owed after this row.</param>
public sealed record StatementLineResponse(
    DateOnly Date,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>An account statement: open items with a running owed balance.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Lines">Ordered by document date.</param>
public sealed record StatementResponse(Guid AccountId, IReadOnlyList<StatementLineResponse> Lines);

/// <summary>One ageing bucket.</summary>
/// <param name="Bucket">Current, 30, 60, 90 or 120+.</param>
/// <param name="Balance">What is owed in it.</param>
public sealed record AgeingBucketResponse(string Bucket, decimal Balance);

/// <summary>An account's arrears position.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Buckets">Five buckets, always all present.</param>
public sealed record AgeingResponse(Guid AccountId, IReadOnlyList<AgeingBucketResponse> Buckets);

/// <summary>A tender-time credit answer.</summary>
/// <param name="Approved">Whether the tender may proceed.</param>
/// <param name="Available">What the account has.</param>
/// <param name="RefusalReason">Why not, when not.</param>
public sealed record CreditCheckResponse(bool Approved, decimal Available, string? RefusalReason);

/// <summary>One lay-by line to freeze.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="Uom">The unit the quantity is counted in.</param>
public sealed record LayByLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom);

/// <summary>Opens a lay-by agreement.</summary>
/// <param name="PartnerId">The customer.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Lines">At least one.</param>
/// <param name="DepositAmount">The first payment.</param>
/// <param name="DepositChannel">Where the deposit was taken.</param>
/// <param name="TermMonths">The payment window.</param>
/// <param name="LocationCode">The lay-by stock location code.</param>
/// <param name="CompanyId">The company, or null for the acting company.</param>
public sealed record OpenLayByRequest(
    Guid PartnerId,
    string Currency,
    IReadOnlyList<LayByLineRequest> Lines,
    decimal DepositAmount,
    string DepositChannel,
    int TermMonths,
    string LocationCode,
    Guid? CompanyId = null);

/// <summary>A lay-by line, as returned by the API.</summary>
/// <param name="Id">The line.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Uom">The unit.</param>
/// <param name="UnitPrice">The frozen unit price.</param>
/// <param name="Discount">The frozen discount.</param>
/// <param name="Tax">The frozen tax.</param>
/// <param name="Net">Unit price times quantity less discount.</param>
/// <param name="PackSize">The frozen pack size.</param>
public sealed record LayByLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    decimal Net,
    string PackSize);

/// <summary>A lay-by instalment, as returned by the API.</summary>
/// <param name="Sequence">One-based order.</param>
/// <param name="Amount">What was paid.</param>
/// <param name="ReceiptReference">The receipt.</param>
/// <param name="PaidAt">When, UTC.</param>
/// <param name="Channel">Where it was taken.</param>
/// <param name="TakenOffline">Whether captured offline.</param>
public sealed record LayByInstalmentResponse(
    int Sequence,
    decimal Amount,
    string ReceiptReference,
    DateTimeOffset PaidAt,
    string Channel,
    bool TakenOffline);

/// <summary>A lay-by agreement, as returned by the API.</summary>
/// <param name="Id">The agreement.</param>
/// <param name="AgreementNumber">The human-readable number.</param>
/// <param name="Status">Draft, Active, Completed, Cancelled or Expired.</param>
/// <param name="AgreedTotal">The frozen total.</param>
/// <param name="PaidToDate">What has been paid.</param>
/// <param name="Remaining">What is still owed.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="ExpiryDate">When it lapses, UTC.</param>
/// <param name="Lines">The frozen lines.</param>
/// <param name="Instalments">The payment history.</param>
public sealed record LayByResponse(
    Guid Id,
    string AgreementNumber,
    string Status,
    decimal AgreedTotal,
    decimal PaidToDate,
    decimal Remaining,
    string Currency,
    DateTimeOffset ExpiryDate,
    IReadOnlyList<LayByLineResponse> Lines,
    IReadOnlyList<LayByInstalmentResponse> Instalments);

/// <summary>Records an instalment.</summary>
/// <param name="Amount">What was paid.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Channel">Where it was taken.</param>
/// <param name="ReceiptReference">The receipt.</param>
/// <param name="TakenOffline">Whether captured offline.</param>
public sealed record RecordInstalmentRequest(
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    bool TakenOffline = false);
