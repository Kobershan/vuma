using VumaRetail.Application.Manufacturing;
using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Manufacturing;

public sealed class BomExplosionEngineTests
{
    [Fact]
    public void A_nested_BOM_rolls_up_leaf_costs_and_scrap()
    {
        Guid tenant = Guid.NewGuid();
        Guid finished = Guid.NewGuid();
        Guid subassembly = Guid.NewGuid();
        Guid leaf = Guid.NewGuid();
        BillOfMaterials nested = BillOfMaterials.Create(tenant, subassembly, 1, "Subassembly");
        nested.AddLine(leaf, new Quantity(2m, "EA"));
        nested.Publish();
        BillOfMaterials root = BillOfMaterials.Create(tenant, finished, 1, "Finished");
        root.AddLine(subassembly, new Quantity(3m, "EA"), scrapPercent: 10m);
        root.Publish();

        BomExplosionResult result = new BomExplosionEngine().Explode(
            root,
            new Quantity(2m, "EA"),
            new Dictionary<BomComponentKey, BillOfMaterials>
            {
                [BomComponentKey.Create(subassembly)] = nested,
            },
            new Dictionary<BomComponentKey, Money>
            {
                [BomComponentKey.Create(leaf)] = new Money(1.25m, "ZAR"),
            },
            "ZAR");

        result.Components.Should().ContainSingle();
        result.Components[0].Quantity.Value.Should().Be(13.333333m);
        result.TotalCost.Amount.Should().Be(16.6667m);
    }

    [Fact]
    public void An_explicit_alternate_overrides_definition_order()
    {
        Guid tenant = Guid.NewGuid();
        Guid finished = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        BillOfMaterials root = BillOfMaterials.Create(tenant, finished, 1, "Finished");
        root.AddLine(first, new Quantity(1m, "EA"), alternateGroup: "BODY");
        root.AddLine(second, new Quantity(1m, "EA"), alternateGroup: "BODY");
        root.Publish();

        BomExplosionResult result = new BomExplosionEngine().Explode(
            root,
            new Quantity(1m, "EA"),
            new Dictionary<BomComponentKey, BillOfMaterials>(),
            new Dictionary<BomComponentKey, Money>
            {
                [BomComponentKey.Create(first)] = new Money(4m, "ZAR"),
                [BomComponentKey.Create(second)] = new Money(6m, "ZAR"),
            },
            "ZAR",
            new HashSet<BomComponentKey> { BomComponentKey.Create(second) });

        result.Components.Should().ContainSingle().Which.Component.Should().Be(BomComponentKey.Create(second));
    }

    [Fact]
    public void A_cycle_is_rejected()
    {
        Guid tenant = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        BillOfMaterials a = BillOfMaterials.Create(tenant, first, 1, "A");
        a.AddLine(second, new Quantity(1m, "EA"));
        a.Publish();
        BillOfMaterials b = BillOfMaterials.Create(tenant, second, 1, "B");
        b.AddLine(first, new Quantity(1m, "EA"));
        b.Publish();

        Action action = () => new BomExplosionEngine().Explode(
            a,
            new Quantity(1m, "EA"),
            new Dictionary<BomComponentKey, BillOfMaterials>
            {
                [BomComponentKey.Create(first)] = a,
                [BomComponentKey.Create(second)] = b,
            },
            new Dictionary<BomComponentKey, Money>(),
            "ZAR");

        action.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("BOM_CYCLE");
    }

    [Fact]
    public void Mixed_cost_currencies_are_rejected()
    {
        Guid tenant = Guid.NewGuid();
        Guid finished = Guid.NewGuid();
        Guid component = Guid.NewGuid();
        BillOfMaterials root = BillOfMaterials.Create(tenant, finished, 1, "Finished");
        root.AddLine(component, new Quantity(1m, "EA"));
        root.Publish();

        Action action = () => new BomExplosionEngine().Explode(
            root,
            new Quantity(1m, "EA"),
            new Dictionary<BomComponentKey, BillOfMaterials>(),
            new Dictionary<BomComponentKey, Money>
            {
                [BomComponentKey.Create(component)] = new Money(1m, "USD"),
            },
            "ZAR");

        action.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("BOM_MIXED_CURRENCY");
    }
}
