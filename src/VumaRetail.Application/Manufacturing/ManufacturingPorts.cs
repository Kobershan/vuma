using VumaRetail.Domain.Manufacturing;

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
