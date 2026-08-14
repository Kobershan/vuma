using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog.Commands;
using VumaRetail.Application.Catalog.Queries;
using VumaRetail.Domain.Catalog;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Catalog;

/// <summary>
/// The Stage 06 <c>catalog</c> command handlers against real PostgreSQL and real migrations
/// (<c>docs/TESTING.md</c> §2).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Creating_an_item_requires_an_active_unit_of_measure_belonging_to_the_tenant()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);

        Func<Task> creating = () => harness.SendAsync(
            new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, Guid.NewGuid()));

        (await creating.Should().ThrowAsync<CatalogRuleException>())
            .Which.Code.Should().Be("CATALOG_UOM_NOT_AVAILABLE");
    }

    [Fact]
    public async Task Refuses_a_second_item_with_the_same_code()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));

        Func<Task> again = () => harness.SendAsync(new CreateItemCommand("milk-2l", "Milk again", ItemType.Stock, unitId));

        (await again.Should().ThrowAsync<CatalogConflictException>())
            .Which.Code.Should().Be("CATALOG_ITEM_CODE_TAKEN");
    }

    [Fact]
    public async Task Deactivating_an_item_does_not_delete_it()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));

        await harness.SendAsync(new DeactivateItemCommand(itemId));

        Item item = (await harness.Items.FindAsync(itemId))!;
        item.IsActive.Should().BeFalse();
        item.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Adding_a_variant_is_refused_once_the_item_already_carries_a_direct_barcode()
    {
        // Application-layer half of the "sells as itself or as a variant, never both" rule — the
        // domain enforces the reverse half (Barcode.CreateForItem) directly.
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("SHIRT", "T-Shirt", ItemType.Stock, unitId));
        await harness.SendAsync(new AddBarcodeCommand(itemId, null, "6001234567890", BarcodeSymbology.Ean13));

        Func<Task> addingVariant = () => harness.SendAsync(
            new CreateItemVariantCommand(itemId, "SHIRT-M-RED", [new VariantAttribute("Size", "M")]));

        (await addingVariant.Should().ThrowAsync<CatalogRuleException>())
            .Which.Code.Should().Be("CATALOG_ITEM_HAS_DIRECT_BARCODES");
    }

    [Fact]
    public async Task A_barcode_is_refused_on_an_item_that_already_has_a_variant()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("SHIRT", "T-Shirt", ItemType.Stock, unitId));
        await harness.SendAsync(new CreateItemVariantCommand(itemId, "SHIRT-M-RED", [new VariantAttribute("Size", "M")]));

        Func<Task> addingBarcode = () => harness.SendAsync(
            new AddBarcodeCommand(itemId, null, "6001234567890", BarcodeSymbology.Ean13));

        (await addingBarcode.Should().ThrowAsync<CatalogRuleException>())
            .Which.Code.Should().Be("CATALOG_BARCODE_ITEM_HAS_VARIANTS");
    }

    [Fact]
    public async Task A_barcode_value_is_unique_tenant_wide_regardless_of_symbology()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid firstItem = await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));
        Guid secondItem = await harness.SendAsync(new CreateItemCommand("MILK-1L", "Milk 1L", ItemType.Stock, unitId));
        await harness.SendAsync(new AddBarcodeCommand(firstItem, null, "6001234567890", BarcodeSymbology.Ean13));

        Func<Task> colliding = () => harness.SendAsync(
            new AddBarcodeCommand(secondItem, null, "6001234567890", BarcodeSymbology.Internal));

        (await colliding.Should().ThrowAsync<CatalogConflictException>())
            .Which.Code.Should().Be("CATALOG_BARCODE_TAKEN");
    }

    [Fact]
    public async Task An_owners_first_barcode_is_always_primary()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));

        Guid barcodeId = await harness.SendAsync(
            new AddBarcodeCommand(itemId, null, "6001234567890", BarcodeSymbology.Ean13, IsPrimary: false));

        (await harness.Barcodes.FindAsync(barcodeId))!.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Setting_a_new_primary_demotes_the_old_one()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));
        Guid first = await harness.SendAsync(new AddBarcodeCommand(itemId, null, "6001234567890", BarcodeSymbology.Ean13));
        Guid second = await harness.SendAsync(new AddBarcodeCommand(itemId, null, "6009876543210", BarcodeSymbology.Internal));

        await harness.SendAsync(new SetPrimaryBarcodeCommand(second));

        (await harness.Barcodes.FindAsync(first))!.IsPrimary.Should().BeFalse();
        (await harness.Barcodes.FindAsync(second))!.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_the_primary_barcode_promotes_a_remaining_sibling()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);
        Guid itemId = await harness.SendAsync(new CreateItemCommand("MILK-2L", "Milk", ItemType.Stock, unitId));
        Guid first = await harness.SendAsync(new AddBarcodeCommand(itemId, null, "6001234567890", BarcodeSymbology.Ean13));
        Guid second = await harness.SendAsync(new AddBarcodeCommand(itemId, null, "6009876543210", BarcodeSymbology.Internal));

        await harness.SendAsync(new RemoveBarcodeCommand(first));

        (await harness.Barcodes.FindAsync(first)).Should().BeNull();
        (await harness.Barcodes.FindAsync(second))!.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task A_derived_unit_of_measure_converts_to_an_existing_base_unit()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid baseUnitId = await harness.SendAsync(new CreateUnitOfMeasureCommand("EA", "Each", UnitOfMeasureType.Count));

        Guid derivedId = await harness.SendAsync(
            new CreateUnitOfMeasureCommand("BOX12", "Box of 12", UnitOfMeasureType.Count, baseUnitId, 12m));

        UnitOfMeasure derived = (await harness.Units.FindAsync(derivedId))!;
        derived.BaseUnitOfMeasureId.Should().Be(baseUnitId);
        derived.ConversionFactorToBase.Should().Be(12m);
    }

    [Fact]
    public async Task Refuses_a_second_unit_of_measure_with_the_same_code()
    {
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        await harness.SendAsync(new CreateUnitOfMeasureCommand("EA", "Each", UnitOfMeasureType.Count));

        Func<Task> again = () => harness.SendAsync(new CreateUnitOfMeasureCommand("ea", "Each again", UnitOfMeasureType.Count));

        (await again.Should().ThrowAsync<CatalogConflictException>())
            .Which.Code.Should().Be("CATALOG_UOM_CODE_TAKEN");
    }

    [Fact]
    public async Task Listing_items_returns_a_stable_keyset_page_across_a_concurrent_insert()
    {
        // The acceptance test docs/PROGRESS.md's pagination promise is about: a caller mid-page must
        // never see a row twice or miss one because a new row landed alphabetically before the cursor.
        await using CatalogHarness harness = await CatalogHarness.CreateAsync(fixture);
        Guid unitId = await CreateEachAsync(harness);

        for (int index = 0; index < 5; index++)
        {
            await harness.SendAsync(new CreateItemCommand($"ITEM-{index:00}", $"Item {index:00}", ItemType.Stock, unitId));
        }

        PageResult<ItemResult> firstPage = await harness.QueryAsync(new ListItemsQuery(Limit: 2));
        firstPage.Items.Should().HaveCount(2);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

        // A row that sorts before the cursor arrives between pages — it must not appear on page two,
        // and nothing already handed out may repeat or be skipped.
        await harness.SendAsync(new CreateItemCommand("ITEM-00A", "Inserted mid-page", ItemType.Stock, unitId));

        PageResult<ItemResult> secondPage = await harness.QueryAsync(new ListItemsQuery(Limit: 2, After: firstPage.NextCursor));

        secondPage.Items.Select(item => item.Code)
            .Should().NotIntersectWith(firstPage.Items.Select(item => item.Code));

        secondPage.Items.Select(item => item.Code).Should().OnlyContain(code => string.CompareOrdinal(code, "ITEM-01") > 0);
    }

    private static async Task<Guid> CreateEachAsync(CatalogHarness harness)
        => await harness.SendAsync(new CreateUnitOfMeasureCommand("EA", "Each", UnitOfMeasureType.Count));
}
