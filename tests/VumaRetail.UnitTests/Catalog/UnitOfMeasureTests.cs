using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Catalog;

public sealed class UnitOfMeasureTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    [Fact]
    public void A_base_unit_converts_to_itself_at_a_factor_of_one()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "ea", "Each", UnitOfMeasureType.Count);

        each.Code.Should().Be("EA");
        each.IsBaseUnit.Should().BeTrue();
        each.BaseUnitOfMeasureId.Should().BeNull();
        each.ConversionFactorToBase.Should().Be(1m);
    }

    [Fact]
    public void A_derived_unit_takes_its_kind_from_the_base_it_converts_to()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);

        UnitOfMeasure box = UnitOfMeasure.CreateDerived(TenantId, "box12", "Box of 12", each, 12m);

        box.Type.Should().Be(UnitOfMeasureType.Count);
        box.BaseUnitOfMeasureId.Should().Be(each.Id);
        box.ConversionFactorToBase.Should().Be(12m);
        box.IsBaseUnit.Should().BeFalse();
    }

    [Fact]
    public void A_derived_unit_may_only_convert_to_a_base_unit_not_to_another_derived_unit()
    {
        // One level of conversion, never a chain (the entity's own remarks) — a derived unit cannot
        // itself be somebody else's base.
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);
        UnitOfMeasure box = UnitOfMeasure.CreateDerived(TenantId, "BOX12", "Box of 12", each, 12m);

        Action creating = () => UnitOfMeasure.CreateDerived(TenantId, "PALLET", "Pallet", box, 100m);

        creating.Should().Throw<CatalogRuleException>().Which.Code.Should().Be("CATALOG_UOM_NOT_A_BASE_UNIT");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_conversion_factor_must_be_positive(decimal factor)
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);

        Action creating = () => UnitOfMeasure.CreateDerived(TenantId, "BOX12", "Box of 12", each, factor);

        creating.Should().Throw<CatalogRuleException>().Which.Code.Should().Be("CATALOG_UOM_FACTOR_NOT_POSITIVE");
    }

    [Fact]
    public void A_unit_of_measure_cannot_exist_without_a_tenant()
    {
        Action creating = () => UnitOfMeasure.CreateBase(Guid.Empty, "EA", "Each", UnitOfMeasureType.Count);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivating_a_unit_leaves_existing_items_unaffected_but_stops_new_use()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);

        each.Deactivate();

        each.IsActive.Should().BeFalse();
        each.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_deactivated_unit_can_be_reactivated()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);
        each.Deactivate();

        each.Activate();

        each.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Renaming_a_unit_updates_its_name_only()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);

        each.Rename("Individual unit");

        each.Name.Should().Be("Individual unit");
        each.Code.Should().Be("EA");
    }
}
