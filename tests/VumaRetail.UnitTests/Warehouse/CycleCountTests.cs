using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>
/// The bin-level count session — the same finalize-is-final shape Stage 08's <c>StocktakeSession</c>
/// uses, at bin granularity.
/// </summary>
public sealed class CycleCountTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ZoneId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void A_new_count_opens_in_the_open_state()
    {
        CycleCount count = CycleCount.Open(TenantId, StoreId, LocationId, ZoneId, null);

        count.Status.Should().Be(CycleCountStatus.Open);
        count.IsFinalized.Should().BeFalse();
        count.ZoneId.Should().Be(ZoneId);
    }

    [Fact]
    public void A_count_may_narrow_to_no_zone_meaning_the_whole_location()
    {
        CycleCount count = CycleCount.Open(TenantId, StoreId, LocationId, null, null);

        count.ZoneId.Should().BeNull();
    }

    [Fact]
    public void Finalizing_closes_the_count()
    {
        CycleCount count = CycleCount.Open(TenantId, StoreId, LocationId, ZoneId, null);

        count.Finalize(Now);

        count.IsFinalized.Should().BeTrue();
        count.FinalizedAt.Should().Be(Now);
    }

    [Fact]
    public void A_finalized_count_cannot_be_finalized_again()
    {
        CycleCount count = CycleCount.Open(TenantId, StoreId, LocationId, ZoneId, null);
        count.Finalize(Now);

        Action finalizing = () => count.Finalize(Now);

        finalizing.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_CYCLE_COUNT_ALREADY_FINALIZED");
    }

    [Fact]
    public void EnsureOpen_refuses_once_finalized()
    {
        CycleCount count = CycleCount.Open(TenantId, StoreId, LocationId, ZoneId, null);
        count.Finalize(Now);

        Action ensuring = () => count.EnsureOpen();

        ensuring.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_CYCLE_COUNT_ALREADY_FINALIZED");
    }
}

/// <summary>
/// One counted bin/stock-keeping-unit line — the system quantity is a snapshot, exactly mirroring
/// Stage 08's <c>StocktakeLineTests</c>.
/// </summary>
public sealed class CycleCountLineTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CycleCountId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    [Fact]
    public void The_variance_is_counted_minus_system()
    {
        CycleCountLine line = CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, ItemId, null,
            new Quantity(10m, "EA"), new Quantity(7m, "EA"));

        line.Variance.Value.Should().Be(-3m);
    }

    [Fact]
    public void A_surplus_count_is_a_positive_variance()
    {
        CycleCountLine line = CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, ItemId, null,
            new Quantity(5m, "EA"), new Quantity(8m, "EA"));

        line.Variance.Value.Should().Be(3m);
    }

    [Fact]
    public void An_exact_count_has_a_zero_variance()
    {
        CycleCountLine line = CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, ItemId, null,
            new Quantity(10m, "EA"), new Quantity(10m, "EA"));

        line.Variance.IsZero.Should().BeTrue();
    }

    [Fact]
    public void A_recount_replaces_both_the_system_and_counted_quantities()
    {
        CycleCountLine line = CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, ItemId, null,
            new Quantity(10m, "EA"), new Quantity(7m, "EA"));

        line.Recount(new Quantity(10m, "EA"), new Quantity(9m, "EA"));

        line.CountedQuantity.Value.Should().Be(9m);
        line.Variance.Value.Should().Be(-1m);
    }

    [Fact]
    public void A_counted_quantity_cannot_be_negative()
    {
        Action recording = () => CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, ItemId, null,
            new Quantity(10m, "EA"), new Quantity(-1m, "EA"));

        recording.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_line_names_exactly_one_of_an_item_or_a_variant()
    {
        Action neither = () => CycleCountLine.Record(
            TenantId, StoreId, CycleCountId, BinId, null, null,
            new Quantity(10m, "EA"), new Quantity(10m, "EA"));

        neither.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_EXACTLY_ONE_ITEM_OR_VARIANT");
    }
}
