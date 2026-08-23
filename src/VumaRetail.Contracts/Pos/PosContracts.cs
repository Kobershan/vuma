namespace VumaRetail.Contracts.Pos;

/// <summary>Opens a shift at the calling terminal with a counted float.</summary>
/// <param name="OpeningFloat">The cash placed in the drawer before trading. May be zero.</param>
/// <param name="Currency">The ISO 4217 currency the drawer is counted in.</param>
public sealed record OpenTillSessionRequest(decimal OpeningFloat, string Currency);

/// <summary>Counts the drawer and closes the shift.</summary>
/// <param name="CountedCash">What was physically counted out of the drawer.</param>
/// <param name="Currency">The ISO 4217 currency it was counted in.</param>
/// <param name="Note">An optional explanation, which a non-zero variance usually wants.</param>
public sealed record CloseTillSessionRequest(decimal CountedCash, string Currency, string? Note = null);

/// <summary>The result of a cash-up.</summary>
/// <param name="ExpectedCash">What the system says should have been in the drawer. Derived, never entered.</param>
/// <param name="CountedCash">What was physically counted.</param>
/// <param name="Variance">Counted less expected. Negative is a shortfall.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
public sealed record CashUpResponse(
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    string Currency);

/// <summary>A till session, as returned by the API.</summary>
/// <param name="Id">The session's id.</param>
/// <param name="TerminalId">The terminal whose drawer it counts.</param>
/// <param name="OperatorUserId">The cashier who took the drawer.</param>
/// <param name="Status">Open or Closed.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="OpeningFloat">The float it opened with.</param>
/// <param name="OpenedAt">When it opened, UTC.</param>
/// <param name="ClosedAt">When it closed, or <c>null</c>.</param>
/// <param name="CountedCash">What was counted at close, or <c>null</c> while open.</param>
/// <param name="ExpectedCash">
/// What the drawer should hold. Derived on every read, so an open session reports its position now.
/// </param>
/// <param name="Variance">Counted less expected, or <c>null</c> while open.</param>
/// <param name="SalesCompleted">How many sales completed on this session.</param>
/// <param name="SalesUnfinished">How many are still open or parked. Must be zero before it can close.</param>
/// <param name="Note">The note recorded at close.</param>
public sealed record TillSessionResponse(
    Guid Id,
    Guid TerminalId,
    Guid OperatorUserId,
    string Status,
    string Currency,
    decimal OpeningFloat,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal? CountedCash,
    decimal ExpectedCash,
    decimal? Variance,
    int SalesCompleted,
    int SalesUnfinished,
    string? Note);

/// <summary>Opens a sale at the calling terminal's open till session.</summary>
/// <param name="SaleId">
/// The sale's identity, or <c>null</c> to mint one. A terminal replaying a sale it rang up offline
/// supplies the id it already printed; re-sending it returns the existing sale rather than a second one.
/// </param>
/// <param name="LocationId">The stock location the goods leave.</param>
/// <param name="CustomerId">The customer, when one was identified.</param>
/// <remarks>
/// Carries no currency (§4.13): a sale always trades in its till session's currency, which is itself
/// resolved from the store, then the tenant, when the session opened — never from a request body.
/// </remarks>
public sealed record OpenSaleRequest(
    Guid? SaleId,
    Guid LocationId,
    Guid? CustomerId = null);

/// <summary>Rings a line up on an open sale.</summary>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/>.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in. Must match the item's own.</param>
/// <param name="UnitPrice">What one unit is being sold for.</param>
/// <param name="Currency">The ISO 4217 currency. Must match the sale's.</param>
/// <param name="DiscountAmount">A manual discount off this line, or <c>null</c> for none.</param>
public sealed record AddSaleLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    string Currency,
    decimal? DiscountAmount = null);

/// <summary>Takes a payment against an open sale.</summary>
/// <param name="TenderType">Cash, Card, Voucher, MobileMoney or CustomerAccount.</param>
/// <param name="Amount">How much. Must be positive.</param>
/// <param name="Currency">The ISO 4217 currency. Must match the sale's.</param>
/// <param name="Reference">The card authorisation code or voucher serial, if any.</param>
public sealed record TenderSaleRequest(
    string TenderType,
    decimal Amount,
    string Currency,
    string? Reference = null);

/// <summary>Abandons a sale before it is paid for.</summary>
/// <param name="Reason">Why. Recorded — an abandoned sale is what shrinkage looks like from outside.</param>
public sealed record VoidSaleRequest(string Reason);

/// <summary>Records that a sale's receipt came out of a printer.</summary>
/// <param name="Reason">Why it was reprinted. Required once the receipt has been printed before.</param>
public sealed record RecordReceiptPrintRequest(string? Reason = null);

/// <summary>One line on a sale, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="LineNumber">Its position on the receipt.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What the receipt says, snapshotted at ring-up.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitOfMeasure">The unit it was sold in.</param>
/// <param name="UnitPrice">What one unit was sold for.</param>
/// <param name="DiscountAmount">The manual discount taken off.</param>
/// <param name="TaxCode">The tax code it was priced under.</param>
/// <param name="Net">The line excluding tax.</param>
/// <param name="Tax">The tax on the line.</param>
/// <param name="Gross">What the line came to.</param>
/// <param name="IsVoided">Whether the line has been taken off the sale.</param>
/// <param name="StockIssue">Pending, Posted or Refused.</param>
/// <param name="StockIssueNote">Why the stock issue was refused, when it was.</param>
public sealed record SaleLineResponse(
    Guid Id,
    int LineNumber,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal DiscountAmount,
    string TaxCode,
    decimal Net,
    decimal Tax,
    decimal Gross,
    bool IsVoided,
    string StockIssue,
    string? StockIssueNote);

/// <summary>One payment taken against a sale, as returned by the API.</summary>
/// <param name="Id">The tender's id.</param>
/// <param name="TenderType">How it was paid.</param>
/// <param name="Amount">How much.</param>
/// <param name="Reference">The card authorisation code or voucher serial, if any.</param>
/// <param name="CapturedAt">When the money changed hands, UTC.</param>
public sealed record SaleTenderResponse(
    Guid Id,
    string TenderType,
    decimal Amount,
    string? Reference,
    DateTimeOffset CapturedAt);

/// <summary>A sale, as returned by the API.</summary>
/// <param name="Id">The sale's id.</param>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="Status">Open, Parked, Completed or Voided.</param>
/// <param name="TillSessionId">The shift it was rung up on.</param>
/// <param name="TerminalId">The terminal.</param>
/// <param name="OperatorUserId">The operator.</param>
/// <param name="LocationId">The stock location the goods leave.</param>
/// <param name="CustomerId">The customer, if one was identified.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Net">The sale excluding tax.</param>
/// <param name="Tax">The tax.</param>
/// <param name="Gross">What the customer owes.</param>
/// <param name="AmountTendered">What has been taken.</param>
/// <param name="ChangeGiven">What was handed back.</param>
/// <param name="OpenedAt">When the first item was scanned, UTC.</param>
/// <param name="CompletedAt">When it completed, or <c>null</c>.</param>
/// <param name="VoidedAt">When it was abandoned, or <c>null</c>.</param>
/// <param name="VoidReason">Why it was abandoned.</param>
/// <param name="Lines">Every line, voided ones included.</param>
/// <param name="Tenders">Every payment taken.</param>
public sealed record SaleResponse(
    Guid Id,
    string SaleNumber,
    string Status,
    Guid TillSessionId,
    Guid TerminalId,
    Guid OperatorUserId,
    Guid LocationId,
    Guid? CustomerId,
    string Currency,
    decimal Net,
    decimal Tax,
    decimal Gross,
    decimal AmountTendered,
    decimal ChangeGiven,
    DateTimeOffset OpenedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    IReadOnlyList<SaleLineResponse> Lines,
    IReadOnlyList<SaleTenderResponse> Tenders);

/// <summary>What a completed sale hands back to the till.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="Gross">What was owed.</param>
/// <param name="AmountTendered">What was taken.</param>
/// <param name="ChangeGiven">What to hand back, in cash.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="StockIssuesRefused">
/// How many lines completed without relieving stock. Zero on a normal sale; anything else is a
/// reconciliation the store owes itself, listed at <c>/pos/reconciliation/stock-issues</c>.
/// </param>
public sealed record SaleCompletionResponse(
    Guid SaleId,
    string SaleNumber,
    decimal Gross,
    decimal AmountTendered,
    decimal ChangeGiven,
    string Currency,
    int StockIssuesRefused);

/// <summary>The id of a newly created POS record.</summary>
/// <param name="Id">The id.</param>
public sealed record PosIdResponse(Guid Id);

/// <summary>One line on a receipt.</summary>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitOfMeasure">The unit.</param>
/// <param name="UnitPrice">What one cost.</param>
/// <param name="DiscountAmount">The discount taken off.</param>
/// <param name="Gross">What the line came to.</param>
/// <param name="TaxCode">The tax code, which the tax summary keys on.</param>
public sealed record ReceiptLineResponse(
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal Gross,
    string TaxCode);

/// <summary>One rate's worth of tax, for the summary block a VAT invoice must carry.</summary>
/// <param name="TaxCode">The code.</param>
/// <param name="Net">The net at this rate.</param>
/// <param name="Tax">The tax at this rate.</param>
public sealed record ReceiptTaxLineResponse(string TaxCode, decimal Net, decimal Tax);

/// <summary>One payment, as it appears at the foot of the slip.</summary>
/// <param name="TenderType">How it was paid.</param>
/// <param name="Amount">How much.</param>
/// <param name="Reference">The card authorisation or voucher serial.</param>
public sealed record ReceiptTenderResponse(string TenderType, decimal Amount, string? Reference);

/// <summary>The receipt a sale produces.</summary>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="StoreName">The trading name at the top of the slip.</param>
/// <param name="StoreAddress">The store's address, formatted for printing.</param>
/// <param name="TaxNumber">The seller's VAT registration number.</param>
/// <param name="CompletedAt">When the sale completed, UTC.</param>
/// <param name="TerminalId">Which till.</param>
/// <param name="OperatorName">Who served the customer.</param>
/// <param name="Lines">What was bought.</param>
/// <param name="TaxLines">Tax broken out per rate.</param>
/// <param name="Net">The sale excluding tax.</param>
/// <param name="Tax">The tax.</param>
/// <param name="Gross">What was owed.</param>
/// <param name="Tenders">How it was paid.</param>
/// <param name="ChangeGiven">What was handed back.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="IsReprint">True when this receipt has been printed before.</param>
/// <param name="PlainText">
/// The slip laid out as fixed-width text, exactly as a thermal printer would set it. Included so a
/// caller that is not driving a printer — a screen preview, an emailed copy, a test — does not have to
/// reimplement the layout.
/// </param>
public sealed record ReceiptResponse(
    string SaleNumber,
    string StoreName,
    string? StoreAddress,
    string? TaxNumber,
    DateTimeOffset CompletedAt,
    Guid TerminalId,
    string? OperatorName,
    IReadOnlyList<ReceiptLineResponse> Lines,
    IReadOnlyList<ReceiptTaxLineResponse> TaxLines,
    decimal Net,
    decimal Tax,
    decimal Gross,
    IReadOnlyList<ReceiptTenderResponse> Tenders,
    decimal ChangeGiven,
    string Currency,
    bool IsReprint,
    string PlainText);

/// <summary>One printing of a receipt.</summary>
/// <param name="Id">The print's id.</param>
/// <param name="PrintedByUserId">Who printed it.</param>
/// <param name="TerminalId">Which terminal.</param>
/// <param name="IsReprint">True for every print after the first.</param>
/// <param name="Reason">Why it was reprinted.</param>
/// <param name="PrintedAt">When, UTC.</param>
public sealed record ReceiptPrintResponse(
    Guid Id,
    Guid PrintedByUserId,
    Guid TerminalId,
    bool IsReprint,
    string? Reason,
    DateTimeOffset PrintedAt);

/// <summary>What a scanned barcode turned out to be.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What to print on the receipt.</param>
/// <param name="UnitOfMeasure">The unit it sells in.</param>
/// <param name="TaxClassCode">The tax code it will be priced under.</param>
public sealed record SellableItemResponse(
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    string UnitOfMeasure,
    string TaxClassCode);

/// <summary>A sale line that completed without relieving stock.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleNumber">Its receipt number.</param>
/// <param name="SaleLineId">The line.</param>
/// <param name="LocationId">Where the stock should have come off.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How much was sold and not relieved.</param>
/// <param name="UnitOfMeasure">The unit.</param>
/// <param name="Reason">Why the ledger refused it.</param>
/// <param name="CompletedAt">When the sale completed.</param>
public sealed record RefusedStockIssueResponse(
    Guid SaleId,
    string SaleNumber,
    Guid SaleLineId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    string Reason,
    DateTimeOffset? CompletedAt);
