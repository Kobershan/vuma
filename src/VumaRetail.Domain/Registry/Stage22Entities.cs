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
public enum TransferRelation { Original, Remainder, Reverse }

/// <summary>A requested SKU quantity in a registry transfer; stock itself remains company-local.</summary>
public sealed class StockTransferLine
{
    private StockTransferLine() { }

    private StockTransferLine(
        Guid tenantId,
        Guid transferId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string unitOfMeasure,
        Guid senderLocationId,
        Guid? receiverLocationId,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber)
    {
        if (tenantId == Guid.Empty || transferId == Guid.Empty || senderLocationId == Guid.Empty)
        {
            throw new ArgumentException("Transfer line tenant, transfer and location are required.");
        }

        if ((itemId is null) == (itemVariantId is null))
        {
            throw new ArgumentException("A transfer line must identify either an item or an item variant.");
        }
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (string.IsNullOrWhiteSpace(unitOfMeasure)) throw new ArgumentException("A unit of measure is required.", nameof(unitOfMeasure));

        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        TransferId = transferId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        UnitOfMeasure = unitOfMeasure.Trim();
        SenderLocationId = senderLocationId;
        ReceiverLocationId = receiverLocationId;
        BatchReference = Normalize(batchReference, nameof(batchReference), 128);
        ExpiryDate = expiryDate;
        SerialNumber = Normalize(serialNumber, nameof(serialNumber), 128);
        if (SerialNumber is not null && quantity != 1m)
        {
            throw new ArgumentException("A serialised transfer line must have quantity one.", nameof(quantity));
        }
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public Guid SenderLocationId { get; private set; }
    public Guid? ReceiverLocationId { get; private set; }
    /// <summary>Batch or lot identity carried with the movement, when batch tracked.</summary>
    public string? BatchReference { get; private set; }
    /// <summary>Expiry date carried with the batch identity, when present.</summary>
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Serial identity carried with the movement, when serial tracked.</summary>
    public string? SerialNumber { get; private set; }
    public decimal? UnitCostAtTransferAmount { get; private set; }
    public string? UnitCostAtTransferCurrency { get; private set; }
    public decimal? ReceivedQuantity { get; private set; }

    public static StockTransferLine Create(
        Guid tenantId,
        Guid transferId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string unitOfMeasure,
        Guid senderLocationId,
        Guid? receiverLocationId = null,
        string? batchReference = null,
        DateOnly? expiryDate = null,
        string? serialNumber = null)
        => new(tenantId, transferId, itemId, itemVariantId, quantity, unitOfMeasure, senderLocationId, receiverLocationId, batchReference, expiryDate, serialNumber);

    private static string? Normalize(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException($"{name} is too long.", name);
        return normalized;
    }

    public void RecordTransferCost(Money cost)
    {
        if (cost.Amount < 0m || string.IsNullOrWhiteSpace(cost.Currency))
        {
            throw new ArgumentException("A transfer cost must have a non-negative amount and currency.", nameof(cost));
        }
        if (UnitCostAtTransferAmount is not null
            && (UnitCostAtTransferAmount != cost.Amount
                || !string.Equals(UnitCostAtTransferCurrency, cost.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A transfer line cost cannot be changed after shipment.");
        }
        UnitCostAtTransferAmount = cost.Amount;
        UnitCostAtTransferCurrency = cost.Currency.Trim().ToUpperInvariant();
    }

    public void RecordReceived(decimal quantity)
    {
        if (quantity < 0m || quantity > Quantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Received quantity must be between zero and requested quantity.");
        }

        ReceivedQuantity = quantity;
    }
}

/// <summary>Append-only registry-side transfer request state machine.</summary>
public sealed class StockTransferRequest
{
    private StockTransferRequest() { }
    private StockTransferRequest(Guid tenantId, Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying)
    {
        if (tenantId == Guid.Empty || requesterCompanyId == Guid.Empty || senderCompanyId == Guid.Empty || receiverCompanyId == Guid.Empty) throw new ArgumentException("Transfer companies are required.");
        if (senderCompanyId == receiverCompanyId) throw new ArgumentException("Sender and receiver must differ.");
        if (totalValue < 0) throw new ArgumentOutOfRangeException(nameof(totalValue));
        Id = UuidV7.NewGuid(); TenantId = tenantId; RequesterCompanyId = requesterCompanyId; SenderCompanyId = senderCompanyId; ReceiverCompanyId = receiverCompanyId; HoldingCompanyId = holdingCompanyId; TotalValue = totalValue; CentralBuying = centralBuying; Status = TransferStatus.Requested; Relation = TransferRelation.Original;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RequesterCompanyId { get; private set; }
    public Guid SenderCompanyId { get; private set; }
    public Guid ReceiverCompanyId { get; private set; }
    public Guid HoldingCompanyId { get; private set; }
    public decimal TotalValue { get; private set; }
    public bool CentralBuying { get; private set; }
    public Guid? RelatedTransferId { get; private set; }
    public TransferRelation Relation { get; private set; }
    public TransferStatus Status { get; private set; }
    public List<StockTransferLine> Lines { get; private set; } = [];
    public decimal? ReceivedQuantity { get; private set; }
    public decimal? DiscrepancyQuantity { get; private set; }
    public static StockTransferRequest Create(Guid tenantId, Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying = false) => new(tenantId, requesterCompanyId, senderCompanyId, receiverCompanyId, holdingCompanyId, totalValue, centralBuying);
    public static StockTransferRequest Create(
        Guid tenantId,
        Guid requesterCompanyId,
        Guid senderCompanyId,
        Guid receiverCompanyId,
        Guid holdingCompanyId,
        decimal totalValue,
        bool centralBuying,
        IReadOnlyCollection<StockTransferLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        StockTransferRequest transfer = Create(tenantId, requesterCompanyId, senderCompanyId, receiverCompanyId, holdingCompanyId, totalValue, centralBuying);
        if (lines.Count == 0) throw new ArgumentException("A transfer must contain at least one line.", nameof(lines));
        if (lines.Any(line => line.TenantId != tenantId))
        {
            throw new InvalidOperationException("Transfer lines must belong to the transfer tenant.");
        }
        foreach (StockTransferLine line in lines)
        {
            transfer.Lines.Add(StockTransferLine.Create(
                tenantId, transfer.Id, line.ItemId, line.ItemVariantId,
                line.Quantity, line.UnitOfMeasure, line.SenderLocationId, line.ReceiverLocationId,
                line.BatchReference, line.ExpiryDate, line.SerialNumber));
        }
        return transfer;
    }

    public static StockTransferRequest CreateRelated(
        StockTransferRequest source,
        TransferRelation relation,
        IReadOnlyCollection<StockTransferLine> lines,
        decimal totalValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lines);
        if (relation == TransferRelation.Original) throw new ArgumentException("A related transfer must declare its relation.", nameof(relation));
        if (lines.Count == 0) throw new ArgumentException("A related transfer must contain at least one line.", nameof(lines));

        StockTransferRequest transfer = Create(
            source.TenantId,
            source.RequesterCompanyId,
            relation == TransferRelation.Reverse ? source.ReceiverCompanyId : source.SenderCompanyId,
            relation == TransferRelation.Reverse ? source.SenderCompanyId : source.ReceiverCompanyId,
            source.HoldingCompanyId,
            totalValue,
            centralBuying: false);
        transfer.RelatedTransferId = source.Id;
        transfer.Relation = relation;
        foreach (StockTransferLine line in lines)
        {
            transfer.Lines.Add(StockTransferLine.Create(
                transfer.TenantId, transfer.Id, line.ItemId, line.ItemVariantId,
                line.Quantity, line.UnitOfMeasure,
                relation == TransferRelation.Reverse
                    ? line.ReceiverLocationId ?? throw new InvalidOperationException("A reverse line requires its receiver location.")
                    : line.SenderLocationId,
                relation == TransferRelation.Reverse ? line.SenderLocationId : line.ReceiverLocationId,
                line.BatchReference, line.ExpiryDate, line.SerialNumber));
        }
        return transfer;
    }
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
    public void Receive(decimal quantity)
    {
        if (Status is not (TransferStatus.InTransit or TransferStatus.Received))
        {
            throw Invalid();
        }
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (ReceivedQuantity is decimal alreadyReceived && quantity < alreadyReceived)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A subsequent receipt cannot reduce the quantity already received.");
        }
        if (Lines.Count > 0 && quantity > Lines.Sum(line => line.Quantity))
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Received quantity cannot exceed requested transfer lines.");
        }

        decimal remaining = quantity;
        foreach (StockTransferLine line in Lines)
        {
            decimal received = Math.Min(line.Quantity, remaining);
            line.RecordReceived(received);
            remaining -= received;
        }

        ReceivedQuantity = quantity;
        Status = TransferStatus.Received;
    }

    public void Reconcile(decimal requestedQuantity, string? reason = null)
    {
        Require(TransferStatus.Received);
        if (Lines.Count > 0 && requestedQuantity != Lines.Sum(line => line.Quantity))
        {
            throw new InvalidOperationException("Reconciliation must use the transfer's requested line quantity.");
        }

        DiscrepancyQuantity = ReceivedQuantity!.Value - requestedQuantity;
        if (DiscrepancyQuantity != 0 && string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("A discrepancy requires a reason.");
        Status = TransferStatus.Reconciled;
    }
    /// <summary>
    /// Cancels a transfer before any company-local stock reservation is created.
    /// Once reserved, picked, shipped, or received, cancellation would leave a company ledger
    /// effect without a compensating saga leg; those states require an explicit release or reverse
    /// operation instead.
    /// </summary>
    public void Cancel()
    {
        if (Status is not (TransferStatus.Requested
            or TransferStatus.RegionalApprovalPending
            or TransferStatus.Checked
            or TransferStatus.Accepted))
        {
            throw Invalid();
        }

        Status = TransferStatus.Cancelled;
    }
    private void Require(TransferStatus expected) { if (Status != expected) throw Invalid(); }
    private InvalidOperationException Invalid() => new($"Transfer {Id} cannot transition from {Status}.");
}
