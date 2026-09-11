#pragma warning disable CS1591
#pragma warning disable IDE0011
#pragma warning disable CA1062
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>The installation relationship model; it does not select a database schema.</summary>
public enum BusinessType { SingleBusiness, MultiLocationBusiness, GroupBusiness }
public enum HierarchyNodeType { Store, Regional, HeadOffice }
public enum OwnershipType { Owned, Franchised }
public enum TransferCostingMethod { SenderCost, GroupStandardCost, LandedCost }
public enum DiscrepancyOwner { Sender, Receiver, Split, HeldForReview }

/// <summary>Registry relationship settings shared by a multi-location business.</summary>
public sealed class GroupSettings
{
    private GroupSettings() { }
    private GroupSettings(Guid tenantId, Guid businessId, decimal threshold, TransferCostingMethod costing, DiscrepancyOwner owner, string storeCodePrefix)
    {
        TenantId = tenantId; BusinessId = businessId; TransferValueThreshold = threshold; TransferCostingMethod = costing; DiscrepancyDefaultOwner = owner; StoreCodePrefix = Require(storeCodePrefix, nameof(storeCodePrefix));
    }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }
    public decimal TransferValueThreshold { get; private set; }
    public TransferCostingMethod TransferCostingMethod { get; private set; }
    public DiscrepancyOwner DiscrepancyDefaultOwner { get; private set; }
    public string StoreCodePrefix { get; private set; } = string.Empty;
    public int RegionalApprovalBusinessHours { get; private set; } = 4;
    public int HoldingStoreDecisionHours { get; private set; } = 24;
    public int DiscrepancyReviewBusinessDays { get; private set; } = 3;

    public static GroupSettings Create(Guid tenantId, Guid businessId, decimal threshold, TransferCostingMethod costing, DiscrepancyOwner owner, string storeCodePrefix)
    {
        if (tenantId == Guid.Empty || businessId == Guid.Empty) throw new ArgumentException("Tenant and business are required.");
        if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        return new GroupSettings(tenantId, businessId, threshold, costing, owner, storeCodePrefix);
    }
    public void SetSlas(int regionalApprovalHours, int holdingDecisionHours, int discrepancyReviewDays)
    {
        if (regionalApprovalHours <= 0 || holdingDecisionHours <= 0 || discrepancyReviewDays <= 0) throw new ArgumentOutOfRangeException(nameof(regionalApprovalHours));
        RegionalApprovalBusinessHours = regionalApprovalHours; HoldingStoreDecisionHours = holdingDecisionHours; DiscrepancyReviewBusinessDays = discrepancyReviewDays;
    }
    public bool IsValidStoreCode(string code) => !string.IsNullOrWhiteSpace(code) && code.StartsWith(StoreCodePrefix, StringComparison.OrdinalIgnoreCase);
    private static string Require(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", name) : value.Trim();
}

/// <summary>A registry node for a legal company in a multi-location relationship.</summary>
public sealed class GroupHierarchyNode
{
    private GroupHierarchyNode() { }
    private GroupHierarchyNode(Guid tenantId, Guid businessId, Guid companyId, HierarchyNodeType nodeType, OwnershipType ownershipType, string? storeCode, bool stockHolding)
    {
        if (tenantId == Guid.Empty || businessId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant, business and company are required.");
        if (nodeType != HierarchyNodeType.Store && storeCode is not null) throw new ArgumentException("Only stores have a store code.", nameof(storeCode));
        if (nodeType == HierarchyNodeType.Store && string.IsNullOrWhiteSpace(storeCode)) throw new ArgumentException("Stores require a store code.", nameof(storeCode));
        TenantId = tenantId; BusinessId = businessId; CompanyId = companyId; NodeType = nodeType; OwnershipType = ownershipType; StoreCode = storeCode?.Trim(); StockHolding = stockHolding;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid CompanyId { get; private set; }
    public HierarchyNodeType NodeType { get; private set; }
    public OwnershipType OwnershipType { get; private set; }
    public Guid? ParentNodeId { get; private set; }
    public string? StoreCode { get; private set; }
    public bool StockHolding { get; private set; }
    public static GroupHierarchyNode Create(Guid tenantId, Guid businessId, Guid companyId, HierarchyNodeType nodeType, OwnershipType ownershipType, string? storeCode = null, bool stockHolding = true)
    {
        var node = new GroupHierarchyNode(tenantId, businessId, companyId, nodeType, ownershipType, storeCode, stockHolding) { Id = UuidV7.NewGuid() };
        return node;
    }
    public void SetParent(Guid? parentNodeId, IReadOnlyCollection<GroupHierarchyNode> existingNodes)
    {
        if (parentNodeId == Id) throw new InvalidOperationException("A hierarchy node cannot be its own parent.");
        if (parentNodeId is null) { ParentNodeId = null; return; }
        var parent = existingNodes.SingleOrDefault(x => x.Id == parentNodeId.Value) ?? throw new InvalidOperationException("Parent node was not found.");
        if (parent.TenantId != TenantId || parent.BusinessId != BusinessId) throw new InvalidOperationException("Parent must be in the same business.");
        var cursor = parent;
        while (cursor.ParentNodeId is Guid ancestorId)
        {
            if (ancestorId == Id) throw new InvalidOperationException("A hierarchy cycle is not allowed.");
            cursor = existingNodes.SingleOrDefault(x => x.Id == ancestorId) ?? throw new InvalidOperationException("Hierarchy contains a missing parent.");
        }
        if (existingNodes.Any(x => x.Id != Id && x.ParentNodeId == parentNodeId && x.CompanyId == CompanyId)) throw new InvalidOperationException("A company may have only one parent node.");
        ParentNodeId = parentNodeId;
    }
    public bool IsControlEligible => OwnershipType == OwnershipType.Owned;
    public bool IsVisibilityEligible => OwnershipType == OwnershipType.Owned;
}

/// <summary>Dedicated cost-free stock projection contract.</summary>
public sealed record OwnedStockOnHandProjection(Guid TenantId, Guid BusinessId, Guid CompanyId, Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal OnHand, decimal Reserved, decimal InStaging, decimal Available, string UnitOfMeasure, DateTimeOffset AsAt) { public Guid Id { get; init; } = UuidV7.NewGuid(); }

public enum TransferStatus { Requested, RegionalApprovalPending, Checked, Accepted, Declined, Reserved, Picked, Shipped, InTransit, Received, Reconciled, Cancelled }

/// <summary>Append-only registry-side transfer request state machine.</summary>
public sealed class StockTransferRequest
{
    private StockTransferRequest() { }
    private StockTransferRequest(Guid tenantId, Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying)
    {
        if (tenantId == Guid.Empty || requesterCompanyId == Guid.Empty || senderCompanyId == Guid.Empty || receiverCompanyId == Guid.Empty) throw new ArgumentException("Transfer companies are required.");
        if (senderCompanyId == receiverCompanyId) throw new ArgumentException("Sender and receiver must differ.");
        if (totalValue < 0) throw new ArgumentOutOfRangeException(nameof(totalValue));
        Id = UuidV7.NewGuid(); TenantId = tenantId; RequesterCompanyId = requesterCompanyId; SenderCompanyId = senderCompanyId; ReceiverCompanyId = receiverCompanyId; HoldingCompanyId = holdingCompanyId; TotalValue = totalValue; CentralBuying = centralBuying; Status = TransferStatus.Requested;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RequesterCompanyId { get; private set; }
    public Guid SenderCompanyId { get; private set; }
    public Guid ReceiverCompanyId { get; private set; }
    public Guid HoldingCompanyId { get; private set; }
    public decimal TotalValue { get; private set; }
    public bool CentralBuying { get; private set; }
    public TransferStatus Status { get; private set; }
    public decimal? ReceivedQuantity { get; private set; }
    public decimal? DiscrepancyQuantity { get; private set; }
    public static StockTransferRequest Create(Guid tenantId, Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying = false) => new(tenantId, requesterCompanyId, senderCompanyId, receiverCompanyId, holdingCompanyId, totalValue, centralBuying);
    public void Check(GroupSettings settings, bool receiverIsDirectChildOfHolding)
    {
        Require(TransferStatus.Requested);
        if (CentralBuying)
        {
            if (!receiverIsDirectChildOfHolding)
            {
                throw new InvalidOperationException("Central buying may only deliver to a direct holding-store child.");
            }

            Status = TransferStatus.Accepted;
        }
        else if (TotalValue >= settings.TransferValueThreshold) Status = TransferStatus.RegionalApprovalPending;
        else Status = TransferStatus.Checked;
    }
    public void ApproveRegional() { Require(TransferStatus.RegionalApprovalPending); Status = TransferStatus.Checked; }
    public void Accept() { Require(TransferStatus.Checked); Status = TransferStatus.Accepted; }
    public void Decline() { if (Status is not (TransferStatus.Checked or TransferStatus.RegionalApprovalPending)) throw Invalid(); Status = TransferStatus.Declined; }
    public void Reserve() { Require(TransferStatus.Accepted); Status = TransferStatus.Reserved; }
    public void Pick() { Require(TransferStatus.Reserved); Status = TransferStatus.Picked; }
    public void Ship() { Require(TransferStatus.Picked); Status = TransferStatus.Shipped; }
    public void MoveInTransit() { Require(TransferStatus.Shipped); Status = TransferStatus.InTransit; }
    public void Receive(decimal quantity) { Require(TransferStatus.InTransit); if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity)); ReceivedQuantity = quantity; Status = TransferStatus.Received; }
    public void Reconcile(decimal requestedQuantity, string? reason = null) { Require(TransferStatus.Received); DiscrepancyQuantity = ReceivedQuantity!.Value - requestedQuantity; if (DiscrepancyQuantity != 0 && string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("A discrepancy requires a reason."); Status = TransferStatus.Reconciled; }
    public void Cancel() { if (Status is TransferStatus.Reconciled or TransferStatus.Declined or TransferStatus.Cancelled) throw Invalid(); Status = TransferStatus.Cancelled; }
    private void Require(TransferStatus expected) { if (Status != expected) throw Invalid(); }
    private InvalidOperationException Invalid() => new($"Transfer {Id} cannot transition from {Status}.");
}
