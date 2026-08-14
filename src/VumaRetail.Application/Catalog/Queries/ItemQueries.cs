using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog.Queries;

/// <summary>
/// An item as read back out of the catalog. Application's own shape — <c>CLAUDE.md</c> §7 rule 1 keeps
/// Application dependent on Domain only, so the mapping into a wire DTO happens at the Web edge.
/// </summary>
/// <param name="Id">The item's id.</param>
/// <param name="Code">The item's code.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Description">The item's description, or <c>null</c>.</param>
/// <param name="ItemType">Stocked, service, or non-stock.</param>
/// <param name="UnitOfMeasureId">The base unit of measure.</param>
/// <param name="TaxClassCode">The tax class code, or <c>null</c>.</param>
/// <param name="IsActive">Whether the item may still be sold or received.</param>
/// <param name="HasVariants">Whether the item sells only through its variants.</param>
public sealed record ItemResult(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    ItemType ItemType,
    Guid UnitOfMeasureId,
    string? TaxClassCode,
    bool IsActive,
    bool HasVariants);

/// <summary>Reads one item by id.</summary>
/// <param name="ItemId">The item.</param>
public sealed record GetItemQuery(Guid ItemId) : IQuery<ItemResult>;

/// <summary>Reads an item.</summary>
/// <param name="items">Item lookup.</param>
public sealed class GetItemQueryHandler(IItemRepository items) : IQueryHandler<GetItemQuery, ItemResult>
{
    /// <inheritdoc />
    public async Task<ItemResult> HandleAsync(GetItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Item item = await items.FindAsync(query.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item", query.ItemId);

        return ToResult(item);
    }

    internal static ItemResult ToResult(Item item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Description,
        item.ItemType,
        item.UnitOfMeasureId,
        item.TaxClassCode,
        item.IsActive,
        item.HasVariants);
}

/// <summary>
/// A keyset-paginated page of items, ordered by code (<c>docs/API_STANDARDS.md</c> §8).
/// </summary>
/// <param name="Limit">How many rows to return. Clamped to <see cref="Paging.MaxLimit"/>.</param>
/// <param name="After">The opaque cursor of the last row the caller already has, or <c>null</c> for the first page.</param>
public sealed record ListItemsQuery(int? Limit = null, string? After = null) : IQuery<PageResult<ItemResult>>;

/// <summary>Lists items a page at a time.</summary>
/// <param name="items">Item lookup.</param>
public sealed class ListItemsQueryHandler(IItemRepository items)
    : IQueryHandler<ListItemsQuery, PageResult<ItemResult>>
{
    /// <inheritdoc />
    public async Task<PageResult<ItemResult>> HandleAsync(
        ListItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        KeysetCursor? after = KeysetCursor.TryDecode(query.After);

        (IReadOnlyList<Item> page, bool hasMore) = await items
            .ListPageAsync(after, limit, cancellationToken)
            .ConfigureAwait(false);

        string? nextCursor = hasMore && page.Count > 0
            ? new KeysetCursor(page[^1].Code, page[^1].Id).Encode()
            : null;

        return new PageResult<ItemResult>([.. page.Select(GetItemQueryHandler.ToResult)], nextCursor, hasMore);
    }
}
