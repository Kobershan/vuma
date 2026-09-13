using FluentAssertions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Projects;

namespace VumaRetail.UnitTests.Projects;

public sealed class ProjectDomainTests
{
    [Fact]
    public void Budget_available_amount_separates_actual_and_commitment()
    {
        ProjectBudget budget = ProjectBudget.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), 1, new Money(10000m, "ZAR"));
        budget.SetMeasures(new Money(3000m, "ZAR"), new Money(2000m, "ZAR"));
        budget.Available.Amount.Should().Be(5000m);
    }

    [Fact]
    public void Unapproved_variation_does_not_change_contract_value()
    {
        ProjectContract contract = ProjectContract.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "C-1", new Money(4000m, "ZAR"));
        ContractVariation variation = ContractVariation.Propose(contract.TenantId, null, contract.CompanyId!.Value, contract.Id, "Scope", new Money(1000m, "ZAR"));
        contract.ValueWithApprovedVariation([variation]).Amount.Should().Be(4000m);
        variation.Approve();
        contract.ValueWithApprovedVariation([variation]).Amount.Should().Be(5000m);
    }

    [Fact]
    public void Milestone_can_be_billed_once_through_approved_state()
    {
        BillingMilestone milestone = BillingMilestone.Plan(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "Delivery", new Money(4000m, "ZAR"));
        FluentActions.Invoking(milestone.MarkBilled).Should().Throw<InvalidOperationException>();
        milestone.Approve(); milestone.MarkBilled();
        FluentActions.Invoking(milestone.MarkBilled).Should().Throw<InvalidOperationException>();
    }
}
