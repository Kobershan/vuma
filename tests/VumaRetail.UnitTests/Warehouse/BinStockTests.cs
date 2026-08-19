using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>
/// The bin-level quantity projection — quantity only, no cost (valuation stays at the location level).
/// </summary>
public sealed class BinStockTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private static BinStock NewBalance() => BinStock.Open(TenantId, StoreId, BinId, ItemId, null, "EA");

    [Fact]
    public void A_new_bin_balance_opens_at_zero_in_the_unit_it_was_given()
    {
        BinStock balance = NewBalance();

        balance.QuantityOnHand.Value.Should().Be(0m);
        balance.QuantityOnHand.UnitOfMeasure.Should().Be("EA");
    }

    [Fact]
    public void Applying_in_increases_on_hand_quantity()
    {
        BinStock balance = NewBalance();

        balance.ApplyIn(new Quantity(12m, "EA"));

        balance.QuantityOnHand.Value.Should().Be(12m);
    }

    [Fact]
    public void Applying_out_decreases_on_hand_quantity()
    {
        BinStock balance = NewBalance();
        balance.ApplyIn(new Quantity(12m, "EA"));

        balance.ApplyOut(new Quantity(5m, "EA"));

        balance.QuantityOnHand.Value.Should().Be(7m);
    }

    [Fact]
    public void A_bin_cannot_be_relieved_below_zero()
    {
        BinStock balance = NewBalance();
        balance.ApplyIn(new Quantity(3m, "EA"));

        Action applying = () => balance.ApplyOut(new Quantity(4m, "EA"));

        applying.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_INSUFFICIENT_BIN_STOCK");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_positive_movement_is_required_both_ways(decimal quantity)
    {
        BinStock balance = NewBalance();
        balance.ApplyIn(new Quantity(5m, "EA"));

        Action applyingIn = () => balance.ApplyIn(new Quantity(quantity, "EA"));
        Action applyingOut = () => balance.ApplyOut(new Quantity(quantity, "EA"));

        applyingIn.Should().Throw<WarehouseRuleException>().Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
        applyingOut.Should().Throw<WarehouseRuleException>().Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_movement_in_a_different_unit_of_measure_is_refused()
    {
        BinStock balance = NewBalance();

        Action applying = () => balance.ApplyIn(new Quantity(1m, "KG"));

        applying.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_UOM_MISMATCH");
    }

    [Fact]
    public void A_bin_balance_names_exactly_one_of_an_item_or_a_variant()
    {
        Action both = () => BinStock.Open(TenantId, StoreId, BinId, ItemId, UuidV7.NewGuid(), "EA");
        Action neither = () => BinStock.Open(TenantId, StoreId, BinId, null, null, "EA");

        both.Should().Throw<WarehouseRuleException>().Which.Code.Should().Be("WAREHOUSE_EXACTLY_ONE_ITEM_OR_VARIANT");
        neither.Should().Throw<WarehouseRuleException>().Which.Code.Should().Be("WAREHOUSE_EXACTLY_ONE_ITEM_OR_VARIANT");
    }
}

/// <summary>The append-only bin-level movement record.</summary>
public sealed class BinStockMovementTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid ReferenceId = UuidV7.NewGuid();

    [Fact]
    public void A_movement_is_an_immutable_record()
    {
        BinStockMovement movement = BinStockMovement.Post(
            TenantId, StoreId, BinId, ItemId, null, BinStockMovementType.PutawayIn,
            new Quantity(5m, "EA"), BinStockReferenceType.Putaway, ReferenceId);

        movement.Should().BeAssignableTo<VumaRetail.Domain.Entities.IImmutableRecord>();
    }

    [Fact]
    public void A_zero_quantity_movement_is_refused()
    {
        Action posting = () => BinStockMovement.Post(
            TenantId, StoreId, BinId, ItemId, null, BinStockMovementType.PutawayIn,
            new Quantity(0m, "EA"), BinStockReferenceType.Putaway, ReferenceId);

        posting.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_movement_must_correlate_to_a_task_or_transfer()
    {
        Action posting = () => BinStockMovement.Post(
            TenantId, StoreId, BinId, ItemId, null, BinStockMovementType.PutawayIn,
            new Quantity(5m, "EA"), BinStockReferenceType.Putaway, Guid.Empty);

        posting.Should().Throw<ArgumentException>();
    }
}
