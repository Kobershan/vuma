using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Opportunity pipeline: open stages move forward, win needs a customer, loss needs a reason,
/// closed deals stay closed.
/// </summary>
public sealed class OpportunityPipelineTests
{
    private static Opportunity Open(decimal amount = 10000m) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Shoprite rollout", new Money(amount, "ZAR"), 10);

    [Fact]
    public void New_opportunity_starts_in_prospecting()
    {
        Open().Stage.Should().Be(OpportunityStage.Prospecting);
    }

    [Fact]
    public void Open_stages_move_forward_only()
    {
        Opportunity deal = Open();
        deal.MoveTo(OpportunityStage.Proposal, 40);
        deal.Stage.Should().Be(OpportunityStage.Proposal);
        Action act = () => deal.MoveTo(OpportunityStage.Qualification, 30);
        act.Should().Throw<OpportunityStageTransitionException>();
    }

    [Fact]
    public void Win_requires_a_customer()
    {
        Opportunity deal = Open();
        deal.Win(Guid.NewGuid());
        deal.Stage.Should().Be(OpportunityStage.Won);
        deal.Probability.Should().Be(100);
    }

    [Fact]
    public void Win_without_customer_is_refused()
    {
        Opportunity deal = Open();
        Action act = () => deal.Win(Guid.Empty);
        act.Should().Throw<OpportunityMissingCustomerException>();
    }

    [Fact]
    public void Lose_requires_a_reason()
    {
        Opportunity deal = Open();
        deal.Lose("chose a competitor");
        deal.Stage.Should().Be(OpportunityStage.Lost);
        deal.LossReason.Should().Be("chose a competitor");

        Opportunity silent = Open();
        Action act = () => silent.Lose("  ");
        act.Should().Throw<OpportunityLossReasonRequiredException>();
    }

    [Fact]
    public void Closed_deals_stay_closed()
    {
        Opportunity deal = Open();
        deal.Win(Guid.NewGuid());
        Action move = () => deal.MoveTo(OpportunityStage.Negotiation, 90);
        Action lose = () => deal.Lose("too late");
        move.Should().Throw<OpportunityStageTransitionException>();
        lose.Should().Throw<OpportunityStageTransitionException>();
    }

    [Fact]
    public void Expected_value_is_money_with_currency()
    {
        Open(25000m).ExpectedValue.Should().Be(new Money(25000m, "ZAR"));
    }
}
