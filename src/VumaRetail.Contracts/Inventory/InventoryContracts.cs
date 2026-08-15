namespace VumaRetail.Contracts.Inventory;

/// <summary>Creates a new stock location.</summary>
/// <param name="Code">The location's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The location's name.</param>
/// <param name="Type">Warehouse, sales floor, or other.</param>
/// <param name="StoreId">The owning store, or <c>null</c> for a tenant-wide location such as a central warehouse.</param>
public sealed record CreateStockLocationRequest(string Code, string Name, string Type, Guid? StoreId = null);

/// <summary>A stock location, as returned by the API.</summary>
/// <param name="Id">The location's id.</param>
/// <param name="Code">The location's code.</param>
/// <param name="Name">The location's name.</param>
/// <param name="Type">Warehouse, sales floor, or other.</param>
/// <param name="StoreId">The owning store, or <c>null</c> for a tenant-wide location.</param>
/// <param name="IsActive">Whether the location may still receive or issue stock.</param>
public sealed record StockLocationResponse(
    Guid Id,
    string Code,
    string Name,
    string Type,
    Guid? StoreId,
    bool IsActive);

/// <summary>Receives stock into a location.</summary>
/// <param name="LocationId">Where the stock arrived.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/> must be set.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/> must be set.</param>
/// <param name="Quantity">How much arrived. Must be positive.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in. Must match the item's own.</param>
/// <param name="UnitCost">What it cost per unit.</param>
/// <param name="Currency">The ISO 4217 currency the cost is denominated in.</param>
/// <param name="Note">An optional free-text note.</param>
public sealed record ReceiveStockRequest(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    string Currency,
    string? Note = null);

/// <summary>Records a sale issuing stock from a location. Stage 09 (POS) is this endpoint's real caller.</summary>
/// <param name="LocationId">Where the stock left from.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much was sold. Must be positive.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="SaleReferenceId">The sale this issue correlates to.</param>
public sealed record RecordSaleIssueRequest(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    Guid SaleReferenceId);

/// <summary>Posts a documented adjustment — a signed correction to on-hand quantity.</summary>
/// <param name="LocationId">Where the adjustment applies.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Delta">The signed quantity change — negative to write stock off. Must not be zero.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="ReasonCode">Why the adjustment was made: damage, loss, found, correction or other.</param>
/// <param name="UnitCost">The cost to value an increase at, or <c>null</c> to use the current average. Ignored for a decrease.</param>
/// <param name="Currency">The ISO 4217 currency for <paramref name="UnitCost"/>. Required when it is set.</param>
/// <param name="Note">An optional free-text note.</param>
public sealed record AdjustStockRequest(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Delta,
    string UnitOfMeasure,
    string ReasonCode,
    decimal? UnitCost = null,
    string? Currency = null,
    string? Note = null);

/// <summary>Transfers stock between two locations as two correlated ledger entries.</summary>
/// <param name="SourceLocationId">Where the stock leaves.</param>
/// <param name="DestinationLocationId">Where the stock arrives. Must differ from <paramref name="SourceLocationId"/>.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much to move. Must be positive.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="Note">An optional free-text note.</param>
public sealed record TransferStockRequest(
    Guid SourceLocationId,
    Guid DestinationLocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string? Note = null);

/// <summary>A stock balance, as returned by the API.</summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="QuantityOnHand">What is currently on hand.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="AverageCost">The weighted-average cost of one unit currently on hand.</param>
/// <param name="TotalValue">The value of everything on hand.</param>
/// <param name="Currency">The ISO 4217 currency the values are denominated in.</param>
public sealed record StockBalanceResponse(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityOnHand,
    string UnitOfMeasure,
    decimal AverageCost,
    decimal TotalValue,
    string Currency);

/// <summary>One posted, immutable stock movement, as returned by the API.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="LocationId">The location affected.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="MovementType">Receipt, sale issue, adjustment, transfer in/out, or stocktake variance.</param>
/// <param name="Quantity">The signed quantity delta — negative for stock leaving.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="UnitCost">The value of one unit at the moment of this movement.</param>
/// <param name="Value">The total value this movement carried.</param>
/// <param name="Currency">The ISO 4217 currency the values are denominated in.</param>
/// <param name="ReferenceType">Manual, transfer, stocktake or sale.</param>
/// <param name="ReferenceId">The correlating document's id, or <c>null</c> for a manual movement.</param>
/// <param name="ReasonCode">Why an adjustment was made, if this is one.</param>
/// <param name="Note">An optional free-text note.</param>
/// <param name="CreatedAt">When this entry was posted, UTC.</param>
public sealed record StockLedgerEntryResponse(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string MovementType,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    decimal Value,
    string Currency,
    string ReferenceType,
    Guid? ReferenceId,
    string? ReasonCode,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>A completed transfer, as returned by the API.</summary>
/// <param name="Id">The transfer's id.</param>
/// <param name="SourceLocationId">Where stock left.</param>
/// <param name="DestinationLocationId">Where stock arrived.</param>
/// <param name="ItemId">The item transferred, when it has no variants.</param>
/// <param name="ItemVariantId">The variant transferred.</param>
/// <param name="Quantity">How much moved.</param>
/// <param name="UnitOfMeasure">The unit the quantity is counted in.</param>
/// <param name="UnitCost">The source's average cost per unit, carried to the destination unchanged.</param>
/// <param name="Value">The value that moved.</param>
/// <param name="Currency">The ISO 4217 currency the values are denominated in.</param>
/// <param name="OutEntryId">The ledger entry posted at the source.</param>
/// <param name="InEntryId">The ledger entry posted at the destination.</param>
/// <param name="Note">An optional free-text note.</param>
public sealed record StockTransferResponse(
    Guid Id,
    Guid SourceLocationId,
    Guid DestinationLocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    decimal Value,
    string Currency,
    Guid OutEntryId,
    Guid InEntryId,
    string? Note);

/// <summary>Opens a new physical count session at a location.</summary>
/// <param name="LocationId">The location to count.</param>
public sealed record OpenStocktakeRequest(Guid LocationId);

/// <summary>Records a physical count for one stock-keeping unit within an open session.</summary>
/// <param name="ItemId">The item counted, when it has no variants.</param>
/// <param name="ItemVariantId">The variant counted.</param>
/// <param name="CountedQuantity">What was physically found. Must not be negative.</param>
/// <param name="UnitOfMeasure">The unit the count was taken in.</param>
public sealed record RecordStocktakeCountRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal CountedQuantity,
    string UnitOfMeasure);

/// <summary>One counted line within a stocktake session, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item counted, when it has no variants.</param>
/// <param name="ItemVariantId">The variant counted.</param>
/// <param name="SystemQuantity">What the system reported at the moment this line was recorded.</param>
/// <param name="CountedQuantity">What was physically found.</param>
/// <param name="Variance">The signed difference — counted minus system.</param>
/// <param name="UnitOfMeasure">The unit the quantities are counted in.</param>
public sealed record StocktakeLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal Variance,
    string UnitOfMeasure);

/// <summary>A stocktake session and its lines so far, as returned by the API.</summary>
/// <param name="Id">The session's id.</param>
/// <param name="LocationId">The location being counted.</param>
/// <param name="Status">Open or finalized.</param>
/// <param name="FinalizedAt">When the session was finalized, or <c>null</c> while still open.</param>
/// <param name="Lines">Every line recorded so far.</param>
public sealed record StocktakeSessionResponse(
    Guid Id,
    Guid LocationId,
    string Status,
    DateTimeOffset? FinalizedAt,
    IReadOnlyList<StocktakeLineResponse> Lines);

/// <summary>A newly created stock location's id.</summary>
/// <param name="Id">The location.</param>
public sealed record StockLocationIdResponse(Guid Id);

/// <summary>A newly posted ledger entry's id.</summary>
/// <param name="Id">The ledger entry.</param>
public sealed record StockLedgerEntryIdResponse(Guid Id);

/// <summary>A newly recorded transfer's id.</summary>
/// <param name="Id">The transfer.</param>
public sealed record StockTransferIdResponse(Guid Id);

/// <summary>A newly opened stocktake session's id.</summary>
/// <param name="Id">The session.</param>
public sealed record StocktakeSessionIdResponse(Guid Id);

/// <summary>A newly recorded stocktake line's id.</summary>
/// <param name="Id">The line.</param>
public sealed record StocktakeLineIdResponse(Guid Id);
