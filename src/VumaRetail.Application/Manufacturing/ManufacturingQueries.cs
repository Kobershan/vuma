using VumaRetail.Domain.Manufacturing;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Reads one production order and its immutable execution genealogy.</summary>
public sealed record GetProductionOrderQuery(Guid ProductionOrderId) : IQuery<ProductionOrder>;

/// <summary>Handles a tenant-scoped production-order read.</summary>
public sealed class GetProductionOrderQueryHandler(IProductionOrderRepository orders)
    : IQueryHandler<GetProductionOrderQuery, ProductionOrder>
{
    /// <inheritdoc />
    public async Task<ProductionOrder> HandleAsync(GetProductionOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await orders.FindAsync(query.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(query.ProductionOrderId);
    }
}

/// <summary>Reads the planned routing capacity for one released production order.</summary>
public sealed record GetProductionCapacityQuery(Guid ProductionOrderId) : IQuery<ProductionCapacity>;

/// <summary>One order's snapshotted routing capacity.</summary>
public sealed record ProductionCapacity(
    Guid ProductionOrderId,
    decimal PlannedQuantity,
    string UnitOfMeasure,
    decimal SetupMinutes,
    decimal RunMinutes,
    decimal TotalMinutes);

/// <summary>Calculates capacity from the release-time routing snapshot.</summary>
public sealed class GetProductionCapacityQueryHandler(IProductionOrderRepository orders)
    : IQueryHandler<GetProductionCapacityQuery, ProductionCapacity>
{
    /// <inheritdoc />
    public async Task<ProductionCapacity> HandleAsync(GetProductionCapacityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ProductionOrder order = await orders.FindAsync(query.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(query.ProductionOrderId);
        ProductionSnapshot snapshot = order.Snapshot
            ?? throw ManufacturingRuleException.PublishedProductionBomRequired();
        decimal setup = snapshot.RoutingSteps.Sum(step => step.SetupMinutes ?? 0m);
        decimal run = snapshot.RoutingSteps.Sum(step => step.RunMinutes ?? 0m) * order.PlannedQuantity.Value;
        return new ProductionCapacity(order.Id, order.PlannedQuantity.Value, order.PlannedQuantity.UnitOfMeasure, setup, run, setup + run);
    }
}
