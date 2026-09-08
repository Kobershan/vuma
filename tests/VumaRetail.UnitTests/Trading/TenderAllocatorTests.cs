using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// The till's one captured tender split across company segments (ADR-126): proportional by
/// segment gross, cent-exact, remainder dust deterministically to the largest segment.
/// </summary>
public sealed class TenderAllocatorTests
{
    private static readonly Guid CompanyA = UuidV7.NewGuid();
    private static readonly Guid CompanyB = UuidV7.NewGuid();
    private static readonly Guid CompanyC = UuidV7.NewGuid();

    [Fact]
    public void Proportional_split_is_cent_exact_and_sums_to_the_tender()
    {
        // The operator's shape: R1 899.00 + R214.00 = R2 113.00, tendered exactly.
        var result = TenderAllocator.AllocateDefault(
            new Money(2113.00m, "ZAR"),
            [(CompanyA, new Money(1899.00m, "ZAR")), (CompanyB, new Money(214.00m, "ZAR"))]);

        result.Allocations.Should().HaveCount(2);
        result.Allocations.Single(a => a.CompanyId == CompanyA).Amount.Amount.Should().Be(1899.00m);
        result.Allocations.Single(a => a.CompanyId == CompanyB).Amount.Amount.Should().Be(214.00m);
        result.Allocations.Sum(a => a.Amount.Amount).Should().Be(2113.00m);
    }

    [Fact]
    public void Remainder_cent_lands_on_the_larger_segment_every_run()
    {
        // R100.00 tendered over R60.00/R30.00 (total R90.00): exact shares are
        // R66.666…/R33.333… — one cent of dust, and it belongs to the larger segment.
        TenderAllocationResult first = Allocate();
        TenderAllocationResult second = Allocate();

        first.Allocations.Single(a => a.CompanyId == CompanyA).Amount.Amount.Should().Be(66.67m);
        first.Allocations.Single(a => a.CompanyId == CompanyB).Amount.Amount.Should().Be(33.33m);
        first.Should().BeEquivalentTo(second, "the same basket allocates the same way on every run");
        first.Basis.Should().Contain(CompanyA.ToString(), "the rule is stated with the dust's destination");

        static TenderAllocationResult Allocate() => TenderAllocator.AllocateDefault(
            new Money(100.00m, "ZAR"),
            [(CompanyA, new Money(60.00m, "ZAR")), (CompanyB, new Money(30.00m, "ZAR"))]);
    }

    [Fact]
    public void Dust_favours_size_not_remainder_order()
    {
        // Under-tender-shaped split (tender below total): exact R71.111…/R8.888… — a pure
        // largest-remainder rule would hand the cent to the smaller segment's .888 fraction.
        // The stage rule names the largest segment instead, and the test pins that choice.
        var result = TenderAllocator.AllocateDefault(
            new Money(80.00m, "ZAR"),
            [(CompanyA, new Money(80.00m, "ZAR")), (CompanyB, new Money(10.00m, "ZAR"))]);

        result.Allocations.Single(a => a.CompanyId == CompanyA).Amount.Amount.Should().Be(71.12m);
        result.Allocations.Single(a => a.CompanyId == CompanyB).Amount.Amount.Should().Be(8.88m);
        result.Allocations.Sum(a => a.Amount.Amount).Should().Be(80.00m);
    }

    [Fact]
    public void Ties_break_by_lowest_company_id()
    {
        // Equal segments: dust must still land somewhere deterministic. Lowest company id
        // first is byte-order stable across machines, unlike dictionary or hash order.
        Guid[] ordered = new[] { CompanyA, CompanyB, CompanyC }.OrderBy(id => id).ToArray();

        var result = TenderAllocator.AllocateDefault(
            new Money(100.00m, "ZAR"),
            [(CompanyA, new Money(30.00m, "ZAR")), (CompanyB, new Money(30.00m, "ZAR")), (CompanyC, new Money(30.00m, "ZAR"))]);

        // R33.333… × 3 = R99.999…: one cent of dust to the lowest company id.
        result.Allocations.Single(a => a.CompanyId == ordered[0]).Amount.Amount.Should().Be(33.34m);
        result.Allocations.Sum(a => a.Amount.Amount).Should().Be(100.00m);
    }

    [Fact]
    public void Override_accepts_any_exact_split()
    {
        var result = TenderAllocator.AllocateOverride(
            new Money(2113.00m, "ZAR"),
            [(CompanyA, new Money(1899.00m, "ZAR")), (CompanyB, new Money(214.00m, "ZAR"))],
            [(CompanyA, new Money(2000.00m, "ZAR")), (CompanyB, new Money(113.00m, "ZAR"))]);

        result.Basis.Should().Be("cashier override, exact");
        result.Allocations.Sum(a => a.Amount.Amount).Should().Be(2113.00m);
    }

    [Fact]
    public void Override_a_cent_short_is_refused()
    {
        Action act = () => TenderAllocator.AllocateOverride(
            new Money(2113.00m, "ZAR"),
            [(CompanyA, new Money(1899.00m, "ZAR")), (CompanyB, new Money(214.00m, "ZAR"))],
            [(CompanyA, new Money(2000.00m, "ZAR")), (CompanyB, new Money(112.99m, "ZAR"))]);

        act.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ALLOCATION_MISMATCH");
    }

    [Fact]
    public void Override_naming_a_stranger_company_is_refused()
    {
        Action act = () => TenderAllocator.AllocateOverride(
            new Money(2113.00m, "ZAR"),
            [(CompanyA, new Money(1899.00m, "ZAR")), (CompanyB, new Money(214.00m, "ZAR"))],
            [(CompanyA, new Money(2113.00m, "ZAR")), (CompanyC, new Money(0.00m, "ZAR"))]);

        act.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ALLOCATION_COMPANIES");
    }
}
