using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.Workflow;

/// <summary>
/// Policy threshold arithmetic — below, at, above, and the no-threshold "always gate" case
/// (<c>docs/stages/STAGE-05-workflow.md</c> "Tests / acceptance").
/// </summary>
public sealed class ApprovalPolicyTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void No_threshold_gates_every_occurrence_regardless_of_amount()
    {
        ApprovalPolicy policy = Define(threshold: null);

        policy.Gates(new Money(0.01m, "ZAR")).Should().BeTrue();
        policy.Gates(new Money(1_000_000m, "ZAR")).Should().BeTrue();
        policy.Gates(null).Should().BeTrue();
    }

    [Fact]
    public void An_amount_below_the_threshold_does_not_gate()
    {
        ApprovalPolicy policy = Define(new Money(5000m, "ZAR"));

        policy.Gates(new Money(4999.99m, "ZAR")).Should().BeFalse();
    }

    [Fact]
    public void An_amount_exactly_at_the_threshold_gates()
    {
        ApprovalPolicy policy = Define(new Money(5000m, "ZAR"));

        policy.Gates(new Money(5000.00m, "ZAR")).Should().BeTrue();
    }

    [Fact]
    public void An_amount_above_the_threshold_gates()
    {
        ApprovalPolicy policy = Define(new Money(5000m, "ZAR"));

        policy.Gates(new Money(5000.01m, "ZAR")).Should().BeTrue();
    }

    [Fact]
    public void A_policy_with_a_threshold_but_no_amount_at_hand_gates_conservatively()
    {
        // A configuration mismatch — an action with no money at all, gated by a policy that named a
        // threshold anyway — asks a human rather than guesses.
        ApprovalPolicy policy = Define(new Money(5000m, "ZAR"));

        policy.Gates(null).Should().BeTrue();
    }

    [Fact]
    public void The_threshold_round_trips_through_the_flat_column_pair()
    {
        // ApprovalPolicy.ThresholdAmount is reassembled from two flat nullable columns (see
        // ValueObjectMapping's note on why), not from EF's complex-type machinery. This is the
        // reassembly itself, exercised with no database in the room.
        ApprovalPolicy policy = Define(new Money(1234.5000m, "ZAR"));

        policy.ThresholdAmount.Should().Be(new Money(1234.5000m, "ZAR"));
    }

    [Fact]
    public void A_policy_with_no_threshold_has_none_to_reassemble()
    {
        ApprovalPolicy policy = Define(null);

        policy.ThresholdAmount.Should().BeNull();
    }

    [Fact]
    public void The_key_is_the_dotted_module_entity_type_action_triple()
    {
        ApprovalPolicy policy = ApprovalPolicy.Define(
            Tenant, "procurement", "purchase-order", "post", "procurement.purchase-order.approve");

        policy.Key.Should().Be("procurement.purchase-order.post");
    }

    [Fact]
    public void Fewer_than_one_required_approval_is_refused()
    {
        Action defining = () => ApprovalPolicy.Define(
            Tenant, "procurement", "purchase-order", "post", "procurement.purchase-order.approve", minApprovals: 0);

        defining.Should().Throw<InvalidApprovalPolicyException>()
            .Which.Code.Should().Be("WORKFLOW_POLICY_INVALID");
    }

    [Fact]
    public void Deactivating_and_reactivating_flip_IsActive()
    {
        ApprovalPolicy policy = Define(null);
        policy.IsActive.Should().BeTrue();

        policy.Deactivate();
        policy.IsActive.Should().BeFalse();

        policy.Reactivate();
        policy.IsActive.Should().BeTrue();
    }

    private static ApprovalPolicy Define(Money? threshold) => ApprovalPolicy.Define(
        Tenant,
        "procurement",
        "purchase-order",
        "post",
        "procurement.purchase-order.approve",
        threshold);
}
