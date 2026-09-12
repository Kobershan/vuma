using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Manufacturing;

public sealed class ProductionOrderTests
{
    [Fact]
    public void Release_snapshots_bom_version_and_material_quantity()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid finishedItemId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(tenantId, finishedItemId, 1, "Widget");
        bom.AddLine(Guid.NewGuid(), new Quantity(2m, "EA"));
        bom.AddRoutingStep(1, "Assemble");
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(
            Guid.NewGuid(), tenantId, companyId, finishedItemId, new Quantity(10m, "EA"), "PROD-001");

        order.Release(bom, DateTimeOffset.UtcNow);

        order.Status.Should().Be(ProductionOrderStatus.Released);
        order.Snapshot!.BillOfMaterialsVersion.Should().Be(1);
        order.Materials.Should().ContainSingle().Which.RequiredQuantity.Should().Be(new Quantity(20m, "EA"));
    }

    [Fact]
    public void Release_rejects_an_unpublished_or_wrong_finished_item_bom()
    {
        ProductionOrder order = ProductionOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Quantity(1m, "EA"), "PROD-001");
        BillOfMaterials draft = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Draft");

        Action action = () => order.Release(draft, DateTimeOffset.UtcNow);

        action.Should().Throw<ManufacturingRuleException>();
    }

    [Fact]
    public void Lifecycle_refuses_skipping_states()
    {
        ProductionOrder order = ProductionOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Quantity(1m, "EA"), "PROD-001");

        Action action = order.Complete;

        action.Should().Throw<ManufacturingRuleException>();
    }
}
