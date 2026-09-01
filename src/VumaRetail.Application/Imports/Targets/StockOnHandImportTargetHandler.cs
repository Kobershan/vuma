using System.Globalization;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// Imports counted or opening stock as Stage 08 ledger adjustments.
/// </summary>
/// <remarks>
/// <para>
/// <b>This target writes no column, and that is the whole design.</b> §7 rule 6 says stock levels are
/// the projection of an append-only ledger, so an import that "sets stock to 40" is a lie there is no
/// way to write. What it does instead is post the difference between what the books say and what the
/// sheet says, through <see cref="IStockLedgerPoster"/>, referenced back to the batch. The ledger then
/// explains where every unit came from, and ADR-068's weighted-average cost is maintained by the same
/// arithmetic that maintains it for a receipt.
/// </para>
/// <para>
/// <b>The quantity column is a level, not a movement.</b> A stock sheet says "we have 40", not "add
/// 40" — that is what a person means when they hand over a stocktake, and reading it as a delta is
/// how re-uploading a corrected file doubles somebody's stock. The delta is computed here, and a row
/// whose figure already matches the books is a no-op reported as a skip rather than a zero-quantity
/// ledger entry nobody can interpret.
/// </para>
/// <para>
/// <b>An increase needs a cost the first time.</b> Stock arriving into a balance that does not exist
/// yet has no average to be valued at, and inventing one would put a fictional number into cost of
/// sales. The row is refused naming the cost field, which is a thing a person can fix in their
/// spreadsheet.
/// </para>
/// <para>
/// <b>Rollback posts the opposite entry, never a deletion (rule 5).</b> The compensating entry carries
/// the same batch reference, so the two sit next to each other in the ledger and the balance ends
/// where it started.
/// </para>
/// </remarks>
/// <param name="items">Item lookup, for resolving the row's code.</param>
/// <param name="variants">Variant lookup, for a file keyed on SKUs.</param>
/// <param name="barcodes">Barcode lookup, for a file keyed on scan codes.</param>
/// <param name="units">Unit-of-measure lookup, for the unit a movement is expressed in.</param>
/// <param name="locations">Stock location lookup.</param>
/// <param name="balances">The on-hand projection, read to turn a level into a delta.</param>
/// <param name="ledger">The append-only ledger, read to find the entry a rollback must reverse.</param>
/// <param name="poster">The one way a stock movement is written (Stage 08).</param>
public sealed class StockOnHandImportTargetHandler(
    IItemRepository items,
    IItemVariantRepository variants,
    IBarcodeRepository barcodes,
    IUnitOfMeasureRepository units,
    IStockLocationRepository locations,
    IStockBalanceRepository balances,
    IStockLedgerRepository ledger,
    IStockLedgerPoster poster) : ImportTargetHandler
{
    /// <summary>The item code, variant SKU or barcode the row counts.</summary>
    public const string SkuField = "sku";

    /// <summary>The stock location's code.</summary>
    public const string LocationField = "location";

    /// <summary>How many are on hand — a level, not a movement.</summary>
    public const string QuantityField = "quantity";

    private const string UnitCostField = "unitCost";

    /// <inheritdoc />
    public override ImportTargetKind Kind => ImportTargetKind.StockOnHand;

    /// <inheritdoc />
    public override ImportTargetDescriptor Descriptor => new(
        ImportTargetKind.StockOnHand,
        "Stock on hand",
        "Opening or counted stock. Each row says how many are on hand at a location; the import posts "
        + "the difference to the stock ledger, so the books explain where the number came from and can "
        + "be rolled back. Nothing overwrites a stock level — stock is never a column.",
        RequiresStore: true,
        [SkuField, LocationField],
        [
            new(
                SkuField,
                ImportFieldType.Text,
                IsRequired: true,
                "What is being counted — an item code, a variant SKU, or a barcode. All three are "
                + "tried, in that order, because stock sheets come off scanners as often as off "
                + "spreadsheets.",
                "MILK-1L",
                ["item code", "stock code", "product code", "code", "barcode", "ean", "article",
                    "item", "plu"]),
            new(
                LocationField,
                ImportFieldType.Text,
                IsRequired: true,
                "The code of the stock location being counted. Bind a constant default when the whole "
                + "sheet is one storeroom, which most stock counts are.",
                "MAIN",
                ["warehouse", "location code", "store", "storeroom", "site", "bin", "from location"]),
            new(
                QuantityField,
                ImportFieldType.Quantity,
                IsRequired: true,
                "How many are on hand — the figure the sheet states, not a movement. The import works "
                + "out the difference from what the books already say.",
                "40",
                ["qty", "quantity on hand", "on hand", "onhand", "count", "counted", "stock",
                    "soh", "balance"]),
            new(
                UnitCostField,
                ImportFieldType.Money,
                IsRequired: false,
                "What one unit cost. Required the first time stock appears at a location, because "
                + "there is no existing average to value it at; ignored where the row reduces stock, "
                + "which always relieves at the current average.",
                "12.50",
                ["cost", "cost price", "unit cost", "average cost", "buy price", "purchase price"]),
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
        string sku = RequiredText(values, SkuField);
        string locationCode = RequiredText(values, LocationField);

        (Guid? itemId, Guid? variantId, _) = await ResolveSkuAsync(sku, cancellationToken)
            .ConfigureAwait(false);

        if (itemId is null && variantId is null)
        {
            errors.Add(ImportRowError.UnknownReference(
                SkuField, sku, "item, variant or barcode"));
        }

        StockLocation? location = await locations
            .FindByCodeAsync(locationCode, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            errors.Add(ImportRowError.UnknownReference(LocationField, locationCode, "stock location"));
        }

        decimal counted = Number(values, QuantityField) ?? 0m;

        if (counted < 0m)
        {
            errors.Add(ImportRowError.OutOfRange(
                QuantityField,
                $"'{counted.ToString(CultureInfo.InvariantCulture)}' is a negative stock level. A "
                + "count says how many are there; if stock is missing, count it as zero."));
        }

        if (errors.Count > 0 || location is null)
        {
            return ImportSemanticVerdict.Invalid(errors);
        }

        StockBalance? balance = await balances
            .FindAsync(location.Id, itemId, variantId, cancellationToken)
            .ConfigureAwait(false);

        decimal onHand = balance?.QuantityOnHand.Value ?? 0m;
        decimal delta = counted - onHand;

        if (delta == 0m)
        {
            // The books already agree with the sheet. Reported as a skip so the preview shows how much
            // of the file is a no-op, rather than posting a zero-quantity entry the ledger cannot
            // explain. The balance is what the row matched, and Guid.Empty where it has never moved
            // here and the sheet also says zero — nothing is applied either way.
            return ImportSemanticVerdict.Skip(balance?.Id ?? Guid.Empty);
        }

        if (delta > 0m && balance is null && Amount(values, UnitCostField, context) is null)
        {
            errors.Add(ImportRowError.Required(UnitCostField));
            errors.Add(ImportRowError.OutOfRange(
                UnitCostField,
                $"'{sku}' has no stock at '{locationCode}' yet, so this row opens its balance and "
                + "there is no existing average cost to value it at."));

            return ImportSemanticVerdict.Invalid(errors);
        }

        if (delta < 0m && onHand + delta < 0m)
        {
            errors.Add(ImportRowError.OutOfRange(
                QuantityField,
                $"Reducing to {counted.ToString(CultureInfo.InvariantCulture)} would take stock below "
                + $"zero — the books say {onHand.ToString(CultureInfo.InvariantCulture)} is on hand."));

            return ImportSemanticVerdict.Invalid(errors);
        }

        // Every applied row posts a movement, so there is nothing here for the duplicate strategy to
        // decide: a stock level is not a record that can already exist. The strategy governs
        // Suppliers, Customers, Items and Price list lines, and this target's description says so
        // rather than pretending to honour a setting it cannot act on.
        return ImportSemanticVerdict.WouldCreate();
    }

    /// <inheritdoc />
    public override async Task<ImportApplyResult> ApplyAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);

        string sku = RequiredText(values, SkuField);
        string locationCode = RequiredText(values, LocationField);

        (Guid? itemId, Guid? variantId, string? unitCode) = await ResolveSkuAsync(sku, cancellationToken)
            .ConfigureAwait(false);

        StockLocation location = await locations.FindByCodeAsync(locationCode, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Stock location '{locationCode}' reached the target handler unresolved. Validation "
                + "should have refused this row.");

        StockBalance? balance = await balances
            .FindAsync(location.Id, itemId, variantId, cancellationToken)
            .ConfigureAwait(false);

        decimal counted = Number(values, QuantityField) ?? 0m;
        decimal onHand = balance?.QuantityOnHand.Value ?? 0m;
        decimal delta = counted - onHand;

        StockLedgerEntry entry = await poster.AdjustForImportAsync(
                location,
                itemId,
                variantId,
                new Quantity(delta, unitCode ?? balance?.QuantityOnHand.UnitOfMeasure ?? "EA"),
                Amount(values, UnitCostField, context),
                context.BatchId,
                $"Imported by {context.BatchNumber}",
                cancellationToken)
            .ConfigureAwait(false);

        return ImportApplyResult.Movement(entry.Id);
    }

    /// <inheritdoc />
    public override Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Reversing a movement is always structurally possible — it is another movement, and the
        // ledger is append-only. What it cannot do is drive a balance negative, which happens when the
        // import added stock and the shop has since sold more than it had before. That check needs the
        // entry, so it is here rather than left for the poster to throw mid-rollback.
        return CheckReversibleAsync(row, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        (StockLedgerEntry? entry, StockLocation? location) = await LoadMovementAsync(row, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || location is null)
        {
            return null;
        }

        StockLedgerEntry reversal = await poster.AdjustForImportAsync(
                location,
                entry.ItemId,
                entry.ItemVariantId,
                -entry.Quantity,
                entry.UnitCost,
                context.BatchId,
                $"Reversal of {context.BatchNumber} row "
                + $"{row.RowNumber.ToString(CultureInfo.InvariantCulture)}",
                cancellationToken)
            .ConfigureAwait(false);

        return reversal.Id;
    }

    /// <summary>Checks that reversing a row's movement would not take the balance below zero.</summary>
    /// <param name="row">The committed row.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task<ImportRollbackObstacle?> CheckReversibleAsync(
        ImportRow row, CancellationToken cancellationToken)
    {
        (StockLedgerEntry? entry, StockLocation? location) = await LoadMovementAsync(row, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || location is null || entry.Quantity.Value <= 0m)
        {
            return null;
        }

        StockBalance? balance = await balances
            .FindAsync(location.Id, entry.ItemId, entry.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        decimal onHand = balance?.QuantityOnHand.Value ?? 0m;

        return onHand >= entry.Quantity.Value
            ? null
            : new ImportRollbackObstacle(
                row.RowNumber,
                $"{entry.Quantity.Value.ToString(CultureInfo.InvariantCulture)} were imported at "
                + $"{location.Code} and only {onHand.ToString(CultureInfo.InvariantCulture)} are left, "
                + "so taking the import back would put stock below zero");
    }

    /// <summary>Loads the ledger entry a committed row posted, and the location it posted at.</summary>
    /// <param name="row">The committed row.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task<(StockLedgerEntry? Entry, StockLocation? Location)> LoadMovementAsync(
        ImportRow row, CancellationToken cancellationToken)
    {
        if (row.TargetEntityId is not { } entryId)
        {
            return (null, null);
        }

        StockLedgerEntry? entry = await ledger
            .FindAsync(entryId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return (null, null);
        }

        StockLocation? location = await locations
            .FindAsync(entry.LocationId, cancellationToken)
            .ConfigureAwait(false);

        return (entry, location);
    }

    /// <summary>
    /// Resolves what a stock row is counting: an item, a variant, or whatever a barcode points at.
    /// </summary>
    /// <param name="sku">The cell.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The item, the variant, and the unit of measure to express the movement in.</returns>
    /// <remarks>
    /// Item code first, then variant SKU, then barcode. That order is deliberate: a shop's own item
    /// codes are the identifiers they typed into their own sheet, and a barcode that happens to equal
    /// somebody's item code should not outrank the code it collides with.
    /// </remarks>
    private async Task<(Guid? ItemId, Guid? VariantId, string? UnitCode)> ResolveSkuAsync(
        string sku, CancellationToken cancellationToken)
    {
        Item? item = await items.FindByCodeAsync(sku, cancellationToken).ConfigureAwait(false);

        if (item is not null && !item.HasVariants)
        {
            return (item.Id, null, await UnitCodeAsync(item, cancellationToken).ConfigureAwait(false));
        }

        ItemVariant? variant = await variants.FindBySkuAsync(sku, cancellationToken).ConfigureAwait(false);

        if (variant is not null)
        {
            Item? parent = await items.FindAsync(variant.ItemId, cancellationToken).ConfigureAwait(false);

            return (null, variant.Id, parent is null
                ? null
                : await UnitCodeAsync(parent, cancellationToken).ConfigureAwait(false));
        }

        Barcode? barcode = await barcodes.FindByCodeAsync(sku, cancellationToken).ConfigureAwait(false);

        if (barcode is null)
        {
            return (null, null, null);
        }

        if (barcode.ItemVariantId is { } variantId)
        {
            ItemVariant? scanned = await variants.FindAsync(variantId, cancellationToken).ConfigureAwait(false);
            Item? parent = scanned is null
                ? null
                : await items.FindAsync(scanned.ItemId, cancellationToken).ConfigureAwait(false);

            return (null, variantId, parent is null
                ? null
                : await UnitCodeAsync(parent, cancellationToken).ConfigureAwait(false));
        }

        Item? owner = barcode.ItemId is { } ownerId
            ? await items.FindAsync(ownerId, cancellationToken).ConfigureAwait(false)
            : null;

        return owner is null
            ? (null, null, null)
            : (owner.Id, null, await UnitCodeAsync(owner, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>An item's base unit-of-measure code, which is how its quantities are expressed.</summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task<string?> UnitCodeAsync(Item item, CancellationToken cancellationToken)
    {
        UnitOfMeasure? unit = await units.FindAsync(item.UnitOfMeasureId, cancellationToken).ConfigureAwait(false);

        return unit?.Code;
    }
}
