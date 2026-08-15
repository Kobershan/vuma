using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Catalog;

public sealed class ItemTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid UnitOfMeasureId = UuidV7.NewGuid();

    [Fact]
    public void An_item_code_is_upper_cased()
    {
        Item item = Item.Create(TenantId, "milk-2l", "Full cream milk 2L", ItemType.Stock, UnitOfMeasureId);

        item.Code.Should().Be("MILK-2L");
    }

    [Fact]
    public void An_item_starts_active_with_no_variants()
    {
        Item item = Item.Create(TenantId, "MILK-2L", "Full cream milk 2L", ItemType.Stock, UnitOfMeasureId);

        item.IsActive.Should().BeTrue();
        item.HasVariants.Should().BeFalse();
    }

    [Fact]
    public void An_item_cannot_exist_without_a_tenant()
    {
        Action creating = () => Item.Create(Guid.Empty, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_item_cannot_exist_without_a_base_unit_of_measure()
    {
        Action creating = () => Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, Guid.Empty);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adding_a_variant_marks_the_item_as_selling_only_through_variants()
    {
        Item item = Item.Create(TenantId, "SHIRT", "T-Shirt", ItemType.Stock, UnitOfMeasureId);

        ItemVariant variant = item.AddVariant("SHIRT-M-RED", [VariantAttribute.Create("Size", "M"), VariantAttribute.Create("Colour", "Red")]);

        item.HasVariants.Should().BeTrue();
        variant.ItemId.Should().Be(item.Id);
        variant.Sku.Should().Be("SHIRT-M-RED");
        variant.Attributes.Should().HaveCount(2);
        variant.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Updating_details_changes_name_description_and_tax_class()
    {
        Item item = Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId);

        item.SetDetails("Full cream milk 2L", "Fresh full cream milk, 2 litre bottle", "VAT-STD");

        item.Name.Should().Be("Full cream milk 2L");
        item.Description.Should().Be("Fresh full cream milk, 2 litre bottle");
        item.TaxClassCode.Should().Be("VAT-STD");
    }

    [Fact]
    public void Updating_details_can_clear_the_optional_fields()
    {
        Item item = Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId, "old", "VAT-STD");

        item.SetDetails("Milk", null, null);

        item.Description.Should().BeNull();
        item.TaxClassCode.Should().BeNull();
    }

    [Fact]
    public void Deactivating_an_item_keeps_its_record()
    {
        Item item = Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId);

        item.Deactivate();

        item.IsActive.Should().BeFalse();
        item.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_deactivated_item_can_be_reactivated()
    {
        Item item = Item.Create(TenantId, "MILK-2L", "Milk", ItemType.Stock, UnitOfMeasureId);
        item.Deactivate();

        item.Activate();

        item.IsActive.Should().BeTrue();
    }
}
