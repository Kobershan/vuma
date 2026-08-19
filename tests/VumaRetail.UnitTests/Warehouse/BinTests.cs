using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>Zone and bin construction, normalisation and lifecycle.</summary>
public sealed class BinTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ZoneId = UuidV7.NewGuid();

    [Fact]
    public void A_zone_normalises_its_code_to_upper_case()
    {
        Zone zone = Zone.Create(TenantId, StoreId, LocationId, "rcv-01", "Receiving Dock", ZoneType.Receiving);

        zone.Code.Should().Be("RCV-01");
        zone.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_zone_can_be_deactivated_and_reactivated()
    {
        Zone zone = Zone.Create(TenantId, StoreId, LocationId, "STOR-A", "Storage A", ZoneType.Storage);

        zone.Deactivate();
        zone.IsActive.Should().BeFalse();

        zone.Activate();
        zone.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_bin_normalises_its_code_and_carries_its_zones_location()
    {
        Bin bin = Bin.Create(TenantId, StoreId, LocationId, ZoneId, "a-01-01", "Aisle A shelf 1", BinType.Shelf);

        bin.Code.Should().Be("A-01-01");
        bin.LocationId.Should().Be(LocationId);
        bin.ZoneId.Should().Be(ZoneId);
        bin.IsActive.Should().BeTrue();
        bin.Capacity.Should().BeNull();
    }

    [Fact]
    public void A_bin_may_carry_an_informational_capacity()
    {
        Bin bin = Bin.Create(
            TenantId, StoreId, LocationId, ZoneId, "PAL-01", "Pallet position 1", BinType.Pallet,
            new Quantity(40m, "EA"));

        bin.Capacity.Should().NotBeNull();
        bin.Capacity!.Value.Value.Should().Be(40m);
        bin.Capacity.Value.UnitOfMeasure.Should().Be("EA");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_bins_capacity_when_supplied_must_be_positive(decimal capacity)
    {
        Action creating = () => Bin.Create(
            TenantId, StoreId, LocationId, ZoneId, "BAD-01", "Bad bin", BinType.Shelf, new Quantity(capacity, "EA"));

        creating.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_bin_can_be_deactivated_and_reactivated()
    {
        Bin bin = Bin.Create(TenantId, StoreId, LocationId, ZoneId, "A-01-02", "Aisle A shelf 2", BinType.Shelf);

        bin.Deactivate();
        bin.IsActive.Should().BeFalse();

        bin.Activate();
        bin.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_zone_must_belong_to_a_tenant_and_a_location()
    {
        Action noTenant = () => Zone.Create(Guid.Empty, StoreId, LocationId, "Z1", "Zone 1", ZoneType.Storage);
        Action noLocation = () => Zone.Create(TenantId, StoreId, Guid.Empty, "Z1", "Zone 1", ZoneType.Storage);

        noTenant.Should().Throw<ArgumentException>();
        noLocation.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_bin_must_belong_to_a_tenant_a_location_and_a_zone()
    {
        Action noTenant = () => Bin.Create(Guid.Empty, StoreId, LocationId, ZoneId, "B1", "Bin 1", BinType.Shelf);
        Action noLocation = () => Bin.Create(TenantId, StoreId, Guid.Empty, ZoneId, "B1", "Bin 1", BinType.Shelf);
        Action noZone = () => Bin.Create(TenantId, StoreId, LocationId, Guid.Empty, "B1", "Bin 1", BinType.Shelf);

        noTenant.Should().Throw<ArgumentException>();
        noLocation.Should().Throw<ArgumentException>();
        noZone.Should().Throw<ArgumentException>();
    }
}
