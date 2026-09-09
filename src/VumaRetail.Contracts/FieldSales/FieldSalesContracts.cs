namespace VumaRetail.Contracts.FieldSales;

/// <summary>One captured line: caller prices, snapshots frozen at capture.</summary>
public sealed record ProFormaLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    decimal DiscountAmount,
    string TaxCode,
    string Currency);

/// <summary>Captures a pro forma order.</summary>
public sealed record CaptureProFormaRequest(
    Guid RepId,
    Guid CompanyId,
    Guid PartnerId,
    string Currency,
    string IdempotencyKey,
    IReadOnlyList<ProFormaLineRequest> Lines,
    string? DeliveryLine1 = null,
    string? DeliveryLine2 = null,
    string? DeliveryCity = null,
    string? DeliveryRegion = null,
    string? DeliveryPostalCode = null,
    string? DeliveryCountryCode = null);

/// <summary>Returns a submitted pro forma for amendment.</summary>
public sealed record AmendProFormaRequest(string Reason);

/// <summary>Withdraws a pro forma before a decision.</summary>
public sealed record WithdrawProFormaRequest(string Reason);

/// <summary>Approves with an optional comment for the audit trail.</summary>
public sealed record ApproveProFormaRequest(string? Comment = null);

/// <summary>Rejects with a reason. The rep is never left guessing why.</summary>
public sealed record RejectProFormaRequest(string Reason);

/// <summary>One proposed credit line against one original invoice line.</summary>
public sealed record ProFormaCreditLineRequest(
    Guid OriginalInvoiceLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    decimal TaxAmount,
    decimal NetAmount,
    string Currency);

/// <summary>Captures a pro forma credit note against an invoice.</summary>
public sealed record CaptureCreditNoteRequest(
    Guid RepId,
    Guid CompanyId,
    Guid OriginalInvoiceId,
    string OriginalInvoiceNumber,
    string ReasonCode,
    string Reason,
    string Currency,
    string IdempotencyKey,
    IReadOnlyList<ProFormaCreditLineRequest> Lines);

/// <summary>One line on the pro forma view.</summary>
public sealed record ProFormaLineResponse(
    Guid LineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPrice,
    decimal Discount,
    string TaxCode,
    decimal Tax,
    decimal Net,
    decimal Gross,
    string PackSize,
    decimal AvailableAtCapture,
    DateTimeOffset AvailabilityAsAt);

/// <summary>One pro forma with its lines and decision trail.</summary>
public sealed record ProFormaResponse(
    Guid ProFormaId,
    string ProFormaNumber,
    Guid RepId,
    Guid CompanyId,
    Guid PartnerId,
    string Currency,
    string Status,
    decimal Gross,
    decimal? RepriceDelta,
    Guid? ApprovalRequestId,
    Guid? ConvertedOrderId,
    string? DecisionReason,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ProFormaLineResponse> Lines);

/// <summary>What approval created.</summary>
public sealed record ApproveProFormaResponse(Guid OrderId);

/// <summary>One company's available for one SKU, with its as-at.</summary>
public sealed record RepAvailabilityRowResponse(Guid CompanyId, decimal Available, DateTimeOffset AsAt);

/// <summary>Group-wide available for what the rep asked, per company, stamped.</summary>
public sealed record RepAvailabilityResponse(
    Guid? ItemId,
    Guid? ItemVariantId,
    bool HasStaleContributor,
    IReadOnlyList<RepAvailabilityRowResponse> Companies);

/// <summary>One period's figures with its comparison and variance.</summary>
public sealed record RepPerformanceResponse(
    Guid RepId,
    Guid? CompanyId,
    DateOnly Period,
    DateOnly CompareTo,
    decimal NetValue,
    decimal CompareNetValue,
    decimal Variance,
    decimal VariancePercent,
    int Version,
    string Reason);

/// <summary>A created document's id.</summary>
public sealed record FieldSalesIdResponse(Guid Id);
