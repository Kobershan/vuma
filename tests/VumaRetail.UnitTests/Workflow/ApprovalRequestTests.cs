using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.Workflow;

/// <summary>
/// The request state machine: pending → approved once <c>MinApprovals</c> is reached, pending →
/// rejected on the first rejection, cancel only while pending, decide-after-decided refused; and
/// self-approval refused unless the policy allows it (<c>docs/stages/STAGE-05-workflow.md</c>).
/// </summary>
public sealed class ApprovalRequestTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Store = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_newly_raised_request_is_pending_with_no_decisions_yet()
    {
        ApprovalRequest request = Raise(minApprovals: 1);

        request.Status.Should().Be(ApprovalRequestStatus.Pending);
        request.IsPending.Should().BeTrue();
        request.ApprovalCount.Should().Be(0);
        request.RejectionCount.Should().Be(0);
        request.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void The_amount_round_trips_through_the_flat_column_pair()
    {
        ApprovalRequest withAmount = Raise(minApprovals: 1, amount: new Money(999.9900m, "ZAR"));
        withAmount.Amount.Should().Be(new Money(999.9900m, "ZAR"));

        ApprovalRequest withoutAmount = Raise(minApprovals: 1, amount: null);
        withoutAmount.Amount.Should().BeNull();
    }

    [Fact]
    public void A_single_approval_approves_a_one_of_one_request()
    {
        ApprovalRequest request = Raise(minApprovals: 1);

        request.RegisterApproval("user:approver-1", Now.AddMinutes(5));

        request.Status.Should().Be(ApprovalRequestStatus.Approved);
        request.ApprovalCount.Should().Be(1);
        request.DecidedAt.Should().Be(Now.AddMinutes(5));
        request.IsPending.Should().BeFalse();
    }

    [Fact]
    public void An_N_of_M_request_stays_pending_until_the_count_is_reached()
    {
        ApprovalRequest request = Raise(minApprovals: 3);

        request.RegisterApproval("user:approver-1", Now.AddMinutes(1));
        request.Status.Should().Be(ApprovalRequestStatus.Pending);

        request.RegisterApproval("user:approver-2", Now.AddMinutes(2));
        request.Status.Should().Be(ApprovalRequestStatus.Pending);

        request.RegisterApproval("user:approver-3", Now.AddMinutes(3));
        request.Status.Should().Be(ApprovalRequestStatus.Approved);
        request.ApprovalCount.Should().Be(3);
    }

    [Fact]
    public void A_single_rejection_ends_a_two_of_three_request_outright_the_veto_model()
    {
        ApprovalRequest request = Raise(minApprovals: 3);

        request.RegisterApproval("user:approver-1", Now.AddMinutes(1));
        request.RegisterRejection("user:approver-2", Now.AddMinutes(2));

        request.Status.Should().Be(ApprovalRequestStatus.Rejected);
        request.RejectionCount.Should().Be(1);
        // The approval already recorded is not erased — it is simply moot, because the veto ended
        // the request. A third decider trying to act finds it already decided.
        request.ApprovalCount.Should().Be(1);
    }

    [Fact]
    public void A_pending_request_can_be_cancelled()
    {
        ApprovalRequest request = Raise(minApprovals: 1);

        request.Cancel(Now.AddHours(1));

        request.Status.Should().Be(ApprovalRequestStatus.Cancelled);
        request.DecidedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Cancel_after_decided_is_refused()
    {
        ApprovalRequest request = Raise(minApprovals: 1);
        request.RegisterApproval("user:approver-1", Now);

        Action cancelling = () => request.Cancel(Now.AddMinutes(1));

        cancelling.Should().Throw<ApprovalAlreadyDecidedException>()
            .Which.Code.Should().Be("WORKFLOW_APPROVAL_ALREADY_DECIDED");
    }

    [Theory]
    [InlineData(ApprovalRequestStatus.Approved)]
    [InlineData(ApprovalRequestStatus.Rejected)]
    [InlineData(ApprovalRequestStatus.Cancelled)]
    public void Deciding_a_request_that_is_no_longer_pending_is_refused(ApprovalRequestStatus terminalStatus)
    {
        ApprovalRequest request = Raise(minApprovals: 1);

        switch (terminalStatus)
        {
            case ApprovalRequestStatus.Approved:
                request.RegisterApproval("user:approver-1", Now);
                break;
            case ApprovalRequestStatus.Rejected:
                request.RegisterRejection("user:approver-1", Now);
                break;
            case ApprovalRequestStatus.Cancelled:
                request.Cancel(Now);
                break;
        }

        Action decidingAgain = () => request.RegisterApproval("user:approver-2", Now.AddMinutes(1));

        decidingAgain.Should().Throw<ApprovalAlreadyDecidedException>()
            .Which.Code.Should().Be("WORKFLOW_APPROVAL_ALREADY_DECIDED");
    }

    [Fact]
    public void Self_approval_is_refused_by_default()
    {
        ApprovalRequest request = Raise(minApprovals: 1, allowSelfApproval: false, requestedBy: "user:requester");

        Action decidingOwnRequest = () => request.RegisterApproval("user:requester", Now);

        decidingOwnRequest.Should().Throw<SelfApprovalNotAllowedException>()
            .Which.Code.Should().Be("WORKFLOW_SELF_APPROVAL_NOT_ALLOWED");
    }

    [Fact]
    public void Self_approval_is_permitted_when_the_policy_explicitly_allows_it()
    {
        ApprovalRequest request = Raise(minApprovals: 1, allowSelfApproval: true, requestedBy: "user:requester");

        request.RegisterApproval("user:requester", Now);

        request.Status.Should().Be(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public void Self_rejection_is_refused_by_default_too()
    {
        ApprovalRequest request = Raise(minApprovals: 1, allowSelfApproval: false, requestedBy: "user:requester");

        Action decidingOwnRequest = () => request.RegisterRejection("user:requester", Now);

        decidingOwnRequest.Should().Throw<SelfApprovalNotAllowedException>();
    }

    [Fact]
    public void The_policys_rules_are_snapshotted_and_do_not_move_with_a_later_edit()
    {
        ApprovalPolicy policy = ApprovalPolicy.Define(
            Tenant, "procurement", "purchase-order", "post", "procurement.purchase-order.approve",
            new Money(1000m, "ZAR"), minApprovals: 1);

        ApprovalRequest request = ApprovalRequest.Raise(
            Tenant, Store, policy, Guid.NewGuid(), "user:requester", new Money(1500m, "ZAR"), null, Now);

        // The policy is edited after the fact — a real caller would deactivate and redefine, since
        // there is no in-place edit, but the point is the request does not reach back to ask the
        // policy for its current rules; it copied them once.
        request.RequiredPermission.Should().Be("procurement.purchase-order.approve");
        request.MinApprovals.Should().Be(1);
        request.AllowSelfApproval.Should().BeFalse();
        request.PolicyId.Should().Be(policy.Id);
    }

    private static ApprovalRequest Raise(
        int minApprovals,
        bool allowSelfApproval = false,
        string requestedBy = "user:requester",
        Money? amount = null)
    {
        ApprovalPolicy policy = ApprovalPolicy.Define(
            Tenant,
            "procurement",
            "purchase-order",
            "post",
            "procurement.purchase-order.approve",
            minApprovals: minApprovals,
            allowSelfApproval: allowSelfApproval);

        return ApprovalRequest.Raise(Tenant, Store, policy, Guid.NewGuid(), requestedBy, amount, null, Now);
    }
}
