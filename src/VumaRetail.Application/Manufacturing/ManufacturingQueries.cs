using VumaRetail.Domain.Manufacturing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Reads one production order and its immutable execution genealogy.</summary>
#pragma warning disable CS1591
public sealed record ProductionOrderReadModel(
    Guid Id, Guid CompanyId, Guid FinishedItemId, decimal PlannedQuantity, string UnitOfMeasure,
    string OrderNumber, string Status, Guid BillOfMaterialsId,
    IReadOnlyList<ProductionRoutingReadModel> Routing,
    IReadOnlyList<ProductionMaterialReadModel> Materials,
    IReadOnlyList<ProductionIssueReadModel> Issues,
    IReadOnlyList<ProductionOutputReadModel> Receipts,
    IReadOnlyList<ProductionScrapReadModel> Scrap);
public sealed record ProductionRoutingReadModel(int Sequence, string OperationName, decimal SetupMinutes, decimal RunMinutes);
public sealed record ProductionMaterialReadModel(Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal ScrapPercent, string? AlternateGroup);
public sealed record ProductionIssueReadModel(Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);
public sealed record ProductionOutputReadModel(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);
public sealed record ProductionScrapReadModel(Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency);
#pragma warning restore CS1591

/// <summary>Reads a production order by id.</summary>
public sealed record GetProductionOrderQuery(Guid ProductionOrderId) : IQuery<ProductionOrderReadModel>;

/// <summary>Handles a tenant-scoped production-order read.</summary>
public sealed class GetProductionOrderQueryHandler(IProductionOrderRepository orders, ICompanyContext? company = null)
    : IQueryHandler<GetProductionOrderQuery, ProductionOrderReadModel>
{
    /// <inheritdoc />
    public async Task<ProductionOrderReadModel> HandleAsync(GetProductionOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ProductionOrder order = await orders.FindAsync(query.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(query.ProductionOrderId);
        EnsureCompany(order, company);
        return new ProductionOrderReadModel(order.Id, order.CompanyId!.Value, order.FinishedItemId,
            order.PlannedQuantity.Value, order.PlannedQuantity.UnitOfMeasure, order.OrderNumber, order.Status.ToString(), order.BillOfMaterialsId,
            order.Snapshot?.RoutingSteps.Select(step => new ProductionRoutingReadModel(step.Sequence, step.OperationName, step.SetupMinutes ?? 0m, step.RunMinutes ?? 0m)).ToArray() ?? [],
            order.Materials.Select(material => new ProductionMaterialReadModel(material.ComponentItemId, material.ComponentVariantId, material.RequiredQuantity.Value, material.RequiredQuantity.UnitOfMeasure, material.ScrapPercent, material.AlternateGroup)).ToArray(),
            order.Issues.Select(issue => new ProductionIssueReadModel(issue.OperationId, issue.ComponentItemId, issue.ComponentVariantId, issue.Quantity.Value, issue.Quantity.UnitOfMeasure, issue.UnitCost.Amount, issue.UnitCost.Currency)).ToArray(),
            order.Receipts.Select(receipt => new ProductionOutputReadModel(receipt.OperationId, receipt.Quantity.Value, receipt.Quantity.UnitOfMeasure, receipt.UnitCost.Amount, receipt.UnitCost.Currency)).ToArray(),
            order.Scrap.Select(scrap => new ProductionScrapReadModel(scrap.OperationId, scrap.Quantity.Value, scrap.Quantity.UnitOfMeasure, scrap.UnitCost.Amount, scrap.UnitCost.Currency)).ToArray());
    }

    internal static void EnsureCompany(ProductionOrder order, ICompanyContext? company)
    {
        if (company?.CompanyId is { } active && order.CompanyId != active)
        {
            throw ManufacturingRuleException.NotFound(order.Id);
        }
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
public sealed class GetProductionCapacityQueryHandler(IProductionOrderRepository orders, ICompanyContext? company = null)
    : IQueryHandler<GetProductionCapacityQuery, ProductionCapacity>
{
    /// <inheritdoc />
    public async Task<ProductionCapacity> HandleAsync(GetProductionCapacityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ProductionOrder order = await orders.FindAsync(query.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(query.ProductionOrderId);
        GetProductionOrderQueryHandler.EnsureCompany(order, company);
        ProductionSnapshot snapshot = order.Snapshot
            ?? throw ManufacturingRuleException.PublishedProductionBomRequired();
        decimal setup = snapshot.RoutingSteps.Sum(step => step.SetupMinutes ?? 0m);
        decimal run = snapshot.RoutingSteps.Sum(step => step.RunMinutes ?? 0m) * order.PlannedQuantity.Value;
        return new ProductionCapacity(order.Id, order.PlannedQuantity.Value, order.PlannedQuantity.UnitOfMeasure, setup, run, setup + run);
    }

}
