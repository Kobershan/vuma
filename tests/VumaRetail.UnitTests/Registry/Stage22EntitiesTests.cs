using VumaRetail.Domain.Registry;

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
}
