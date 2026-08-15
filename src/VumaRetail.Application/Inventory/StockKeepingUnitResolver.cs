using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Inventory;

/// <summary>
/// Resolves an item/variant reference to its base unit of measure, so a movement's quantity can be
/// checked against it before it ever reaches <see cref="StockLedgerPoster"/>.
/// </summary>
/// <remarks>
/// The one place inventory reaches into <c>catalog</c> — through its published repository ports
/// (<c>CONVENTIONS.md</c> §2's "modules reference each other by ID and resolve through published
/// contracts"), never through a schema reference. No foreign key in <c>inventory.*</c> points at
/// <c>catalog.*</c>; every id is a plain column the application layer validates.
/// </remarks>
public interface IStockKeepingUnitResolver
{
    /// <summary>
    /// Resolves the unit-of-measure code a stock-keeping unit is counted in.
    /// </summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="CatalogNotFoundException">The item, variant or its unit of measure does not exist in this tenant.</exception>
    Task<string> ResolveUnitOfMeasureCodeAsync(Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStockKeepingUnitResolver" />
/// <param name="items">Item lookup.</param>
/// <param name="variants">Variant lookup.</param>
/// <param name="units">Unit-of-measure lookup.</param>
public sealed class StockKeepingUnitResolver(
    IItemRepository items,
    IItemVariantRepository variants,
    IUnitOfMeasureRepository units) : IStockKeepingUnitResolver
{
    /// <inheritdoc />
    public async Task<string> ResolveUnitOfMeasureCodeAsync(
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
    {
        Guid unitOfMeasureId;

        if (itemId is { } id)
        {
            Item item = await items.FindAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item", id);

            unitOfMeasureId = item.UnitOfMeasureId;
        }
        else if (itemVariantId is { } variantId)
        {
            ItemVariant variant = await variants.FindAsync(variantId, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item variant", variantId);

            Item parent = await items.FindAsync(variant.ItemId, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item", variant.ItemId);

            unitOfMeasureId = parent.UnitOfMeasureId;
        }
        else
        {
            throw Domain.Inventory.InventoryRuleException.ExactlyOneItemOrVariantRequired();
        }

        UnitOfMeasure unit = await units.FindAsync(unitOfMeasureId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("unit of measure", unitOfMeasureId);

        return unit.Code;
    }
}
