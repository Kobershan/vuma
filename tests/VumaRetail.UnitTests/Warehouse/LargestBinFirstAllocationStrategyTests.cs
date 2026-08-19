using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>
/// ADR-090: allocates from the bin holding the most of a stock-keeping unit first, spilling into the
/// next largest only if the first cannot fully satisfy the demand.
/// </summary>
public sealed class LargestBinFirstAllocationStrategyTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private readonly LargestBinFirstAllocationStrategy _strategy = new();

    private static BinStock BinWith(decimal quantity)
    {
        BinStock balance = BinStock.Open(TenantId, StoreId, UuidV7.NewGuid(), ItemId, null, "EA");
        balance.ApplyIn(new Quantity(quantity, "EA"));
        return balance;
    }

    [Fact]
    public void An_exact_fit_in_one_bin_allocates_entirely_from_it()
    {
        BinStock bin = BinWith(10m);

        var allocations = _strategy.Allocate(new Quantity(10m, "EA"), [bin]);

        allocations.Should().ContainSingle();
        allocations[0].BinId.Should().Be(bin.BinId);
        allocations[0].Quantity.Value.Should().Be(10m);
    }

    [Fact]
    public void The_largest_candidate_is_tried_first()
    {
        BinStock small = BinWith(3m);
        BinStock large = BinWith(20m);

        var allocations = _strategy.Allocate(new Quantity(5m, "EA"), [small, large]);

        allocations.Should().ContainSingle();
        allocations[0].BinId.Should().Be(large.BinId);
    }

    [Fact]
    public void Demand_beyond_the_largest_bin_spills_into_the_next_largest()
    {
        BinStock first = BinWith(6m);
        BinStock second = BinWith(10m);

        var allocations = _strategy.Allocate(new Quantity(12m, "EA"), [first, second]);

        allocations.Should().HaveCount(2);
        allocations[0].BinId.Should().Be(second.BinId, "the larger bin is tried first");
        allocations[0].Quantity.Value.Should().Be(10m);
        allocations[1].BinId.Should().Be(first.BinId);
        allocations[1].Quantity.Value.Should().Be(2m);
    }

    [Fact]
    public void A_bin_with_nothing_on_hand_is_skipped()
    {
        BinStock empty = BinStock.Open(TenantId, StoreId, UuidV7.NewGuid(), ItemId, null, "EA");
        BinStock stocked = BinWith(5m);

        var allocations = _strategy.Allocate(new Quantity(5m, "EA"), [empty, stocked]);

        allocations.Should().ContainSingle();
        allocations[0].BinId.Should().Be(stocked.BinId);
    }

    [Fact]
    public void Demand_beyond_every_candidates_total_returns_a_partial_allocation()
    {
        // Refusing the shortfall is ReleasePickWaveCommandHandler's job (WAREHOUSE_INSUFFICIENT_STOCK_TO_ALLOCATE);
        // the strategy itself just reports what it could find.
        BinStock only = BinWith(4m);

        var allocations = _strategy.Allocate(new Quantity(10m, "EA"), [only]);

        allocations.Should().ContainSingle();
        allocations[0].Quantity.Value.Should().Be(4m);
    }

    [Fact]
    public void No_candidates_allocates_nothing()
    {
        var allocations = _strategy.Allocate(new Quantity(10m, "EA"), []);

        allocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_request_is_refused(decimal requested)
    {
        Action allocating = () => _strategy.Allocate(new Quantity(requested, "EA"), []);

        allocating.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE");
    }
}
