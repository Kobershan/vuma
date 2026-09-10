using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Manufacturing;

public sealed class BillOfMaterialsTests
{
    [Fact]
    public void A_draft_BOM_can_publish_only_after_receiving_a_component()
    {
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AddLine(Guid.NewGuid(), new Quantity(2m, "EA"));

        bom.Publish();

        bom.Status.Should().Be(BillOfMaterialsStatus.Published);
    }

    [Fact]
    public void A_published_BOM_cannot_be_changed()
    {
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AddLine(Guid.NewGuid(), new Quantity(1m, "EA"));
        bom.Publish();

        Action action = () => bom.AddLine(Guid.NewGuid(), new Quantity(1m, "EA"));

        action.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("BOM_INVALID_TRANSITION");
    }

    [Fact]
    public void A_BOM_rejects_self_reference_and_non_positive_components()
    {
        Guid finishedItemId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), finishedItemId, 1, "Assembly");

        Action selfReference = () => bom.AddLine(finishedItemId, new Quantity(1m, "EA"));
        Action zero = () => bom.AddLine(Guid.NewGuid(), new Quantity(0m, "EA"));

        selfReference.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("BOM_SELF_REFERENCE");
        zero.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("BOM_POSITIVE_QUANTITY");
    }

    [Fact]
    public void Alternate_components_are_preserved_as_a_group()
    {
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AddLine(Guid.NewGuid(), new Quantity(1m, "EA"), alternateGroup: "FASTENER");
        bom.AddLine(Guid.NewGuid(), new Quantity(1m, "EA"), alternateGroup: "FASTENER");

        bom.Lines.Select(line => line.AlternateGroup).Should().AllBe("FASTENER");
    }
}
