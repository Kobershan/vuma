using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Reads and writes tenant-scoped BOM definitions.</summary>
public interface IBillOfMaterialsRepository
{
    /// <summary>Finds a definition by id, or <c>null</c>.</summary>
    Task<BillOfMaterials?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds one version for a finished item or variant.</summary>
    Task<BillOfMaterials?> FindVersionAsync(Guid itemId, Guid? variantId, int version, CancellationToken cancellationToken = default);

    /// <summary>Adds a new definition.</summary>
    void Add(BillOfMaterials bom);
}

/// <summary>Reads and writes tenant/company-scoped production orders.</summary>
public interface IProductionOrderRepository
{
    /// <summary>Finds an order by its stable client-created id.</summary>
    Task<ProductionOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new order.</summary>
    void Add(ProductionOrder order);
}

/// <summary>Financial fact raised when production scrap is recorded.</summary>
public sealed record ProductionScrapAccountingEvent(
    Guid TenantId,
    Guid CompanyId,
    Guid ProductionOrderId,
    Guid OperationId,
    Quantity Quantity,
    Money Value,
    DateTimeOffset OccurredAt);

/// <summary>Publishes production WIP/scrap facts through the finance boundary.</summary>
public interface IProductionAccountingEventPublisher
{
    /// <summary>Raises one production scrap fact.</summary>
    Task PublishScrapAsync(ProductionScrapAccountingEvent accountingEvent, CancellationToken cancellationToken = default);
}
