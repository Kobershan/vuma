namespace VumaRetail.Contracts.Registry;

public sealed record CreateBusinessGroupRequest(Guid BusinessId, string BusinessName, string BusinessType, decimal TransferValueThreshold, string TransferCostingMethod, string DiscrepancyDefaultOwner, string StoreCodePrefix);
public sealed record CreateHierarchyNodeRequest(Guid BusinessId, Guid CompanyId, string NodeType, string OwnershipType, Guid? ParentNodeId, string? StoreCode, bool StockHolding = true);
public sealed record CreateTransferRequest(Guid RequesterCompanyId, Guid SenderCompanyId, Guid ReceiverCompanyId, Guid HoldingCompanyId, decimal TotalValue, bool CentralBuying = false);
public sealed record AddBusinessCompanyRequest(Guid CompanyId);
public sealed record ChangeBusinessTypeRequest(string BusinessType);
public sealed record ReceiveTransferRequest(decimal Quantity);
public sealed record ReconcileTransferRequest(decimal RequestedQuantity, string? Reason);
public sealed record Stage22BusinessGroupResponse(Guid BusinessId, string BusinessName, string BusinessType, decimal TransferValueThreshold, string TransferCostingMethod, string DiscrepancyDefaultOwner, string StoreCodePrefix);
public sealed record Stage22HierarchyNodeResponse(Guid Id, Guid BusinessId, Guid CompanyId, string NodeType, string OwnershipType, Guid? ParentNodeId, string? StoreCode, bool StockHolding);
public sealed record Stage22TransferResponse(Guid Id, Guid RequesterCompanyId, Guid SenderCompanyId, Guid ReceiverCompanyId, decimal TotalValue, string Status, decimal? ReceivedQuantity, decimal? DiscrepancyQuantity);
