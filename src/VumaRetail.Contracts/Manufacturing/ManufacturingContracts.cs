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

/// <summary>One immutable production routing step.</summary>
public sealed record ProductionRoutingStepResponse(int Sequence, string OperationName, decimal SetupMinutes, decimal RunMinutes);

/// <summary>One production material requirement.</summary>
public sealed record ProductionMaterialRequirementResponse(Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal ScrapPercent, string? AlternateGroup);

/// <summary>One recorded production material issue.</summary>
public sealed record ProductionMaterialIssueResponse(Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>One recorded finished-output receipt.</summary>
public sealed record ProductionOutputReceiptResponse(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>One recorded production scrap entry.</summary>
public sealed record ProductionScrapResponse(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>Production order execution and genealogy response.</summary>
public sealed record ProductionOrderResponse(Guid Id, Guid CompanyId, Guid FinishedItemId, decimal PlannedQuantity, string UnitOfMeasure, string OrderNumber, string Status, Guid BillOfMaterialsId, IReadOnlyList<ProductionRoutingStepResponse> Routing, IReadOnlyList<ProductionMaterialRequirementResponse> Materials, IReadOnlyList<ProductionMaterialIssueResponse> Issues, IReadOnlyList<ProductionOutputReceiptResponse> Receipts, IReadOnlyList<ProductionScrapResponse> Scrap);

/// <summary>Snapshotted routing capacity for a production order.</summary>
public sealed record ProductionCapacityResponse(Guid ProductionOrderId, decimal PlannedQuantity, string UnitOfMeasure, decimal SetupMinutes, decimal RunMinutes, decimal TotalMinutes);

/// <summary>Records a production material issue.</summary>
public sealed record IssueProductionMaterialRequest(Guid LocationId, Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>Records finished output.</summary>
public sealed record ReceiveProductionOutputRequest(Guid LocationId, Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);

/// <summary>Records production scrap.</summary>
public sealed record RecordProductionScrapRequest(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);
