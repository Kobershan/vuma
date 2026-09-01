namespace VumaRetail.Contracts.Orders;

/// <summary>Raises a new draft order.</summary>
public sealed record CreateOrderRequest(
    Guid? PartnerId,
    string Channel,
    string FulfilmentType,
    Guid FulfillingLocationId,
    string Currency,
    string? DeliveryLine1 = null,
    string? DeliveryLine2 = null,
    string? DeliveryCity = null,
    string? DeliveryRegion = null,
    string? DeliveryPostalCode = null,
    string? DeliveryCountryCode = null,
    DateTimeOffset? RequestedFulfilmentDate = null);

/// <summary>Adds a demand line to a draft order.</summary>
public sealed record AddOrderLineRequest(Guid? ItemId, Guid? ItemVariantId, decimal RequestedQuantity, string UnitOfMeasure);

/// <summary>Cancels a whole order.</summary>
public sealed record CancelOrderRequest(string? Reason = null);

/// <summary>Records which mechanism paid for an order.</summary>
public sealed record RecordOrderSettlementRequest(string PaymentStatus, Guid? SettlingSaleId = null, Guid? SettlingCustomerAccountId = null);

/// <summary>A delivery address, as returned by the API.</summary>
public sealed record OrderAddressResponse(
    string Line1, string? Line2, string City, string? Region, string? PostalCode, string CountryCode);

/// <summary>One order line, with its allocation and fulfilment computed live, as returned by the API.</summary>
public sealed record SalesOrderLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal RequestedQuantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    Guid? PriceListId,
    string PromotionsSummary,
    decimal BackorderedQuantity,
    string LineStatus,
    decimal AllocatedQuantity,
    decimal FulfilledQuantity);

/// <summary>An order and every line on it, as returned by the API.</summary>
public sealed record SalesOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid? PartnerId,
    string Channel,
    string FulfilmentType,
    Guid FulfillingLocationId,
    OrderAddressResponse? DeliveryAddress,
    string Status,
    string PaymentStatus,
    Guid? SettlingSaleId,
    Guid? SettlingCustomerAccountId,
    string Currency,
    DateTimeOffset OrderDate,
    DateTimeOffset? RequestedFulfilmentDate,
    bool IsRevenueRecognised,
    decimal Net,
    decimal Tax,
    decimal Gross,
    IReadOnlyList<SalesOrderLineResponse> Lines);

/// <summary>An order, summarised for a list screen, as returned by the API.</summary>
public sealed record SalesOrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    Guid? PartnerId,
    string Channel,
    string FulfilmentType,
    string Status,
    string PaymentStatus,
    string Currency,
    DateTimeOffset OrderDate,
    decimal Net,
    decimal Tax,
    decimal Gross);

/// <summary>A page of orders, as returned by the API. See <c>docs/API_STANDARDS.md</c> §8.</summary>
public sealed record SalesOrderPageResponse(IReadOnlyList<SalesOrderSummaryResponse> Items, string? NextCursor, bool HasMore);

/// <summary>One backordered line, as returned by the API.</summary>
public sealed record BackorderedLineResponse(
    Guid SalesOrderId,
    string OrderNumber,
    DateTimeOffset OrderDate,
    Guid SalesOrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal BackorderedQuantity,
    string UnitOfMeasure);

/// <summary>What one reattempt pass achieved, as returned by the API.</summary>
public sealed record ReattemptBackorderedAllocationsResponse(int OrdersReallocated, int LinesReallocated);

/// <summary>What completing an order recognised, or not, as returned by the API.</summary>
public sealed record CompleteOrderResponse(Guid SalesOrderId, string Status, bool RevenueRecognised, decimal Net, decimal Tax, decimal Gross);

/// <summary>Raises an order return.</summary>
public sealed record CreateOrderReturnRequest(Guid SalesOrderId, string Reason);

/// <summary>Puts a fulfilled order line onto a draft return.</summary>
public sealed record AddOrderReturnLineRequest(Guid SalesOrderLineId, decimal Quantity);

/// <summary>One line coming back on an order return, as returned by the API.</summary>
public sealed record SalesOrderReturnLineResponse(
    Guid Id,
    Guid SalesOrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    decimal FulfilledQuantity,
    decimal UnitPrice,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string StockReturn,
    Guid? StockLedgerEntryId,
    string? StockReturnNote);

/// <summary>An order return and every line on it, as returned by the API.</summary>
public sealed record SalesOrderReturnResponse(
    Guid Id,
    Guid SalesOrderId,
    string ReturnNumber,
    string Reason,
    Guid AuthorisedByUserId,
    string Status,
    string RefundStatus,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string Currency,
    DateTimeOffset RaisedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<SalesOrderReturnLineResponse> Lines);

/// <summary>What completing an order return handed back, as returned by the API.</summary>
public sealed record OrderReturnCompletionResponse(
    Guid SalesOrderReturnId, string ReturnNumber, decimal Net, decimal Tax, decimal Gross, string Currency, int StockReturnsRefused);

/// <summary>A page of order returns, as returned by the API.</summary>
public sealed record SalesOrderReturnPageResponse(IReadOnlyList<SalesOrderReturnResponse> Items, string? NextCursor, bool HasMore);

/// <summary>A newly created row's id.</summary>
public sealed record OrderIdResponse(Guid Id);
