using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Pos;

namespace VumaRetail.Application.Pos;

/// <summary>
/// What the till needs to know about a thing before it can ring it up: what to print on the receipt,
/// what unit it sells in, and which tax rule applies to it.
/// </summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What the receipt says. Snapshotted onto the line at ring-up.</param>
/// <param name="UnitOfMeasureCode">The unit the quantity must be expressed in.</param>
/// <param name="TaxClassCode">
/// The tax code to price the line under. Falls back to the tenant's default when the item names none,
/// because an item with no tax class is a configuration gap, not a zero-rated sale — and a till that
/// quietly zero-rates is how a VAT audit goes badly.
/// </param>
public sealed record SellableItem(
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    string UnitOfMeasureCode,
    string TaxClassCode);

/// <summary>
/// Resolves a stock-keeping unit — or a scanned barcode — into everything a sale line needs.
/// </summary>
/// <remarks>
/// The one place POS reaches into <c>catalog</c>, through its published repository ports the way
/// <c>StockKeepingUnitResolver</c> does (<c>CONVENTIONS.md</c> §2). No foreign key in <c>pos.*</c>
/// points at <c>catalog.*</c>; every id is a plain column this service validates.
/// </remarks>
public interface ISellableItemResolver
{
    /// <summary>Resolves an item or variant reference.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="CatalogNotFoundException">Nothing in this tenant matches.</exception>
    /// <exception cref="PosRuleException">Neither or both of the two ids were supplied, or the item is not sellable.</exception>
    Task<SellableItem> ResolveAsync(Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);

    /// <summary>Resolves a scanned barcode.</summary>
    /// <param name="barcode">The scanned digits, already stripped of any embedded price or weight.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="CatalogNotFoundException">No barcode in this tenant matches.</exception>
    Task<SellableItem> ResolveBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISellableItemResolver" />
/// <param name="items">Item lookup.</param>
/// <param name="variants">Variant lookup.</param>
/// <param name="units">Unit-of-measure lookup.</param>
/// <param name="barcodes">Barcode lookup.</param>
public sealed class SellableItemResolver(
    IItemRepository items,
    IItemVariantRepository variants,
    IUnitOfMeasureRepository units,
    IBarcodeRepository barcodes) : ISellableItemResolver
{
    /// <summary>
    /// The tax code used when an item names none. Matches the <c>STANDARD</c> rule
    /// <c>DemoSeed</c> writes and <c>CLAUDE.md</c> §9's South African default; the *rate* behind it is
    /// still tenant data resolved by the rules engine, so this is a code, not a rate.
    /// </summary>
    public const string DefaultTaxClassCode = "STANDARD";

    /// <inheritdoc />
    public async Task<SellableItem> ResolveAsync(
        Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
    {
        SaleLine.EnsureExactlyOneStockKeepingUnit(itemId, itemVariantId);

        if (itemId is { } id)
        {
            Item item = await items.FindAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item", id);

            // An item that has variants is not itself sellable — the variant is the thing on the shelf.
            // Stage 06 models it that way and Stage 08's ledger follows it; letting a till ring up the
            // parent would create a balance nothing can ever relieve.
            if (item.HasVariants)
            {
                throw PosRuleException.ExactlyOneItemOrVariantRequired();
            }

            return await BuildAsync(item, itemId: item.Id, itemVariantId: null, item.Name, cancellationToken)
                .ConfigureAwait(false);
        }

        Guid variantId = itemVariantId!.Value;

        ItemVariant variant = await variants.FindAsync(variantId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item variant", variantId);

        Item parent = await items.FindAsync(variant.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item", variant.ItemId);

        return await BuildAsync(parent, itemId: null, itemVariantId: variant.Id, $"{parent.Name} {variant.Sku}", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SellableItem> ResolveBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        Barcode match = await barcodes.FindByCodeAsync(barcode, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("barcode", Guid.Empty);

        return await ResolveAsync(match.ItemId, match.ItemVariantId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SellableItem> BuildAsync(
        Item item, Guid? itemId, Guid? itemVariantId, string description, CancellationToken cancellationToken)
    {
        UnitOfMeasure unit = await units.FindAsync(item.UnitOfMeasureId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("unit of measure", item.UnitOfMeasureId);

        return new SellableItem(
            itemId,
            itemVariantId,
            description,
            unit.Code,
            string.IsNullOrWhiteSpace(item.TaxClassCode) ? DefaultTaxClassCode : item.TaxClassCode);
    }
}
