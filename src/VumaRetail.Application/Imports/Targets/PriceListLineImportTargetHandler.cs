using System.Globalization;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// Imports prices onto a Stage 10 price list — what a shop means when it says "the specials sheet".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is what "import specials" means.</b> R5 says specials, and what a shop actually
/// receives from a supplier is a list of products and prices with a date range on the covering email.
/// That is a <em>price list with an effective window</em>, which Stage 10 already models. A
/// <c>Promotion</c> is a reward <em>rule</em> — buy two get one, ten percent off a basket over R500,
/// happy hour — and a rule is not a row of values, so no file can carry one. Importing prices covers
/// what a specials sheet contains; the rule engine stays Stage 10's, configured by hand.
/// </para>
/// <para>
/// <b>The list is named per row, and must already exist.</b> An import that created price lists would
/// let a typo in one cell open a second "SPECIALS" list at a different priority, silently outranking
/// the real one at the till. The row is refused naming the list instead.
/// </para>
/// <para>
/// <b>The price is in the list's currency, not the batch's.</b> Every other target denominates money
/// in the store's currency; a price list carries its own, and every line on it was authored in that
/// currency. Reading a euro list's cells as rand would be silent and catastrophic, so the list wins
/// and a batch currency that disagrees is simply not consulted here.
/// </para>
/// <para>
/// <b>Duplicates are the ordinary case.</b> The natural key is (list, stock-keeping unit, quantity
/// break), and next month's sheet is the same key at a new price — so <c>Update</c> reprices,
/// <c>Skip</c> leaves last month's price standing, and <c>Fail</c> reports every clash, which is what
/// somebody uploading a supposedly-new list wants to see.
/// </para>
/// </remarks>
/// <param name="priceLists">Price list lookup.</param>
/// <param name="items">Item lookup.</param>
/// <param name="variants">Variant lookup.</param>
/// <param name="barcodes">Barcode lookup.</param>
/// <param name="principal">Who is acting — stamped onto a soft delete.</param>
/// <param name="clock">The only source of time (<c>CONVENTIONS.md</c> §6).</param>
public sealed class PriceListLineImportTargetHandler(
    IPriceListRepository priceLists,
    IItemRepository items,
    IItemVariantRepository variants,
    IBarcodeRepository barcodes,
    IPrincipalAccessor principal,
    IClock clock) : ImportTargetHandler
{
    /// <summary>The code of the price list the row prices onto.</summary>
    public const string PriceListField = "priceList";

    /// <summary>The item code, variant SKU or barcode being priced.</summary>
    public const string SkuField = "sku";

    /// <summary>What one unit sells for.</summary>
    public const string UnitPriceField = "unitPrice";

    private const string MinimumQuantityField = "minimumQuantity";

    /// <inheritdoc />
    public override ImportTargetKind Kind => ImportTargetKind.PriceListLines;

    /// <inheritdoc />
    public override ImportTargetDescriptor Descriptor => new(
        ImportTargetKind.PriceListLines,
        "Price list lines",
        "Prices onto an existing price list — a supplier's monthly sheet, a promotional list with an "
        + "effective window, a wholesale tier. The list must already exist and its dates decide when "
        + "the prices apply; this import only puts the numbers on it.",
        RequiresStore: false,
        [PriceListField, SkuField, MinimumQuantityField],
        [
            new(
                PriceListField,
                ImportFieldType.Text,
                IsRequired: true,
                "The code of the price list these prices go onto. It must already exist — bind a "
                + "constant default when the whole sheet is one list, which a supplier's sheet always "
                + "is.",
                "SPECIALS-MAR",
                ["price list", "list", "list code", "price list code", "pricelist"]),
            new(
                SkuField,
                ImportFieldType.Text,
                IsRequired: true,
                "What is being priced — an item code, a variant SKU, or a barcode.",
                "MILK-1L",
                ["item code", "stock code", "product code", "code", "barcode", "ean", "article",
                    "item", "plu"]),
            new(
                UnitPriceField,
                ImportFieldType.Money,
                IsRequired: true,
                "What one unit sells for, in the price list's own currency. Whether that figure "
                + "includes tax is a property of the list, not of this column.",
                "18.99",
                ["price", "sell price", "selling price", "retail", "retail price", "unit price",
                    "rrp", "amount", "new price"]),
            new(
                MinimumQuantityField,
                ImportFieldType.Decimal,
                IsRequired: false,
                "The quantity this price starts applying at — a quantity break. Defaults to 1, the "
                + "ordinary single-unit price.",
                "1",
                ["min qty", "minimum qty", "break", "break quantity", "qty break", "from qty",
                    "pack size"]),
        ]);

    /// <inheritdoc />
    public override async Task<ImportSemanticVerdict> ValidateAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);

        List<ImportRowError> errors = [];
        string listCode = RequiredText(values, PriceListField);
        string sku = RequiredText(values, SkuField);

        PriceList? list = await priceLists.FindByCodeAsync(listCode, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            errors.Add(ImportRowError.UnknownReference(PriceListField, listCode, "price list"));
        }

        (Guid? itemId, Guid? variantId) = await ResolveSkuAsync(sku, cancellationToken).ConfigureAwait(false);

        if (itemId is null && variantId is null)
        {
            errors.Add(ImportRowError.UnknownReference(SkuField, sku, "item, variant or barcode"));
        }

        decimal price = Number(values, UnitPriceField) ?? 0m;

        if (price < 0m)
        {
            errors.Add(ImportRowError.OutOfRange(
                UnitPriceField,
                $"'{price.ToString(CultureInfo.InvariantCulture)}' is a negative price. A discount is "
                + "a promotion, not a price below zero."));
        }

        decimal minimumQuantity = Number(values, MinimumQuantityField) ?? 1m;

        if (minimumQuantity <= 0m)
        {
            errors.Add(ImportRowError.OutOfRange(
                MinimumQuantityField,
                $"'{minimumQuantity.ToString(CultureInfo.InvariantCulture)}' is not a quantity a price "
                + "can start applying at. Leave the cell empty for the ordinary single-unit price."));
        }

        if (errors.Count > 0 || list is null)
        {
            return ImportSemanticVerdict.Invalid(errors);
        }

        PriceListLine? existing = FindLine(list, itemId, variantId, minimumQuantity);

        if (existing is null)
        {
            return ImportSemanticVerdict.WouldCreate();
        }

        switch (context.DuplicateStrategy)
        {
            case ImportDuplicateStrategy.Skip:
                return ImportSemanticVerdict.Skip(existing.Id);

            case ImportDuplicateStrategy.Fail:
                return ImportSemanticVerdict.Invalid(
                    [ImportRowError.Duplicate(SkuField, sku)]);

            default:
                return ImportSemanticVerdict.WouldUpdate(existing.Id);
        }
    }

    /// <inheritdoc />
    public override async Task<ImportApplyResult> ApplyAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);

        string listCode = RequiredText(values, PriceListField);
        string sku = RequiredText(values, SkuField);

        PriceList list = await priceLists.FindByCodeAsync(listCode, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Price list '{listCode}' reached the target handler unresolved. Validation should "
                + "have refused this row.");

        (Guid? itemId, Guid? variantId) = await ResolveSkuAsync(sku, cancellationToken).ConfigureAwait(false);

        decimal minimumQuantity = Number(values, MinimumQuantityField) ?? 1m;

        // The list's currency, not the batch's — see the type's remarks.
        Money price = new(Number(values, UnitPriceField) ?? 0m, list.Currency);

        PriceListLine? existing = FindLine(list, itemId, variantId, minimumQuantity);

        if (existing is null)
        {
            PriceListLine created = list.AddLine(itemId, variantId, price, minimumQuantity);

            return ImportApplyResult.Created(created.Id);
        }

        string beforeImage = WriteBeforeImage(new PriceListLineBeforeImage(existing.UnitPrice));

        existing.Reprice(price);

        return ImportApplyResult.Updated(existing.Id, beforeImage);
    }

    /// <inheritdoc />
    public override Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
        // Nothing in the system points at a price list line. A sale records the price it charged, not
        // the row the price came from (Stage 10's PriceResolver copies the value), so removing a line
        // or putting its old price back leaves no dangling reference behind.
        => Task.FromResult<ImportRollbackObstacle?>(null);

    /// <inheritdoc />
    public override async Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        if (row.TargetEntityId is not { } lineId)
        {
            return null;
        }

        string listCode = row.NormalisedValues.TryGetValue(PriceListField, out string? code) ? code ?? string.Empty : string.Empty;

        PriceList? list = string.IsNullOrWhiteSpace(listCode)
            ? null
            : await priceLists.FindByCodeAsync(listCode, cancellationToken).ConfigureAwait(false);

        PriceListLine? line = list?.Lines.FirstOrDefault(candidate => candidate.Id == lineId);

        if (line is null)
        {
            return null;
        }

        if (row.Outcome is ImportRowOutcome.Created)
        {
            line.MarkDeleted(principal.Principal, clock.UtcNow);

            return null;
        }

        line.Reprice(ReadBeforeImage<PriceListLineBeforeImage>(row).UnitPrice);

        return null;
    }

    /// <summary>The line already on the list for this stock-keeping unit at this exact break.</summary>
    /// <param name="list">The list.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="minimumQuantity">The quantity break.</param>
    /// <remarks>
    /// Exact equality on the break, not <c>PriceList.FindPrice</c>'s "highest break at or below".
    /// Resolution asks "what does this cost at quantity five"; an import asks "is this row already
    /// here", and answering the first question would have a five-unit row silently reprice the
    /// single-unit line.
    /// </remarks>
    private static PriceListLine? FindLine(
        PriceList list, Guid? itemId, Guid? itemVariantId, decimal minimumQuantity)
        => list.Lines.FirstOrDefault(line
            => line.ItemId == itemId
            && line.ItemVariantId == itemVariantId
            && line.MinimumQuantity == minimumQuantity);

    /// <summary>Resolves what a price row is pricing: an item, a variant, or what a barcode points at.</summary>
    /// <param name="sku">The cell.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task<(Guid? ItemId, Guid? VariantId)> ResolveSkuAsync(
        string sku, CancellationToken cancellationToken)
    {
        Item? item = await items.FindByCodeAsync(sku, cancellationToken).ConfigureAwait(false);

        if (item is not null && !item.HasVariants)
        {
            return (item.Id, null);
        }

        ItemVariant? variant = await variants.FindBySkuAsync(sku, cancellationToken).ConfigureAwait(false);

        if (variant is not null)
        {
            return (null, variant.Id);
        }

        Barcode? barcode = await barcodes.FindByCodeAsync(sku, cancellationToken).ConfigureAwait(false);

        return barcode is null ? (null, null) : (barcode.ItemId, barcode.ItemVariantId);
    }
}

/// <summary>The price a line carried before an import repriced it.</summary>
/// <param name="UnitPrice">What one unit sold for before.</param>
public sealed record PriceListLineBeforeImage(Money UnitPrice);
