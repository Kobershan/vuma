using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Catalog;

public sealed class ItemVariantTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid UnitOfMeasureId = UuidV7.NewGuid();

    private static Item NewItem() => Item.Create(TenantId, "SHIRT", "T-Shirt", ItemType.Stock, UnitOfMeasureId);

    [Fact]
    public void A_variant_sku_is_upper_cased()
    {
        ItemVariant variant = NewItem().AddVariant("shirt-m-red", [VariantAttribute.Create("Size", "M")]);

        variant.Sku.Should().Be("SHIRT-M-RED");
    }

    [Fact]
    public void A_variant_starts_active()
    {
        ItemVariant variant = NewItem().AddVariant("SHIRT-M-RED", []);

        variant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Attributes_are_kept_in_the_order_they_were_set()
    {
        ItemVariant variant = NewItem().AddVariant(
            "SHIRT-M-RED",
            [VariantAttribute.Create("Size", "M"), VariantAttribute.Create("Colour", "Red")]);

        variant.Attributes.Select(attribute => attribute.Name).Should().ContainInOrder("Size", "Colour");
    }

    [Fact]
    public void Attributes_can_be_replaced_wholesale()
    {
        ItemVariant variant = NewItem().AddVariant("SHIRT-M-RED", [VariantAttribute.Create("Size", "M")]);

        variant.ReplaceAttributes([VariantAttribute.Create("Size", "L"), VariantAttribute.Create("Colour", "Blue")]);

        variant.Attributes.Should().HaveCount(2);
        variant.Attributes[0].Should().Be(new VariantAttribute("Size", "L"));
    }

    [Fact]
    public void A_variant_attribute_pair_cannot_be_blank()
    {
        Action creating = () => VariantAttribute.Create("Size", "  ");

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivating_a_variant_keeps_its_record()
    {
        ItemVariant variant = NewItem().AddVariant("SHIRT-M-RED", []);

        variant.Deactivate();

        variant.IsActive.Should().BeFalse();
        variant.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_deactivated_variant_can_be_reactivated()
    {
        ItemVariant variant = NewItem().AddVariant("SHIRT-M-RED", []);
        variant.Deactivate();

        variant.Activate();

        variant.IsActive.Should().BeTrue();
    }
}
