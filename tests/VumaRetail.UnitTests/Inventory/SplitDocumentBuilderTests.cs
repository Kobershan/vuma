using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The split document builder: one draft per supplying company, reconciled line for line and cent
/// for cent — and a named refusal the moment anything does not add up.
/// </summary>
public sealed class SplitDocumentBuilderTests
{
    private static readonly Guid CompanyA = UuidV7.NewGuid();
    private static readonly Guid CompanyB = UuidV7.NewGuid();
    private static readonly Guid LocationA = UuidV7.NewGuid();
    private static readonly Guid LocationB = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid Line1 = UuidV7.NewGuid();
    private static readonly Guid Line2 = UuidV7.NewGuid();

    private readonly ISplitDocumentBuilder _builder = new SplitDocumentBuilder();

    private static SourcingDemandLine Demand(Guid lineId, decimal quantity) => new(
        lineId, ItemId, null,
        new Quantity(quantity, "EA"),
        new Money(100m, "ZAR"),
        new Money(quantity * 100m, "ZAR"),
        new Money(quantity * 15m, "ZAR"),
        new Money(quantity * 115m, "ZAR"));

    private static SourcingSourceOrder Source(params SourcingDemandLine[] demands) => new(
        Guid.NewGuid(), "SO-2026-000412", Guid.NewGuid(),
        SalesChannel.InStore, OrderFulfilmentType.ClickAndCollect,
        DeliveryAddress: null, "ZAR", demands);

    [Fact]
    public void A_two_company_plan_builds_two_drafts_whose_lines_sum_to_the_order()
    {
        SourcingSourceOrder source = Source(Demand(Line1, 20m));
        var committed = new SourcingPlan(CompanyA, [
            new SourcingPlanLine(Line1, [
                new SourcingAllocation(CompanyA, LocationA, new Quantity(12m, "EA")),
                new SourcingAllocation(CompanyB, LocationB, new Quantity(8m, "EA")),
            ], new Quantity(0m, "EA")),
        ]);

        IReadOnlyList<SplitOrderDraft> drafts = _builder.Build(source, committed);

        drafts.Should().HaveCount(2);
        drafts.SelectMany(draft => draft.Lines).Sum(line => line.Quantity.Value).Should().Be(20m);
        drafts.SelectMany(draft => draft.Lines).Sum(line => line.DiscountAmount.Amount).Should().Be(0m);
        drafts.SelectMany(draft => draft.Lines).Sum(line => line.TaxAmount.Amount).Should().Be(300m);
        drafts.Select(draft => draft.CompanyId).Should().BeEquivalentTo<Guid>([CompanyA, CompanyB]);
    }

    [Fact]
    public void Backordered_remainder_takes_no_money_and_drafts_still_sum()
    {
        SourcingSourceOrder source = Source(Demand(Line1, 20m));
        var committed = new SourcingPlan(CompanyA, [
            new SourcingPlanLine(Line1, [
                new SourcingAllocation(CompanyA, LocationA, new Quantity(12m, "EA")),
                new SourcingAllocation(CompanyB, LocationB, new Quantity(8m, "EA")),
            ], new Quantity(0m, "EA")),
        ]);

        IReadOnlyList<SplitOrderDraft> drafts = _builder.Build(source, committed);

        drafts.SelectMany(draft => draft.Lines).Sum(line => line.Quantity.Value).Should().Be(20m);
        drafts.SelectMany(draft => draft.Lines).Sum(line => line.TaxAmount.Amount).Should().Be(300m);
    }

    [Fact]
    public void Every_line_lands_on_exactly_one_draft()
    {
        SourcingSourceOrder source = Source(Demand(Line1, 10m), Demand(Line2, 6m));
        var committed = new SourcingPlan(CompanyA, [
            new SourcingPlanLine(Line1, [
                new SourcingAllocation(CompanyA, LocationA, new Quantity(10m, "EA")),
            ], new Quantity(0m, "EA")),
            new SourcingPlanLine(Line2, [
                new SourcingAllocation(CompanyB, LocationB, new Quantity(6m, "EA")),
            ], new Quantity(0m, "EA")),
        ]);

        IReadOnlyList<SplitOrderDraft> drafts = _builder.Build(source, committed);

        drafts.Should().HaveCount(2);
        drafts.SelectMany(draft => draft.Lines).Select(line => line.SourceLineId)
            .Should().BeEquivalentTo<Guid>([Line1, Line2]);
    }

    [Fact]
    public void A_plan_covering_an_unknown_line_is_refused_naming_the_line()
    {
        SourcingSourceOrder source = Source(Demand(Line1, 10m));
        var committed = new SourcingPlan(CompanyA, [
            new SourcingPlanLine(Guid.NewGuid(), [
                new SourcingAllocation(CompanyA, LocationA, new Quantity(10m, "EA")),
            ], new Quantity(0m, "EA")),
        ]);

        Action build = () => _builder.Build(source, committed);

        build.Should().Throw<InventoryRuleException>().WithMessage("*does not have*");
    }

    [Fact]
    public void A_foreign_currency_line_is_refused()
    {
        var demand = new SourcingDemandLine(
            Line1, ItemId, null, new Quantity(2m, "EA"),
            new Money(100m, "USD"), new Money(200m, "USD"), new Money(30m, "USD"), new Money(230m, "USD"));
        SourcingSourceOrder source = Source(demand);

        Action build = () => _builder.Build(
            source,
            new SourcingPlan(CompanyA, [
                new SourcingPlanLine(Line1, [
                    new SourcingAllocation(CompanyA, LocationA, new Quantity(2m, "EA")),
                ], new Quantity(0m, "EA")),
            ]));

        build.Should().Throw<InventoryRuleException>().WithMessage("*One order, one currency*");
    }
}
