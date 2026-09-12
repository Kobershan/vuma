using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Domain.Manufacturing;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core repository for Stage 16 BOM definitions.</summary>
public sealed class BillOfMaterialsRepository(VumaRetailDbContext context) : IBillOfMaterialsRepository
{
    /// <inheritdoc />
    public Task<BillOfMaterials?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.BillOfMaterials.FirstOrDefaultAsync(bom => bom.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<BillOfMaterials?> FindVersionAsync(Guid itemId, Guid? variantId, int version, CancellationToken cancellationToken = default)
        => context.BillOfMaterials.FirstOrDefaultAsync(
            bom => bom.FinishedItemId == itemId && bom.FinishedVariantId == variantId && bom.Version == version,
            cancellationToken);

    /// <inheritdoc />
    public void Add(BillOfMaterials bom) => context.BillOfMaterials.Add(bom);
}

/// <summary>EF Core repository for Stage 17 production orders.</summary>
public sealed class ProductionOrderRepository(VumaRetailDbContext context) : IProductionOrderRepository
{
    /// <inheritdoc />
    public Task<ProductionOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ProductionOrders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(ProductionOrder order) => context.ProductionOrders.Add(order);
}
