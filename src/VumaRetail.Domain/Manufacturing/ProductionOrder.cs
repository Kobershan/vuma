using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Manufacturing;

/// <summary>A traceable request to manufacture a finished item from one published BOM version.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ProductionOrder : Entity
{
    private readonly List<ProductionMaterialRequirement> _materials = [];

    private ProductionOrder(Guid id, Guid tenantId, Guid companyId, Guid finishedItemId, Quantity quantity, string orderNumber)
        : base(id, tenantId, null)
    {
        CompanyId = companyId;
        FinishedItemId = finishedItemId;
        PlannedQuantity = quantity;
        OrderNumber = orderNumber;
        Status = ProductionOrderStatus.Draft;
    }

    private ProductionOrder()
    {
    }

    /// <summary>Creates an offline-safe draft with a caller-supplied operation identity.</summary>
    public static ProductionOrder Create(
        Guid id, Guid tenantId, Guid companyId, Guid finishedItemId, Quantity quantity, string orderNumber)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || companyId == Guid.Empty || finishedItemId == Guid.Empty)
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

        return new ProductionOrder(id, tenantId, companyId, finishedItemId, quantity, orderNumber.Trim());
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

    /// <summary>Releases the order against the current published BOM and copies its inputs.</summary>
    public void Release(BillOfMaterials bom, DateTimeOffset releasedAt)
    {
        ArgumentNullException.ThrowIfNull(bom);
        if (Status is not ProductionOrderStatus.Draft)
        {
            throw ManufacturingRuleException.InvalidProductionTransition(Status, ProductionOrderStatus.Released);
        }
        if (bom.Status is not BillOfMaterialsStatus.Published || bom.FinishedItemId != FinishedItemId)
        {
            throw ManufacturingRuleException.PublishedProductionBomRequired();
        }

        Snapshot = new ProductionSnapshot(
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
    }

    /// <summary>Moves a released order into execution.</summary>
    public void Start() => Transition(ProductionOrderStatus.Released, ProductionOrderStatus.InProgress);

    /// <summary>Marks execution complete after output, scrap and material reconcile.</summary>
    public void Complete() => Transition(ProductionOrderStatus.InProgress, ProductionOrderStatus.Completed);

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
