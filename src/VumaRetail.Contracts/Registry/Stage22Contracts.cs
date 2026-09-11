namespace VumaRetail.Contracts.Registry;

public sealed record CreateBusinessGroupRequest(Guid BusinessId, string BusinessName, string BusinessType, decimal TransferValueThreshold, string TransferCostingMethod, string DiscrepancyDefaultOwner, string StoreCodePrefix);
public sealed record CreateHierarchyNodeRequest(Guid BusinessId, Guid CompanyId, string NodeType, string OwnershipType, Guid? ParentNodeId, string? StoreCode, bool StockHolding = true);
public sealed record CreateTransferLineRequest(Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure, Guid SenderLocationId, Guid? ReceiverLocationId = null);
public sealed record CreateTransferRequest(Guid RequesterCompanyId, Guid SenderCompanyId, Guid ReceiverCompanyId, Guid HoldingCompanyId, decimal TotalValue, bool CentralBuying = false, IReadOnlyList<CreateTransferLineRequest>? Lines = null);
public sealed record AddBusinessCompanyRequest(Guid CompanyId);
public sealed record ChangeBusinessTypeRequest(string BusinessType);
public sealed record ReceiveTransferRequest(decimal Quantity);
public sealed record ReconcileTransferRequest(decimal RequestedQuantity, string? Reason);
public sealed record AddPremisesSkuRoutingRequest(Guid PremisesId, string SkuOrBarcode, Guid CompanyId, bool IsBarcode);
public sealed record PublishOwnedStockProjectionRequest(Guid BusinessId, Guid CompanyId, Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal OnHand, decimal Reserved, decimal InStaging, decimal Available, string UnitOfMeasure, DateTimeOffset AsAt);
public sealed record Stage22OwnedStockResponse(Guid Id, Guid BusinessId, Guid CompanyId, Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal OnHand, decimal Reserved, decimal InStaging, decimal Available, string UnitOfMeasure, DateTimeOffset AsAt);
public sealed record Stage22BusinessGroupResponse(Guid BusinessId, string BusinessName, string BusinessType, decimal TransferValueThreshold, string TransferCostingMethod, string DiscrepancyDefaultOwner, string StoreCodePrefix);
public sealed record Stage22HierarchyNodeResponse(Guid Id, Guid BusinessId, Guid CompanyId, string NodeType, string OwnershipType, Guid? ParentNodeId, string? StoreCode, bool StockHolding);
public sealed record Stage22TransferResponse(Guid Id, Guid RequesterCompanyId, Guid SenderCompanyId, Guid ReceiverCompanyId, decimal TotalValue, string Status, decimal? ReceivedQuantity, decimal? DiscrepancyQuantity);
