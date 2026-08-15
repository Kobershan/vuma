using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Catalog;

public sealed class BarcodeTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid UnitOfMeasureId = UuidV7.NewGuid();

    private static Item NewItem() => Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId);

    [Fact]
    public void A_barcode_can_attach_directly_to_an_item_with_no_variants()
    {
        Item item = NewItem();

        Barcode barcode = Barcode.CreateForItem(item, "6001234567890", BarcodeSymbology.Ean13);

        barcode.ItemId.Should().Be(item.Id);
        barcode.ItemVariantId.Should().BeNull();
        barcode.TenantId.Should().Be(item.TenantId);
    }

    [Fact]
    public void A_barcode_cannot_attach_directly_to_an_item_that_already_has_a_variant()
    {
        // An item sells "as itself" or "as a variant", never both — the other half of this rule
        // (CreateItemVariantCommandHandler refusing a variant on an item with a direct barcode) lives
        // in the application layer because it needs the barcode repository.
        Item item = NewItem();
        item.AddVariant("MILK-2L-STD", []);

        Action attaching = () => Barcode.CreateForItem(item, "6001234567890", BarcodeSymbology.Ean13);

        attaching.Should().Throw<CatalogRuleException>().Which.Code.Should().Be("CATALOG_BARCODE_ITEM_HAS_VARIANTS");
    }

    [Fact]
    public void A_barcode_can_attach_to_a_variant()
    {
        Item item = NewItem();
        ItemVariant variant = item.AddVariant("MILK-2L-STD", []);

        Barcode barcode = Barcode.CreateForVariant(variant, "6001234567890", BarcodeSymbology.Ean13);

        barcode.ItemVariantId.Should().Be(variant.Id);
        barcode.ItemId.Should().BeNull();
    }

    [Fact]
    public void Setting_a_new_primary_demotes_the_previous_one_atomically()
    {
        Item item = NewItem();
        Barcode first = Barcode.CreateForItem(item, "6001234567890", BarcodeSymbology.Ean13);
        Barcode second = Barcode.CreateForItem(item, "6009876543210", BarcodeSymbology.Internal);
        Barcode.SetPrimary([first, second], first.Id);

        Barcode.SetPrimary([first, second], second.Id);

        first.IsPrimary.Should().BeFalse();
        second.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void Setting_a_primary_that_is_not_among_the_owners_barcodes_is_refused()
    {
        Item item = NewItem();
        Barcode barcode = Barcode.CreateForItem(item, "6001234567890", BarcodeSymbology.Ean13);

        Action setting = () => Barcode.SetPrimary([barcode], UuidV7.NewGuid());

        setting.Should().Throw<CatalogNotFoundException>();
    }
}
