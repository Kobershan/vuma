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
            Guid.NewGuid(), tenantId, companyId, finishedItemId, new Quantity(10m, "EA"), "PROD-001", bom.Id);

        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);

        order.Status.Should().Be(ProductionOrderStatus.Released);
        order.Snapshot!.BillOfMaterialsVersion.Should().Be(1);
        order.Materials.Should().ContainSingle().Which.RequiredQuantity.Should().Be(new Quantity(20m, "EA"));
    }

    [Fact]
    public void Release_rejects_an_unpublished_or_wrong_finished_item_bom()
    {
        Guid bomId = Guid.NewGuid();
        ProductionOrder order = ProductionOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Quantity(1m, "EA"), "PROD-001", bomId);
        BillOfMaterials draft = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Draft");

        Action action = () => order.Release(Guid.NewGuid(), draft, DateTimeOffset.UtcNow);

        action.Should().Throw<ManufacturingRuleException>();
    }

    [Fact]
    public void Release_replays_the_same_operation_without_mutating_the_snapshot()
    {
        Guid tenantId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(tenantId, itemId, 1, "Widget");
        bom.AddLine(Guid.NewGuid(), new Quantity(1m, "EA"));
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), tenantId, Guid.NewGuid(), itemId, new Quantity(1m, "EA"), "PROD-REPLAY", bom.Id);
        Guid operationId = Guid.NewGuid();
        DateTimeOffset releasedAt = DateTimeOffset.UtcNow;

        order.Release(operationId, bom, releasedAt).Should().BeTrue();
        order.Release(operationId, bom, releasedAt.AddMinutes(1)).Should().BeFalse();
        order.Snapshot!.ReleasedAt.Should().Be(releasedAt);
    }

    [Fact]
    public void Lifecycle_refuses_skipping_states()
    {
        ProductionOrder order = ProductionOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Quantity(1m, "EA"), "PROD-001", Guid.NewGuid());

        Action action = order.Complete;

        action.Should().Throw<ManufacturingRuleException>();
    }

    [Fact]
    public void Material_issue_output_and_scrap_are_idempotent_and_reconcile()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(tenantId, itemId, 1, "Widget");
        Guid componentId = Guid.NewGuid();
        bom.AddLine(componentId, new Quantity(2m, "EA"));
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), tenantId, companyId, itemId, new Quantity(10m, "EA"), "PROD-002", bom.Id);
        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);
        Money cost = new(10m, "ZAR");
        Guid issueId = Guid.NewGuid();
        order.IssueMaterial(issueId, componentId, null, new Quantity(20m, "EA"), cost);
        order.IssueMaterial(issueId, componentId, null, new Quantity(20m, "EA"), cost);
        order.ReceiveOutput(Guid.NewGuid(), new Quantity(9m, "EA"), cost);
        order.RecordScrap(Guid.NewGuid(), new Quantity(1m, "EA"), cost);
        order.Complete();

        order.Status.Should().Be(ProductionOrderStatus.Completed);
        order.Issues.Should().ContainSingle();
    }
}
