using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog;

/// <summary>Reads and writes <see cref="UnitOfMeasure"/> rows.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, CLAUDE.md §7 rule 2).
/// </remarks>
public interface IUnitOfMeasureRepository
{
    /// <summary>Finds a unit of measure by id.</summary>
    Task<UnitOfMeasure?> FindAsync(Guid unitOfMeasureId, CancellationToken cancellationToken = default);

    /// <summary>Finds a unit of measure by its code, case-insensitively.</summary>
    Task<UnitOfMeasure?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Lists every unit of measure in the tenant, ordered by code.</summary>
    Task<IReadOnlyList<UnitOfMeasure>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new unit of measure.</summary>
    void Add(UnitOfMeasure unitOfMeasure);
}

/// <summary>Reads and writes <see cref="Item"/> rows.</summary>
public interface IItemRepository
{
    /// <summary>Finds an item by id.</summary>
    Task<Item?> FindAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Finds an item by its code, case-insensitively.</summary>
    Task<Item?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// A keyset page of items ordered by code, then id. See <c>docs/API_STANDARDS.md</c> §8.
    /// </summary>
    /// <param name="after">The cursor of the last row the caller already has, or <c>null</c> for the first page.</param>
    /// <param name="limit">How many rows to return, already clamped by the query handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Up to <paramref name="limit"/> items, and whether at least one more exists beyond them.</returns>
    Task<(IReadOnlyList<Item> Items, bool HasMore)> ListPageAsync(
        Application.Abstractions.KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new item.</summary>
    void Add(Item item);
}

/// <summary>Reads and writes <see cref="ItemVariant"/> rows.</summary>
public interface IItemVariantRepository
{
    /// <summary>Finds a variant by id.</summary>
    Task<ItemVariant?> FindAsync(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Finds a variant by its SKU, case-insensitively.</summary>
    Task<ItemVariant?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Adds a new variant.</summary>
    void Add(ItemVariant variant);
}

/// <summary>Reads and writes <see cref="Barcode"/> rows.</summary>
public interface IBarcodeRepository
{
    /// <summary>Finds a barcode by id.</summary>
    Task<Barcode?> FindAsync(Guid barcodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a barcode by its scanned value, tenant-wide and regardless of symbology — a scanner reads
    /// a code, not a type.
    /// </summary>
    Task<Barcode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Every barcode attached directly to an item.</summary>
    Task<IReadOnlyList<Barcode>> ListForItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Every barcode attached to a variant.</summary>
    Task<IReadOnlyList<Barcode>> ListForVariantAsync(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new barcode.</summary>
    void Add(Barcode barcode);

    /// <summary>
    /// Removes a barcode outright. The one deliberate hard delete in this stage — see
    /// <c>docs/DECISIONS.md</c> for the ADR.
    /// </summary>
    void Remove(Barcode barcode);
}
