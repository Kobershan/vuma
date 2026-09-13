using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Manufacturing;

/// <summary>A traceable request to manufacture a finished item from one published BOM version.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ProductionOrder : Entity
{
    private readonly List<ProductionMaterialRequirement> _materials = [];
    private readonly List<ProductionMaterialIssue> _issues = [];
    private readonly List<ProductionOutputReceipt> _receipts = [];
    private readonly List<ProductionScrap> _scrap = [];

    private ProductionOrder(Guid id, Guid tenantId, Guid companyId, Guid finishedItemId, Quantity quantity, string orderNumber, Guid billOfMaterialsId)
        : base(id, tenantId, null)
    {
        CompanyId = companyId;
        FinishedItemId = finishedItemId;
        PlannedQuantity = quantity;
        OrderNumber = orderNumber;
        BillOfMaterialsId = billOfMaterialsId;
        Status = ProductionOrderStatus.Draft;
    }

    private ProductionOrder()
    {
    }

    /// <summary>Creates an offline-safe draft with a caller-supplied operation identity.</summary>
    public static ProductionOrder Create(
        Guid id, Guid tenantId, Guid companyId, Guid finishedItemId, Quantity quantity, string orderNumber, Guid billOfMaterialsId)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || companyId == Guid.Empty || finishedItemId == Guid.Empty || billOfMaterialsId == Guid.Empty)
        {
            throw new ArgumentException("Production order identity and ownership are required.");
        }
        if (quantity.Value <= 0m)
        {
            throw ManufacturingRuleException.PositiveQuantityRequired();
        }
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            throw new ArgumentException("A production order number is required.", nameof(orderNumber));
        }

        return new ProductionOrder(id, tenantId, companyId, finishedItemId, quantity, orderNumber.Trim(), billOfMaterialsId);
    }

    /// <summary>The BOM requested by the caller; the published version is snapshotted at release.</summary>
    public Guid BillOfMaterialsId { get; private set; }

    /// <summary>Checks whether a repeated client operation has the same immutable request.</summary>
    public bool MatchesCreateRequest(Guid companyId, Guid finishedItemId, Quantity quantity, string orderNumber, Guid billOfMaterialsId)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);
        return CompanyId == companyId && FinishedItemId == finishedItemId && PlannedQuantity == quantity
            && string.Equals(OrderNumber, orderNumber.Trim(), StringComparison.Ordinal)
            && BillOfMaterialsId == billOfMaterialsId;
    }

    /// <summary>The finished item to be produced.</summary>
    public Guid FinishedItemId { get; private set; }

    /// <summary>The immutable order number.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Requested finished quantity.</summary>
    public Quantity PlannedQuantity { get; private set; }

    /// <summary>The production lifecycle state.</summary>
    public ProductionOrderStatus Status { get; private set; }

    /// <summary>The BOM version and material snapshot captured at release.</summary>
    public ProductionSnapshot? Snapshot { get; private set; }

    /// <summary>Material requirements captured at release.</summary>
    public IReadOnlyList<ProductionMaterialRequirement> Materials => _materials;

    /// <summary>Material ledger operations already accepted for this order.</summary>
    public IReadOnlyList<ProductionMaterialIssue> Issues => _issues;

    /// <summary>Finished output receipts already accepted for this order.</summary>
    public IReadOnlyList<ProductionOutputReceipt> Receipts => _receipts;

    /// <summary>Scrap records already accepted for this order.</summary>
    public IReadOnlyList<ProductionScrap> Scrap => _scrap;

    /// <summary>Records one material issue exactly once, rejecting a changed replay.</summary>
    public bool IssueMaterial(Guid operationId, Guid componentItemId, Guid? componentVariantId, Quantity quantity, Money unitCost)
    {
        EnsureExecutable();
        EnsurePositiveOperation(operationId, quantity);
        ProductionMaterialIssue? prior = _issues.SingleOrDefault(issue => issue.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.ComponentItemId != componentItemId || prior.ComponentVariantId != componentVariantId || prior.Quantity != quantity || prior.UnitCost != unitCost)
            {
                throw ManufacturingRuleException.OperationPayloadConflict(operationId);
            }
            return false;
        }
        ProductionMaterialRequirement requirement = _materials.SingleOrDefault(material => material.ComponentItemId == componentItemId && material.ComponentVariantId == componentVariantId)
            ?? throw ManufacturingRuleException.MaterialNotRequired(componentItemId);
        Quantity consumed = _issues.Where(issue => issue.ComponentItemId == componentItemId && issue.ComponentVariantId == componentVariantId)
            .Select(issue => issue.Quantity).Aggregate(Quantity.Zero(quantity.UnitOfMeasure), (sum, value) => sum + value);
        if (consumed + quantity > requirement.RequiredQuantity)
        {
            throw ManufacturingRuleException.MaterialOverIssue(componentItemId);
        }
        _issues.Add(new ProductionMaterialIssue(operationId, componentItemId, componentVariantId, quantity, unitCost));
        return true;
    }

    /// <summary>Records one finished-output receipt exactly once.</summary>
    public bool ReceiveOutput(Guid operationId, Quantity quantity, Money unitCost)
    {
        EnsureExecutable();
        EnsurePositiveOperation(operationId, quantity);
        ProductionOutputReceipt? prior = _receipts.SingleOrDefault(receipt => receipt.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.Quantity != quantity || prior.UnitCost != unitCost)
            {
                throw ManufacturingRuleException.OperationPayloadConflict(operationId);
            }
            return false;
        }
        if (TotalOutput() + quantity > PlannedQuantity)
        {
            throw ManufacturingRuleException.OutputOverPlan();
        }
        _receipts.Add(new ProductionOutputReceipt(operationId, quantity, unitCost));
        return true;
    }

    /// <summary>Records one scrap quantity exactly once, bounded by the planned output.</summary>
    public bool RecordScrap(Guid operationId, Quantity quantity, Money unitCost)
    {
        EnsureExecutable();
        EnsurePositiveOperation(operationId, quantity);
        ProductionScrap? prior = _scrap.SingleOrDefault(entry => entry.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.Quantity != quantity || prior.UnitCost != unitCost)
            {
                throw ManufacturingRuleException.OperationPayloadConflict(operationId);
            }
            return false;
        }
        if (TotalOutput() + TotalScrap() + quantity > PlannedQuantity)
        {
            throw ManufacturingRuleException.OutputOverPlan();
        }
        _scrap.Add(new ProductionScrap(operationId, quantity, unitCost));
        return true;
    }

    /// <summary>Releases the order against the current published BOM and copies its inputs.</summary>
    public bool Release(Guid operationId, BillOfMaterials bom, DateTimeOffset releasedAt)
    {
        ArgumentNullException.ThrowIfNull(bom);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A production operation identity is required.", nameof(operationId));
        }
        if (Status is not ProductionOrderStatus.Draft)
        {
            if (Status == ProductionOrderStatus.Released && Snapshot is not null && Snapshot.ReleaseOperationId == operationId && Snapshot.BillOfMaterialsId == bom.Id)
            {
                return false;
            }
            if (Status == ProductionOrderStatus.Released && Snapshot is not null && Snapshot.ReleaseOperationId == operationId)
            {
                throw ManufacturingRuleException.OperationPayloadConflict(operationId);
            }
            throw ManufacturingRuleException.InvalidProductionTransition(Status, ProductionOrderStatus.Released);
        }
        if (bom.Status is not BillOfMaterialsStatus.Published || bom.FinishedItemId != FinishedItemId || bom.Id != BillOfMaterialsId || (bom.CompanyId.HasValue && bom.CompanyId != CompanyId))
        {
            throw ManufacturingRuleException.PublishedProductionBomRequired();
        }

        Snapshot = new ProductionSnapshot(
            operationId,
            bom.Id,
            bom.Version,
            bom.Name,
            releasedAt,
            bom.RoutingSteps.ToArray());
        _materials.Clear();
        _materials.AddRange(bom.Lines.Select(line => new ProductionMaterialRequirement(
            line.ComponentItemId,
            line.ComponentVariantId,
            line.Quantity * PlannedQuantity.Value,
            line.ScrapPercent,
            line.AlternateGroup)));
        Status = ProductionOrderStatus.Released;
        return true;
    }

    /// <summary>Moves a released order into execution.</summary>
    public void Start() => Transition(ProductionOrderStatus.Released, ProductionOrderStatus.InProgress);

    /// <summary>Marks execution complete after output, scrap and material reconcile.</summary>
    public void Complete()
    {
        if (TotalOutput() + TotalScrap() != PlannedQuantity)
        {
            throw ManufacturingRuleException.OutputReconciliationRequired();
        }
        Transition(ProductionOrderStatus.InProgress, ProductionOrderStatus.Completed);
    }

    /// <summary>Closes a completed order.</summary>
    public void Close() => Transition(ProductionOrderStatus.Completed, ProductionOrderStatus.Closed);

    private void Transition(ProductionOrderStatus expected, ProductionOrderStatus next)
    {
        if (Status != expected)
        {
            throw ManufacturingRuleException.InvalidProductionTransition(Status, next);
        }
        Status = next;
    }

    private void EnsureExecutable()
    {
        if (Status == ProductionOrderStatus.Released)
        {
            Start();
        }
        else if (Status != ProductionOrderStatus.InProgress)
        {
            throw ManufacturingRuleException.InvalidProductionTransition(Status, ProductionOrderStatus.InProgress);
        }
    }

    private static void EnsurePositiveOperation(Guid operationId, Quantity quantity)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A production operation identity is required.", nameof(operationId));
        }
        if (quantity.Value <= 0m)
        {
            throw ManufacturingRuleException.PositiveQuantityRequired();
        }
    }

    private Quantity TotalOutput() => _receipts.Select(receipt => receipt.Quantity).Aggregate(Quantity.Zero(PlannedQuantity.UnitOfMeasure), (sum, value) => sum + value);
    private Quantity TotalScrap() => _scrap.Select(entry => entry.Quantity).Aggregate(Quantity.Zero(PlannedQuantity.UnitOfMeasure), (sum, value) => sum + value);
}

/// <summary>Lifecycle of a production order.</summary>
public enum ProductionOrderStatus
{
    /// <summary>Order is being prepared.</summary>
    Draft = 0,
    /// <summary>BOM and requirements have been snapshotted.</summary>
    Released = 1,
    /// <summary>Material or output execution is underway.</summary>
    InProgress = 2,
    /// <summary>Planned output and reconciliation are complete.</summary>
    Completed = 3,
    /// <summary>Order is immutable and closed.</summary>
    Closed = 4,
}

/// <summary>Immutable release-time production inputs.</summary>
public sealed record ProductionSnapshot(
    Guid ReleaseOperationId,
    Guid BillOfMaterialsId,
    int BillOfMaterialsVersion,
    string BillOfMaterialsName,
    DateTimeOffset ReleasedAt,
    IReadOnlyList<RoutingStep> RoutingSteps);

/// <summary>A material requirement captured for one production order.</summary>
public sealed record ProductionMaterialRequirement(
    Guid ComponentItemId,
    Guid? ComponentVariantId,
    Quantity RequiredQuantity,
    decimal ScrapPercent,
    string? AlternateGroup);

/// <summary>One idempotent material consumption operation.</summary>
public sealed record ProductionMaterialIssue(Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, Quantity Quantity, Money UnitCost);

/// <summary>One idempotent finished-output receipt.</summary>
public sealed record ProductionOutputReceipt(Guid OperationId, Quantity Quantity, Money UnitCost);

/// <summary>One idempotent scrap record.</summary>
public sealed record ProductionScrap(Guid OperationId, Quantity Quantity, Money UnitCost);
