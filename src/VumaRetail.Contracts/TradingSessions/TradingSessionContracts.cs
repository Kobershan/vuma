namespace VumaRetail.Contracts.TradingSessions;

/// <summary>Opens a trading session at a shared till.</summary>
/// <param name="PremisesId">The premises the till stands on.</param>
/// <param name="TerminalId">The shared terminal.</param>
/// <param name="CashierUserId">The cashier.</param>
/// <param name="SessionCompanyId">The till's own company.</param>
/// <param name="Currency">ISO 4217 code for the whole basket.</param>
/// <param name="IdempotencyKey">Caller-minted key; replays return the existing session.</param>
/// <param name="CustomerGroupPartnerId">The customer, when identified.</param>
public sealed record OpenTradingSessionRequest(
    Guid PremisesId,
    Guid TerminalId,
    Guid CashierUserId,
    Guid SessionCompanyId,
    string Currency,
    string IdempotencyKey,
    Guid? CustomerGroupPartnerId = null);

/// <summary>Adds a scanned line to its company's segment.</summary>
/// <param name="Barcode">The scanned barcode (routing index decides the company).</param>
/// <param name="QuantityValue">How many.</param>
/// <param name="QuantityUom">In what unit.</param>
/// <param name="UnitPriceAmount">Shelf price, in <paramref name="Currency"/>.</param>
/// <param name="Currency">Must match the session.</param>
/// <param name="DiscountAmount">Line discount, default zero.</param>
/// <param name="TaxCode">Tax code, default STANDARD.</param>
/// <param name="LineId">Caller-minted id; replays return the existing line.</param>
public sealed record AddBasketLineRequest(
    string Barcode,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    string Currency,
    decimal DiscountAmount = 0m,
    string TaxCode = "STANDARD",
    Guid? LineId = null);

/// <summary>Captures the one tender against the session.</summary>
/// <param name="TenderType">Cash, Card, Voucher, MobileMoney or CustomerAccount.</param>
/// <param name="Amount">Must cover the basket gross.</param>
/// <param name="Currency">Must match the session.</param>
/// <param name="Reference">Card authorisation code or voucher serial.</param>
public sealed record CaptureTenderRequest(
    string TenderType,
    decimal Amount,
    string Currency,
    string? Reference = null);

/// <summary>One override entry: the company and its exact share.</summary>
public sealed record AllocationOverrideRequest(Guid CompanyId, decimal Amount, string Currency);

/// <summary>Replaces the proportional allocation with the cashier's exact split.</summary>
public sealed record OverrideAllocationRequest(IReadOnlyList<AllocationOverrideRequest> Allocations);

/// <summary>Abandons a session with a reason.</summary>
public sealed record VoidSessionRequest(string Reason);

/// <summary>One returned line: the session line and how much comes back.</summary>
public sealed record ReturnSessionLineRequest(Guid SessionLineId, decimal QuantityValue);

/// <summary>Returns lines from one origin company (ADR-128).</summary>
/// <param name="CompanyId">The origin company being credited.</param>
/// <param name="InvoiceNumber">The invoice the caller is crediting (must be that company's).</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="Lines">The session lines and quantities.</param>
public sealed record ReturnBasketLinesRequest(
    Guid CompanyId,
    string InvoiceNumber,
    string Reason,
    IReadOnlyList<ReturnSessionLineRequest> Lines);

/// <summary>One line on the session view.</summary>
public sealed record SessionLineResponse(
    Guid LineId,
    Guid CompanyId,
    string Barcode,
    string Description,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPrice,
    decimal Discount,
    string TaxCode,
    decimal Tax,
    decimal Net,
    decimal Gross,
    string PackSize,
    bool IsVoided);

/// <summary>One company segment on the session view.</summary>
public sealed record SessionSegmentResponse(
    Guid CompanyId,
    decimal Net,
    decimal Tax,
    decimal Gross,
    decimal? Allocation,
    string? AllocationBasis,
    string? InvoiceNumber,
    IReadOnlyList<SessionLineResponse> Lines);

/// <summary>The whole session: segments, per-company tax, allocation, invoices when done.</summary>
public sealed record TradingSessionResponse(
    Guid SessionId,
    string SessionNumber,
    Guid SessionCompanyId,
    string Currency,
    string Status,
    decimal Gross,
    string? TenderType,
    decimal? TenderAmount,
    string? FailureReason,
    IReadOnlyList<string> UnwoundInvoiceNumbers,
    IReadOnlyList<SessionSegmentResponse> Segments);

/// <summary>One posted segment.</summary>
public sealed record CompletedSegmentResponse(
    Guid CompanyId, Guid SaleId, Guid InvoiceId, string InvoiceNumber);

/// <summary>One invoice on the basket summary.</summary>
public sealed record SummaryInvoiceResponse(
    Guid CompanyId, Guid InvoiceId, string InvoiceNumber, decimal Subtotal);

/// <summary>The customer-facing summary. Not a fiscal document.</summary>
public sealed record BasketSummaryResponse(
    string SessionNumber,
    IReadOnlyList<SummaryInvoiceResponse> Invoices,
    decimal Total,
    string NotATaxInvoiceStatement);

/// <summary>A created document's id.</summary>
public sealed record TradingIdResponse(Guid Id);
