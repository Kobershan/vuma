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
        order.ReceiveOutput(Guid.NewGuid(), new Quantity(9m, "EA"), new Money(20m, "ZAR"));
        order.RecordScrap(Guid.NewGuid(), new Quantity(1m, "EA"), new Money(20m, "ZAR"));
        order.Complete();

        order.Status.Should().Be(ProductionOrderStatus.Completed);
        order.Issues.Should().ContainSingle();
    }

    [Fact]
    public void Closing_an_already_closed_order_is_a_safe_replay()
    {
        Guid itemId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), itemId, 1, "Widget");
        Guid componentId = Guid.NewGuid();
        bom.AddLine(componentId, new Quantity(1m, "EA"));
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), itemId, new(1m, "EA"), "PO-CLOSE-REPLAY", bom.Id);
        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);
        Money cost = new(10m, "ZAR");
        order.IssueMaterial(Guid.NewGuid(), componentId, null, new(1m, "EA"), cost);
        order.ReceiveOutput(Guid.NewGuid(), new(1m, "EA"), cost);
        order.Complete();
        order.Close();

        order.Close();

        order.Status.Should().Be(ProductionOrderStatus.Closed);
    }

    [Fact]
    public void Release_snapshots_only_the_default_line_from_each_alternate_group()
    {
        Guid itemId = Guid.NewGuid();
        Guid firstComponent = Guid.NewGuid();
        Guid secondComponent = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), itemId, 1, "Alternate widget");
        bom.AddLine(firstComponent, new Quantity(1m, "EA"), alternateGroup: "BODY");
        bom.AddLine(secondComponent, new Quantity(1m, "EA"), alternateGroup: "BODY");
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), itemId, new(1m, "EA"), "PO-ALT", bom.Id);

        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);

        order.Materials.Should().ContainSingle().Which.ComponentItemId.Should().Be(firstComponent);
    }

    [Fact]
    public void Rolled_back_external_effect_can_be_retried_without_false_idempotency()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(tenantId, itemId, 1, "Widget");
        bom.AddLine(componentId, new Quantity(2m, "EA"));
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), tenantId, companyId, itemId, new Quantity(10m, "EA"), "PROD-003", bom.Id);
        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);
        Guid issueId = Guid.NewGuid();
        Money cost = new(10m, "ZAR");

        order.IssueMaterial(issueId, componentId, null, new Quantity(2m, "EA"), cost);
        order.RollbackMaterialIssue(issueId);
        order.ReceiveOutput(issueId, new Quantity(1m, "EA"), cost);
        order.RollbackOutputReceipt(issueId);
        order.RecordScrap(issueId, new Quantity(1m, "EA"), cost);
        order.RollbackScrap(issueId);

        order.Issues.Should().BeEmpty();
        order.Receipts.Should().BeEmpty();
        order.Scrap.Should().BeEmpty();
        order.IssueMaterial(issueId, componentId, null, new Quantity(2m, "EA"), cost).Should().BeTrue();
    }

    [Fact]
    public void Reusing_an_operation_id_with_changed_execution_content_is_rejected()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(tenantId, itemId, 1, "Widget");
        bom.AddLine(componentId, new Quantity(2m, "EA"));
        bom.Publish();
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), tenantId, companyId, itemId, new Quantity(10m, "EA"), "PO-CONFLICT", bom.Id);
        order.Release(Guid.NewGuid(), bom, DateTimeOffset.UtcNow);
        Money cost = new(10m, "ZAR");

        Guid issueOperationId = Guid.NewGuid();
        order.IssueMaterial(issueOperationId, componentId, null, new Quantity(2m, "EA"), cost);
        Action changedIssue = () => order.IssueMaterial(issueOperationId, componentId, null, new Quantity(1m, "EA"), cost);

        Guid receiptOperationId = Guid.NewGuid();
        order.ReceiveOutput(receiptOperationId, new Quantity(1m, "EA"), cost);
        Action changedReceipt = () => order.ReceiveOutput(receiptOperationId, new Quantity(2m, "EA"), cost);

        Guid scrapOperationId = Guid.NewGuid();
        order.RecordScrap(scrapOperationId, new Quantity(1m, "EA"), cost);
        Action changedScrap = () => order.RecordScrap(scrapOperationId, new Quantity(2m, "EA"), cost);

        changedIssue.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("PRODUCTION_OPERATION_CONFLICT");
        changedReceipt.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("PRODUCTION_OPERATION_CONFLICT");
        changedScrap.Should().Throw<ManufacturingRuleException>().Which.Code.Should().Be("PRODUCTION_OPERATION_CONFLICT");
    }
}
