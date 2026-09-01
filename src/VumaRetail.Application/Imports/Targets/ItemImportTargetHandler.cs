using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// Imports a product list into Stage 06 <see cref="Item"/> rows, with their barcodes.
/// </summary>
/// <remarks>
/// <para>
/// This is the target most shops use first, because it is the one that decides whether they can start
/// trading this week or next month. It is also the one with the most ways to go quietly wrong, and
/// each is handled explicitly below rather than left to chance.
/// </para>
/// <para>
/// <b>The unit of measure is resolved, never created.</b> A cell reading <c>EACH</c> where the tenant
/// has <c>EA</c> is a row error naming the unit, not a new unit of measure. Letting an import invent
/// units is how a catalogue ends up with <c>EA</c>, <c>EACH</c>, <c>Each</c> and <c>ea.</c> as four
/// different things, none of which convert to each other.
/// </para>
/// <para>
/// <b>A barcode is part of the row, not a second import.</b> A product file has a barcode column, and
/// making somebody import the catalogue and then import barcodes against it is two chances to get the
/// keys wrong. A barcode already attached to <em>another</em> item is a row error rather than a
/// silent move — two products sharing a scan code is the fault that shows up at a till, in front of a
/// customer, weeks later.
/// </para>
/// <para>
/// <b>Only items with no variants are imported.</b> A variant matrix is a structure, not a row: it
/// needs the attribute names, their permitted values and their combinations, none of which a flat
/// file carries. A file naming an item that already sells through variants is refused on that row with
/// an explanation, rather than creating a second, variantless copy of a product the shop already has.
/// </para>
/// </remarks>
/// <param name="items">Item lookup and insertion.</param>
/// <param name="units">Unit-of-measure lookup.</param>
/// <param name="barcodes">Barcode lookup, insertion and removal.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="usage">Whether anything now references an item this import created.</param>
/// <param name="principal">Who is acting — stamped onto a soft delete.</param>
/// <param name="clock">The only source of time (<c>CONVENTIONS.md</c> §6).</param>
public sealed class ItemImportTargetHandler(
    IItemRepository items,
    IUnitOfMeasureRepository units,
    IBarcodeRepository barcodes,
    ITenantContext tenant,
    IImportUsageProbe usage,
    IPrincipalAccessor principal,
    IClock clock) : ImportTargetHandler
{
    /// <summary>The item code — the natural key an import matches duplicates on.</summary>
    public const string CodeField = "code";

    /// <summary>The item's name, as it prints on a receipt.</summary>
    public const string NameField = "name";

    /// <summary>The base unit of measure's code.</summary>
    public const string UnitOfMeasureField = "unitOfMeasure";

    /// <summary>The scannable code, when the file carries one.</summary>
    public const string BarcodeField = "barcode";

    private const string DescriptionField = "description";
    private const string ItemTypeField = "itemType";
    private const string TaxClassField = "taxClassCode";
    private const string BarcodeSymbologyField = "barcodeSymbology";

    /// <inheritdoc />
    public override ImportTargetKind Kind => ImportTargetKind.Items;

    /// <inheritdoc />
    public override ImportTargetDescriptor Descriptor => new(
        ImportTargetKind.Items,
        "Items",
        "The product catalogue — what the shop sells, what unit it sells it in, and what scans at the "
        + "till. Does not set prices or stock: those are the Price list lines and Stock on hand "
        + "targets, because they arrive in different files at different times.",
        RequiresStore: false,
        [CodeField],
        [
            new(
                CodeField,
                ImportFieldType.Text,
                IsRequired: true,
                "The item's unique code in this tenant. Rows are matched on it, so it is what decides "
                + "whether a row creates or updates.",
                "MILK-1L",
                ["sku", "item code", "stock code", "product code", "article", "article code",
                    "item no", "item number", "plu"]),
            new(
                NameField,
                ImportFieldType.Text,
                IsRequired: true,
                "What the item is called — this is what prints on a receipt and shows on a till button.",
                "Full Cream Milk 1L",
                ["description", "item description", "product", "product name", "item name", "desc"]),
            new(
                UnitOfMeasureField,
                ImportFieldType.Text,
                IsRequired: true,
                "The code of the unit this item is counted, weighed or measured in. It must already "
                + "exist — an import resolves units, it never invents them. Bind a constant default "
                + "when every row is the same unit, which most product files are.",
                "EA",
                ["uom", "unit", "unit of measure", "base unit", "sell unit", "measure"]),
            new(
                BarcodeField,
                ImportFieldType.Text,
                IsRequired: false,
                "The code that scans at the till. Left alone if the item already has it; refused if "
                + "another item already has it.",
                "6001234567890",
                ["ean", "ean13", "upc", "barcode number", "scan code", "bar code"]),
            new(
                BarcodeSymbologyField,
                ImportFieldType.Text,
                IsRequired: false,
                $"What kind of barcode it is. One of: {NamesOf<BarcodeSymbology>()}. Defaults to "
                + "Ean13, which is what a South African retail barcode is.",
                "Ean13",
                ["symbology", "barcode type", "code type"]),
            new(
                DescriptionField,
                ImportFieldType.Text,
                IsRequired: false,
                "A longer description, for a catalogue or a storefront. Not what prints on a receipt.",
                "Pasteurised full cream milk, 1 litre carton",
                ["long description", "notes", "detail", "extended description"]),
            new(
                ItemTypeField,
                ImportFieldType.Text,
                IsRequired: false,
                $"One of: {NamesOf<ItemType>()}. Defaults to Stock — a product file is a file of "
                + "things that sit on a shelf.",
                "Stock",
                ["type", "item type", "product type", "stock type"]),
            new(
                TaxClassField,
                ImportFieldType.Text,
                IsRequired: false,
                "The tax class code this item is taxed under. Left to the tenant default when empty.",
                "STD",
                ["tax class", "tax code", "vat code", "vat class", "tax group"]),
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
        string code = RequiredText(values, CodeField);
        string unitCode = RequiredText(values, UnitOfMeasureField);

        UnitOfMeasure? unit = await units.FindByCodeAsync(unitCode, cancellationToken).ConfigureAwait(false);

        if (unit is null)
        {
            errors.Add(ImportRowError.UnknownReference(UnitOfMeasureField, unitCode, "unit of measure"));
        }

        if (!TryEnum(values, ItemTypeField, ItemType.Stock, out ItemType _))
        {
            errors.Add(ImportRowError.OutOfRange(
                ItemTypeField,
                $"'{Text(values, ItemTypeField)}' is not an item type. One of: {NamesOf<ItemType>()}."));
        }

        if (!TryEnum(values, BarcodeSymbologyField, BarcodeSymbology.Ean13, out BarcodeSymbology _))
        {
            errors.Add(ImportRowError.OutOfRange(
                BarcodeSymbologyField,
                $"'{Text(values, BarcodeSymbologyField)}' is not a barcode symbology. One of: "
                + $"{NamesOf<BarcodeSymbology>()}."));
        }

        Item? existing = await items.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (Text(values, BarcodeField) is { } barcodeValue)
        {
            Barcode? owner = await barcodes.FindByCodeAsync(barcodeValue, cancellationToken).ConfigureAwait(false);

            if (owner is not null && owner.ItemId != existing?.Id)
            {
                errors.Add(ImportRowError.OutOfRange(
                    BarcodeField,
                    $"'{barcodeValue}' is already the barcode of something else. Two products sharing "
                    + "a scan code is a fault that only shows up at a till, in front of a customer."));
            }
        }

        if (existing is null)
        {
            return errors.Count > 0 ? ImportSemanticVerdict.Invalid(errors) : ImportSemanticVerdict.WouldCreate();
        }

        if (existing.HasVariants)
        {
            errors.Add(ImportRowError.OutOfRange(
                CodeField,
                $"Item '{code}' sells through variants, and a flat file cannot describe a variant "
                + "matrix. Maintain its variants in the catalogue instead."));

            return ImportSemanticVerdict.Invalid(errors);
        }

        switch (context.DuplicateStrategy)
        {
            case ImportDuplicateStrategy.Skip:
                return ImportSemanticVerdict.Skip(existing.Id);

            case ImportDuplicateStrategy.Fail:
                errors.Add(ImportRowError.Duplicate(CodeField, code));
                return ImportSemanticVerdict.Invalid(errors);

            default:
                return errors.Count > 0
                    ? ImportSemanticVerdict.Invalid(errors)
                    : ImportSemanticVerdict.WouldUpdate(existing.Id);
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

        string code = RequiredText(values, CodeField);
        string name = RequiredText(values, NameField);
        string unitCode = RequiredText(values, UnitOfMeasureField);
        string? description = Text(values, DescriptionField);
        string? taxClass = Text(values, TaxClassField);

        TryEnum(values, ItemTypeField, ItemType.Stock, out ItemType itemType);
        TryEnum(values, BarcodeSymbologyField, BarcodeSymbology.Ean13, out BarcodeSymbology symbology);

        UnitOfMeasure unit = await units.FindByCodeAsync(unitCode, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Unit of measure '{unitCode}' reached the target handler unresolved. Validation "
                + "should have refused this row.");

        Item? existing = await items.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            Item created = Item.Create(tenant.TenantId, code, name, itemType, unit.Id, description, taxClass);

            items.Add(created);

            await AttachBarcodeAsync(created, Text(values, BarcodeField), symbology, cancellationToken)
                .ConfigureAwait(false);

            return ImportApplyResult.Created(created.Id);
        }

        IReadOnlyList<Barcode> before = await barcodes
            .ListForItemAsync(existing.Id, cancellationToken)
            .ConfigureAwait(false);

        string beforeImage = WriteBeforeImage(ItemBeforeImage.Of(existing, before));

        existing.SetDetails(name, description, taxClass);

        await AttachBarcodeAsync(existing, Text(values, BarcodeField), symbology, cancellationToken)
            .ConfigureAwait(false);

        return ImportApplyResult.Updated(existing.Id, beforeImage);
    }

    /// <inheritdoc />
    public override async Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Outcome is not ImportRowOutcome.Created || row.TargetEntityId is not { } id)
        {
            return null;
        }

        Item? item = await items.FindAsync(id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        string? reference = await usage.DescribeItemUsageAsync(item.Id, cancellationToken).ConfigureAwait(false);

        return reference is null
            ? null
            : new ImportRollbackObstacle(
                row.RowNumber,
                $"item {item.Code} has been used since it was imported ({reference})");
    }

    /// <inheritdoc />
    public override async Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        Item? item = row.TargetEntityId is { } id
            ? await items.FindAsync(id, cancellationToken).ConfigureAwait(false)
            : null;

        if (item is null)
        {
            return null;
        }

        IReadOnlyList<Barcode> current = await barcodes
            .ListForItemAsync(item.Id, cancellationToken)
            .ConfigureAwait(false);

        if (row.Outcome is ImportRowOutcome.Created)
        {
            // Every barcode on an item this import created was put there by this import, so all of
            // them go. Barcodes are the one deliberate hard delete in the catalogue (Stage 06's ADR):
            // a soft-deleted barcode would keep occupying its scan code, and the next import of the
            // same file would be refused as a duplicate of something nobody can see.
            foreach (Barcode barcode in current)
            {
                barcodes.Remove(barcode);
            }

            item.MarkDeleted(principal.Principal, clock.UtcNow);

            return null;
        }

        ItemBeforeImage before = ReadBeforeImage<ItemBeforeImage>(row);

        item.SetDetails(before.Name, before.Description, before.TaxClassCode);

        if (before.IsActive)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        // Only the barcodes this import added come off. One the shop added by hand between the commit
        // and the rollback is not this import's to remove.
        foreach (Barcode barcode in current)
        {
            if (!before.Barcodes.Contains(barcode.Code, StringComparer.OrdinalIgnoreCase))
            {
                barcodes.Remove(barcode);
            }
        }

        return null;
    }

    /// <summary>Attaches a barcode if the row carries one the item does not already have.</summary>
    /// <param name="item">The item.</param>
    /// <param name="code">The scanned value, or <c>null</c>.</param>
    /// <param name="symbology">What kind of barcode it is.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Idempotent on purpose: re-running the same product file must not fail on the second pass, which
    /// is exactly what a shop does when it fixes three rows and re-uploads. Validation has already
    /// refused a code that belongs to a different item, so a match here is this item's own.
    /// </remarks>
    private async Task AttachBarcodeAsync(
        Item item, string? code, BarcodeSymbology symbology, CancellationToken cancellationToken)
    {
        if (code is null)
        {
            return;
        }

        Barcode? existing = await barcodes.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        barcodes.Add(Barcode.CreateForItem(item, code, symbology));
    }
}

/// <summary>The item state a rollback restores, and nothing else.</summary>
/// <param name="Name">The name before the import.</param>
/// <param name="Description">The description before the import.</param>
/// <param name="TaxClassCode">The tax class before the import.</param>
/// <param name="IsActive">Whether the item was on sale.</param>
/// <param name="Barcodes">
/// The scan codes the item carried before the import, so compensation removes the ones this import
/// added and leaves alone the ones somebody added by hand afterwards.
/// </param>
public sealed record ItemBeforeImage(
    string Name,
    string? Description,
    string? TaxClassCode,
    bool IsActive,
    IReadOnlyList<string> Barcodes)
{
    /// <summary>Captures an item's current state.</summary>
    /// <param name="item">The item, before it is changed.</param>
    /// <param name="barcodes">Its barcodes, before it is changed.</param>
    public static ItemBeforeImage Of(Item item, IReadOnlyList<Barcode> barcodes)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(barcodes);

        return new ItemBeforeImage(
            item.Name,
            item.Description,
            item.TaxClassCode,
            item.IsActive,
            [.. barcodes.Select(barcode => barcode.Code)]);
    }
}
