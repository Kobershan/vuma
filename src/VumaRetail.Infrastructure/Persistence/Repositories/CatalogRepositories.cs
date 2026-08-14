using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IUnitOfMeasureRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class UnitOfMeasureRepository(VumaRetailDbContext context) : IUnitOfMeasureRepository
{
    /// <inheritdoc />
    public Task<UnitOfMeasure?> FindAsync(Guid unitOfMeasureId, CancellationToken cancellationToken = default)
        => context.UnitsOfMeasure.FirstOrDefaultAsync(unit => unit.Id == unitOfMeasureId, cancellationToken);

    /// <inheritdoc />
    public Task<UnitOfMeasure?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.UnitsOfMeasure.FirstOrDefaultAsync(unit => unit.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitOfMeasure>> ListAsync(CancellationToken cancellationToken = default)
        => await context.UnitsOfMeasure
            .AsNoTracking()
            .OrderBy(unit => unit.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(UnitOfMeasure unitOfMeasure) => context.UnitsOfMeasure.Add(unitOfMeasure);
}

/// <summary>EF Core implementation of <see cref="IItemRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ItemRepository(VumaRetailDbContext context) : IItemRepository
{
    /// <inheritdoc />
    public Task<Item?> FindAsync(Guid itemId, CancellationToken cancellationToken = default)
        => context.Items.FirstOrDefaultAsync(item => item.Id == itemId, cancellationToken);

    /// <inheritdoc />
    public Task<Item?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Items.FirstOrDefaultAsync(item => item.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Item> Items, bool HasMore)> ListPageAsync(
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Item> query = context.Items.AsNoTracking();

        if (after is { } cursor)
        {
            // Keyset, not offset (docs/API_STANDARDS.md §8): compare the composite (code, id) tuple
            // rather than skipping a row count, so a concurrent insert never shifts what "page two"
            // means for a caller already holding a cursor.
            query = query.Where(item => item.Code.CompareTo(cursor.SortKey) > 0
                || (item.Code == cursor.SortKey && item.Id.CompareTo(cursor.Id) > 0));
        }

        List<Item> page = await query
            .OrderBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return (page, hasMore);
    }

    /// <inheritdoc />
    public void Add(Item item) => context.Items.Add(item);
}

/// <summary>EF Core implementation of <see cref="IItemVariantRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ItemVariantRepository(VumaRetailDbContext context) : IItemVariantRepository
{
    /// <inheritdoc />
    public Task<ItemVariant?> FindAsync(Guid variantId, CancellationToken cancellationToken = default)
        => context.ItemVariants.FirstOrDefaultAsync(variant => variant.Id == variantId, cancellationToken);

    /// <inheritdoc />
    public Task<ItemVariant?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        string normalized = sku.Trim().ToUpperInvariant();

        return context.ItemVariants.FirstOrDefaultAsync(variant => variant.Sku == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(ItemVariant variant) => context.ItemVariants.Add(variant);
}

/// <summary>EF Core implementation of <see cref="IBarcodeRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class BarcodeRepository(VumaRetailDbContext context) : IBarcodeRepository
{
    /// <inheritdoc />
    public Task<Barcode?> FindAsync(Guid barcodeId, CancellationToken cancellationToken = default)
        => context.Barcodes.FirstOrDefaultAsync(barcode => barcode.Id == barcodeId, cancellationToken);

    /// <inheritdoc />
    public Task<Barcode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string trimmed = code.Trim();

        return context.Barcodes.FirstOrDefaultAsync(barcode => barcode.Code == trimmed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Barcode>> ListForItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => await context.Barcodes
            .Where(barcode => barcode.ItemId == itemId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Barcode>> ListForVariantAsync(Guid variantId, CancellationToken cancellationToken = default)
        => await context.Barcodes
            .Where(barcode => barcode.ItemVariantId == variantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Barcode barcode) => context.Barcodes.Add(barcode);

    /// <inheritdoc />
    public void Remove(Barcode barcode) => context.Barcodes.Remove(barcode);
}
