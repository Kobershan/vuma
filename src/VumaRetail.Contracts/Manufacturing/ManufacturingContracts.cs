namespace VumaRetail.Contracts.Manufacturing;

/// <summary>One component in a BOM create request.</summary>
public sealed record BillOfMaterialsLineRequest(Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal ScrapPercent = 0m, string? AlternateGroup = null);

/// <summary>Creates a draft BOM.</summary>
public sealed record CreateBillOfMaterialsRequest(Guid CompanyId, Guid FinishedItemId, Guid? FinishedVariantId, int Version, string Name, IReadOnlyList<BillOfMaterialsLineRequest> Lines);

/// <summary>A BOM returned by the API.</summary>
public sealed record BillOfMaterialsResponse(Guid Id, Guid FinishedItemId, Guid? FinishedVariantId, int Version, string Name, string Status, IReadOnlyList<BillOfMaterialsLineRequest> Lines);

/// <summary>Returns a created BOM id.</summary>
public sealed record BillOfMaterialsIdResponse(Guid Id);

/// <summary>Creates a production order.</summary>
public sealed record CreateProductionOrderRequest(Guid OperationId, Guid CompanyId, Guid FinishedItemId, decimal Quantity, string UnitOfMeasure, string OrderNumber, Guid BillOfMaterialsId);

/// <summary>Releases a production order against one published BOM.</summary>
public sealed record ReleaseProductionOrderRequest(Guid OperationId, Guid BillOfMaterialsId);

/// <summary>Records a production material issue.</summary>
public sealed record IssueProductionMaterialRequest(Guid LocationId, Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>Records finished output.</summary>
public sealed record ReceiveProductionOutputRequest(Guid LocationId, Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>Records production scrap.</summary>
public sealed record RecordProductionScrapRequest(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);
