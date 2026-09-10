namespace VumaRetail.Contracts.Manufacturing;

/// <summary>One component in a BOM create request.</summary>
public sealed record BillOfMaterialsLineRequest(Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal ScrapPercent = 0m, string? AlternateGroup = null);

/// <summary>Creates a draft BOM.</summary>
public sealed record CreateBillOfMaterialsRequest(Guid CompanyId, Guid FinishedItemId, Guid? FinishedVariantId, int Version, string Name, IReadOnlyList<BillOfMaterialsLineRequest> Lines);

/// <summary>A BOM returned by the API.</summary>
public sealed record BillOfMaterialsResponse(Guid Id, Guid FinishedItemId, Guid? FinishedVariantId, int Version, string Name, string Status, IReadOnlyList<BillOfMaterialsLineRequest> Lines);

/// <summary>Returns a created BOM id.</summary>
public sealed record BillOfMaterialsIdResponse(Guid Id);
