using VumaRetail.Domain.Inventory;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The default sourcing strategy against the stage's own numbers: ordering company first, then
/// most available, backorder the rest — deterministic and total.
/// </summary>
public sealed class SourcingStrategyTests
{
    private static readonly Guid CompanyA = UuidV7.NewGuid();
    private static readonly Guid CompanyB = UuidV7.NewGuid();
    private static readonly Guid LocationA = UuidV7.NewGuid();
    private static readonly Guid LocationB = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid LineId = UuidV7.NewGuid();

    private readonly ICompanySourcingStrategy _strategy = new AvailabilityThenProximityStrategy();

    private static SourcingDemandLine Demand(decimal quantity) => new(
        LineId, ItemId, null,
        new Quantity(quantity, "EA"),
        new Money(100m, "ZAR"),
        new Money(quantity * 100m, "ZAR"),
        new Money(quantity * 15m, "ZAR"),
        new Money(quantity * 115m, "ZAR"));

    private static SourcingCandidate Candidate(Guid company, string code, Guid location, decimal available, bool stale = false) => new(
        company, code, location, ItemId, null,
        new Quantity(available, "EA"),
        DateTimeOffset.UtcNow,
        stale);

    [Fact]
    public void Twenty_demanded_with_twelve_and_thirty_allocates_twelve_and_eight()
    {
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(20m)],
            [
                Candidate(CompanyA, "AA", LocationA, 12m),
                Candidate(CompanyB, "BB", LocationB, 30m),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.Should().HaveCount(2);
        line.Allocations.First(allocation => allocation.CompanyId == CompanyA).Quantity.Value.Should().Be(12m);
        line.Allocations.First(allocation => allocation.CompanyId == CompanyB).Quantity.Value.Should().Be(8m);
        line.Backorder.Value.Should().Be(0m);
        plan.IsFullyCovered.Should().BeTrue();
    }

    [Fact]
    public void Twenty_demanded_with_twelve_and_three_allocates_fifteen_and_backorders_five()
    {
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(20m)],
            [
                Candidate(CompanyA, "AA", LocationA, 12m),
                Candidate(CompanyB, "BB", LocationB, 3m),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.Sum(allocation => allocation.Quantity.Value).Should().Be(15m);
        line.Backorder.Value.Should().Be(5m);
        plan.IsFullyCovered.Should().BeFalse();
    }

    [Fact]
    public void The_ordering_company_comes_first_even_when_it_holds_less()
    {
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(10m)],
            [
                Candidate(CompanyB, "BB", LocationB, 100m),
                Candidate(CompanyA, "AA", LocationA, 4m),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.First().CompanyId.Should().Be(CompanyA);
        line.Allocations.First().Quantity.Value.Should().Be(4m);
        line.Allocations.Last().Quantity.Value.Should().Be(6m);
    }

    [Fact]
    public void Nothing_is_allocated_against_a_company_with_none()
    {
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(5m)],
            [
                Candidate(CompanyA, "AA", LocationA, 0m),
                Candidate(CompanyB, "BB", LocationB, 5m),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.Should().ContainSingle().Which.CompanyId.Should().Be(CompanyB);
        line.Backorder.Value.Should().Be(0m);
    }

    [Fact]
    public void No_stock_anywhere_is_a_full_backorder_not_an_allocation()
    {
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(5m)],
            [
                Candidate(CompanyA, "AA", LocationA, 0m),
                Candidate(CompanyB, "BB", LocationB, 0m),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.Should().BeEmpty();
        line.Backorder.Value.Should().Be(5m);
    }

    [Fact]
    public void Stale_figures_plan_but_never_allocate_what_is_not_there()
    {
        // The planner is told B has 30 (stale). Planning from it is correct — the commit
        // re-checks — and the plan itself still balances: 12 + 8 planned, nothing conjured.
        SourcingPlan plan = _strategy.Plan(new SourcingPlanRequest(
            [Demand(20m)],
            [
                Candidate(CompanyA, "AA", LocationA, 12m),
                Candidate(CompanyB, "BB", LocationB, 30m, stale: true),
            ],
            CompanyA,
            []));

        SourcingPlanLine line = plan.Lines.Should().ContainSingle().Subject;
        line.Allocations.Sum(allocation => allocation.Quantity.Value).Should().Be(20m);
        line.Backorder.Value.Should().Be(0m);
    }
}

/// <summary>
/// Cumulative money telescoping: slices sum exactly to the source, whatever the rounding.
/// </summary>
public sealed class MoneyTelescopingTests
{
    [Fact]
    public void Three_equal_slices_of_a_hundred_sum_exactly()
    {
        IReadOnlyList<Money> slices = MoneyTelescoping.Split(
            new Money(100m, "ZAR"), 3m, [1m, 1m, 1m]);

        slices.Should().HaveCount(3);
        slices.Sum(slice => slice.Amount).Should().Be(100m);
        slices.Select(slice => slice.Currency).Should().AllBe("ZAR");
    }

    [Fact]
    public void An_uneven_split_telescopes_without_drift()
    {
        // 7 units at R485.10 total across 2 + 5: each slice is independently verifiable and the
        // sum is exact — rounding each slice independently would not have that property.
        IReadOnlyList<Money> slices = MoneyTelescoping.Split(
            new Money(485.10m, "ZAR"), 7m, [2m, 5m]);

        slices.Should().HaveCount(2);
        slices.Sum(slice => slice.Amount).Should().Be(485.10m);
        slices[0].Amount.Should().Be(Math.Round(485.10m * 2m / 7m, 4, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Five_hundred_random_splits_all_sum_exactly()
    {
        var random = new Random(0x08C2);

        for (int trial = 0; trial < 500; trial++)
        {
            decimal total = random.Next(1, 50);
            int parts = random.Next(1, 5);
            List<decimal> slices = [];
            decimal assigned = 0m;
            for (int part = 0; part < parts - 1; part++)
            {
                decimal take = Math.Min(total - assigned, random.Next(0, (int)total + 1));
                slices.Add(take);
                assigned += take;
            }

            slices.Add(total - assigned);
            decimal whole = random.Next(100, 100000) / 100m;

            IReadOnlyList<Money> amounts = MoneyTelescoping.Split(new Money(whole, "ZAR"), total, slices);

            amounts.Sum(amount => amount.Amount).Should().Be(whole);
        }
    }

    [Fact]
    public void Slices_that_do_not_sum_to_the_total_are_refused()
    {
        Action split = () => MoneyTelescoping.Split(new Money(10m, "ZAR"), 5m, [2m, 2m]);

        split.Should().Throw<ArgumentException>();
    }
}
