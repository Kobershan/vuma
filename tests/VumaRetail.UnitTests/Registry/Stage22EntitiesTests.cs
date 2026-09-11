using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Registry;

public sealed class Stage22EntitiesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    [Fact]
    public void Hierarchy_rejects_cycles_and_duplicate_parent_membership()
    {
        var root = GroupHierarchyNode.Create(Tenant, Business, CompanyA, HierarchyNodeType.HeadOffice, OwnershipType.Owned);
        var child = GroupHierarchyNode.Create(Tenant, Business, CompanyB, HierarchyNodeType.Store, OwnershipType.Owned, "SPAR-1");
        var nodes = new[] { root, child };
        child.SetParent(root.Id, nodes);
        FluentActions.Invoking(() => root.SetParent(child.Id, nodes)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Franchised_node_is_never_control_or_visibility_eligible()
    {
        var node = GroupHierarchyNode.Create(Tenant, Business, CompanyA, HierarchyNodeType.Store, OwnershipType.Franchised, "FR-1");
        node.IsControlEligible.Should().BeFalse();
        node.IsVisibilityEligible.Should().BeFalse();
    }

    [Fact]
    public void Transfer_above_threshold_requires_regional_approval_before_acceptance()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1000m);
        transfer.Check(settings, receiverIsDirectChildOfHolding: false);
        transfer.Status.Should().Be(TransferStatus.RegionalApprovalPending);
        FluentActions.Invoking(transfer.Accept).Should().Throw<InvalidOperationException>();
        transfer.ApproveRegional();
        transfer.Accept();
        transfer.Reserve();
        transfer.Pick();
        transfer.Ship();
        transfer.MoveInTransit();
        transfer.Receive(9);
        transfer.Reconcile(10, "Damaged unit");
        transfer.Status.Should().Be(TransferStatus.Reconciled);
        transfer.DiscrepancyQuantity.Should().Be(-1);
    }

    [Fact]
    public void Transfer_below_threshold_can_go_directly_to_acceptance()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.GroupStandardCost, DiscrepancyOwner.Receiver, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 999.99m);
        transfer.Check(settings, receiverIsDirectChildOfHolding: false);
        transfer.Status.Should().Be(TransferStatus.Checked);
        transfer.Accept();
        transfer.Status.Should().Be(TransferStatus.Accepted);
    }

    [Fact]
    public void Central_buying_is_preaccepted_only_for_a_direct_holding_child()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.GroupStandardCost, DiscrepancyOwner.Receiver, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, CompanyA, 10_000m, centralBuying: true);

        FluentActions.Invoking(() => transfer.Check(settings, receiverIsDirectChildOfHolding: false))
            .Should().Throw<InvalidOperationException>();

        transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, CompanyA, 10_000m, centralBuying: true);
        transfer.Check(settings, receiverIsDirectChildOfHolding: true);
        transfer.Status.Should().Be(TransferStatus.Accepted);
    }

    [Fact]
    public void Shipping_and_in_transit_are_separate_observable_states()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1m);
        transfer.Check(settings, false);
        transfer.Accept();
        transfer.Reserve();
        transfer.Pick();
        transfer.Ship();
        transfer.Status.Should().Be(TransferStatus.Shipped);
        transfer.MoveInTransit();
        transfer.Status.Should().Be(TransferStatus.InTransit);
    }

    [Fact]
    public void Discrepancy_requires_reason()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.LandedCost, DiscrepancyOwner.HeldForReview, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1m);
        transfer.Check(settings, true); transfer.Accept(); transfer.Reserve(); transfer.Pick(); transfer.Ship(); transfer.MoveInTransit(); transfer.Receive(0);
        FluentActions.Invoking(() => transfer.Reconcile(1)).Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void Company_consumes_the_Control_App_issued_identity()
    {
        Guid issuedId = Guid.NewGuid();
        Company company = Company.CreateFromIssuedIdentity(issuedId, Tenant, "issued", "Issued Legal", "Issued Trading", "ZAR", "en-ZA", "ISS");
        company.Id.Should().Be(issuedId);
    }

    [Fact]
    public void Cost_free_stock_projection_exposes_no_cost_bearing_property()
    {
        string[] forbidden = ["cost", "margin", "price", "ledger", "gl", "financial"];
        string[] propertyNames = typeof(OwnedStockOnHandProjection).GetProperties().Select(x => x.Name.ToLowerInvariant()).ToArray();
        propertyNames.Should().OnlyContain(name => forbidden.All(token => !name.Contains(token, StringComparison.Ordinal)));
    }

    [Fact]
    public void Transfer_line_requires_one_sku_identity_and_bounds_received_quantity()
    {
        Guid transferId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        StockTransferLine line = StockTransferLine.Create(Tenant, transferId, itemId, null, 10m, "EA", locationId);

        FluentActions.Invoking(() => StockTransferLine.Create(Tenant, transferId, itemId, itemId, 1m, "EA", locationId))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => line.RecordReceived(11m))
            .Should().Throw<ArgumentOutOfRangeException>();

        line.RecordReceived(8m);
        line.ReceivedQuantity.Should().Be(8m);
    }

    [Fact]
    public void Transfer_line_cost_is_write_once_and_normalized()
    {
        StockTransferLine line = StockTransferLine.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), null, 1m, "EA", Guid.NewGuid());

        line.RecordTransferCost(new Money(12.3456m, "zar"));
        line.UnitCostAtTransferAmount.Should().Be(12.3456m);
        line.UnitCostAtTransferCurrency.Should().Be("ZAR");
        line.RecordTransferCost(new Money(12.3456m, "ZAR"));

        FluentActions.Invoking(() => line.RecordTransferCost(new Money(13m, "ZAR")))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => line.RecordTransferCost(new Money(-1m, "ZAR")))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Lined_transfer_rejects_over_receipt_and_requires_line_total_at_reconciliation()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1m);
        transfer.Lines.Add(StockTransferLine.Create(Tenant, transfer.Id, Guid.NewGuid(), null, 10m, "EA", Guid.NewGuid()));
        transfer.Check(settings, false);
        transfer.Accept();
        transfer.Reserve();
        transfer.Pick();
        transfer.Ship();
        transfer.MoveInTransit();

        FluentActions.Invoking(() => transfer.Receive(11m)).Should().Throw<ArgumentOutOfRangeException>();
        transfer.Receive(8m);
        FluentActions.Invoking(() => transfer.Reconcile(9m, "Mismatch"))
            .Should().Throw<InvalidOperationException>();
        transfer.Reconcile(10m, "Two units damaged");
        transfer.DiscrepancyQuantity.Should().Be(-2m);
    }

    [Fact]
    public void Lined_transfer_receipt_is_monotonic_and_reallocates_cumulative_quantity()
    {
        var settings = GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "SPAR");
        var transfer = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1m);
        var first = StockTransferLine.Create(Tenant, transfer.Id, Guid.NewGuid(), null, 2m, "EA", Guid.NewGuid());
        var second = StockTransferLine.Create(Tenant, transfer.Id, null, Guid.NewGuid(), 3m, "EA", Guid.NewGuid());
        transfer.Lines.Add(first);
        transfer.Lines.Add(second);
        transfer.Check(settings, false);
        transfer.Accept();
        transfer.Reserve();
        transfer.Pick();
        transfer.Ship();
        transfer.MoveInTransit();

        transfer.Receive(1m);
        first.ReceivedQuantity.Should().Be(1m);
        second.ReceivedQuantity.Should().Be(0m);
        transfer.Receive(4m);
        first.ReceivedQuantity.Should().Be(2m);
        second.ReceivedQuantity.Should().Be(2m);
        FluentActions.Invoking(() => transfer.Receive(3m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Transfer_factory_rebinds_lines_to_the_new_transfer()
    {
        var line = StockTransferLine.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), null, 1m, "EA", Guid.NewGuid());
        var transfer = StockTransferRequest.Create(
            Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 1m, false, [line]);
        transfer.Lines.Should().ContainSingle();
        transfer.Lines[0].TransferId.Should().Be(transfer.Id);
    }

    [Fact]
    public void Related_transfers_preserve_scope_and_reverse_locations()
    {
        StockTransferRequest source = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 100m);
        Guid senderLocation = Guid.NewGuid();
        Guid receiverLocation = Guid.NewGuid();
        StockTransferLine sourceLine = StockTransferLine.Create(Tenant, source.Id, Guid.NewGuid(), null, 5m, "EA", senderLocation, receiverLocation);
        source.Lines.Add(sourceLine);
        StockTransferRequest reverse = StockTransferRequest.CreateRelated(source, TransferRelation.Reverse, [sourceLine], 100m);

        reverse.Relation.Should().Be(TransferRelation.Reverse);
        reverse.RelatedTransferId.Should().Be(source.Id);
        reverse.SenderCompanyId.Should().Be(CompanyB);
        reverse.ReceiverCompanyId.Should().Be(CompanyA);
        reverse.Lines.Single().SenderLocationId.Should().Be(receiverLocation);
        reverse.Lines.Single().ReceiverLocationId.Should().Be(senderLocation);
    }

    [Fact]
    public void Delivery_note_is_immutable_snapshot_and_is_only_issued_after_shipping()
    {
        StockTransferRequest source = StockTransferRequest.Create(Tenant, CompanyA, CompanyA, CompanyB, Guid.NewGuid(), 100m);
        StockTransferLine line = StockTransferLine.Create(Tenant, source.Id, Guid.NewGuid(), null, 2m, "EA", Guid.NewGuid(), Guid.NewGuid());
        source.Lines.Add(line);

        FluentActions.Invoking(() => StockTransferDeliveryNote.Create(source, DateTimeOffset.UtcNow))
            .Should().Throw<InvalidOperationException>();

        source.Check(GroupSettings.Create(Tenant, Business, 1000m, TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "SPAR"), false);
        source.Accept(); source.Reserve(); source.Pick(); source.Ship();
        StockTransferDeliveryNote note = StockTransferDeliveryNote.Create(source, DateTimeOffset.UtcNow, "DRIVER-1");
        note.Number.Should().Be($"DN-{source.Id:N}");
        note.Lines.Should().ContainSingle().Which.Quantity.Should().Be(2m);
    }
}
