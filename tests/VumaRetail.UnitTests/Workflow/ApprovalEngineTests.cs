using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Identity;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;
using VumaRetail.Workflow.Approvals;

namespace VumaRetail.UnitTests.Workflow;

/// <summary>
/// <see cref="ApprovalEngine"/> unit tests — NSubstitute over the repository and
/// <see cref="IRoleRepository"/> ports, no database (<c>docs/stages/STAGE-05-workflow.md</c>).
/// </summary>
public sealed class ApprovalEngineTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Store = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ApproverUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RequesterUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private const string RequesterPrincipal = "user:44444444-4444-4444-4444-444444444444";
    private const string ApproverPrincipal = "user:33333333-3333-3333-3333-333333333333";
    private const string RequiredPermission = "procurement.purchase-order.approve";

    private readonly IApprovalPolicyRepository _policies = Substitute.For<IApprovalPolicyRepository>();
    private readonly IApprovalRequestRepository _requests = Substitute.For<IApprovalRequestRepository>();
    private readonly IApprovalDecisionRepository _decisions = Substitute.For<IApprovalDecisionRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPrincipalAccessor _principal = Substitute.For<IPrincipalAccessor>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ApprovalEngineTests()
    {
        _tenant.TenantId.Returns(Tenant);
        _tenant.StoreId.Returns(Store);
        _clock.UtcNow.Returns(Now);
    }

    private ApprovalEngine CreateEngine()
        => new(_policies, _requests, _decisions, _roles, _principal, _tenant, _clock);

    [Fact]
    public async Task No_policy_configured_auto_approves_with_no_request_row_created()
    {
        _policies.FindActiveAsync("procurement", "purchase-order", "post", Arg.Any<CancellationToken>())
            .Returns((ApprovalPolicy?)null);

        ApprovalOutcome outcome = await CreateEngine().EvaluateAsync(
            new ApprovalContext("procurement", "purchase-order", "post", Guid.NewGuid(), new Money(50_000m, "ZAR")));

        outcome.Kind.Should().Be(ApprovalOutcomeKind.AutoApproved);
        outcome.MayProceed.Should().BeTrue();
        outcome.RequestId.Should().BeNull();
        _requests.DidNotReceive().Add(Arg.Any<ApprovalRequest>());
    }

    [Fact]
    public async Task Below_threshold_auto_approves()
    {
        ApprovalPolicy policy = DefinePolicy(new Money(5000m, "ZAR"));
        _policies.FindActiveAsync("procurement", "purchase-order", "post", Arg.Any<CancellationToken>())
            .Returns(policy);

        ApprovalOutcome outcome = await CreateEngine().EvaluateAsync(
            new ApprovalContext("procurement", "purchase-order", "post", Guid.NewGuid(), new Money(1000m, "ZAR")));

        outcome.MayProceed.Should().BeTrue();
        _requests.DidNotReceive().Add(Arg.Any<ApprovalRequest>());
    }

    [Fact]
    public async Task At_or_above_threshold_creates_a_pending_request()
    {
        ApprovalPolicy policy = DefinePolicy(new Money(5000m, "ZAR"));
        _policies.FindActiveAsync("procurement", "purchase-order", "post", Arg.Any<CancellationToken>())
            .Returns(policy);
        _principal.Principal.Returns(RequesterPrincipal);

        ApprovalOutcome outcome = await CreateEngine().EvaluateAsync(
            new ApprovalContext("procurement", "purchase-order", "post", Guid.NewGuid(), new Money(5000m, "ZAR")));

        outcome.Kind.Should().Be(ApprovalOutcomeKind.Pending);
        outcome.MayProceed.Should().BeFalse();
        outcome.RequestId.Should().NotBeNull();
        _requests.Received(1).Add(Arg.Is<ApprovalRequest>(request =>
            request.Status == ApprovalRequestStatus.Pending
            && request.RequestedBy == RequesterPrincipal
            && request.PolicyId == policy.Id));
    }

    [Fact]
    public async Task A_decider_lacking_the_required_permission_is_refused()
    {
        ApprovalRequest request = RaisePending(minApprovals: 1);
        ArrangeFind(request);
        _principal.Principal.Returns(ApproverPrincipal);
        _decisions.HasDecidedAsync(request.Id, ApproverPrincipal, Arg.Any<CancellationToken>()).Returns(false);
        _roles.ListEffectivePermissionsAsync(ApproverUserId, Store, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyCollection<string>)new List<string> { "some.other.permission" });

        Func<Task> deciding = () => CreateEngine().DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, null);

        (await deciding.Should().ThrowAsync<ApproverLacksRequiredPermissionException>())
            .Which.Code.Should().Be("WORKFLOW_APPROVER_LACKS_PERMISSION");
    }

    [Fact]
    public async Task A_decider_holding_the_required_permission_can_approve()
    {
        ApprovalRequest request = RaisePending(minApprovals: 1);
        ArrangeFind(request);
        ArrangeApprover(request);

        ApprovalDecisionResult result = await CreateEngine()
            .DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, "looks fine");

        result.Status.Should().Be(ApprovalRequestStatus.Approved);
        result.ApprovalCount.Should().Be(1);
        _decisions.Received(1).Add(Arg.Is<ApprovalDecisionEntry>(entry =>
            entry.ApprovalRequestId == request.Id
            && entry.Outcome == ApprovalDecisionOutcome.Approved
            && entry.DecidedBy == ApproverPrincipal
            && entry.Comment == "looks fine"));
    }

    [Fact]
    public async Task Self_approval_is_refused_when_the_decider_is_the_requester()
    {
        ApprovalRequest request = RaisePending(minApprovals: 1, requestedBy: ApproverPrincipal);
        ArrangeFind(request);
        ArrangeApprover(request);

        Func<Task> deciding = () => CreateEngine().DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, null);

        await deciding.Should().ThrowAsync<SelfApprovalNotAllowedException>();
    }

    [Fact]
    public async Task Self_approval_is_permitted_when_the_policy_allows_it()
    {
        ApprovalRequest request = RaisePending(minApprovals: 1, requestedBy: ApproverPrincipal, allowSelfApproval: true);
        ArrangeFind(request);
        ArrangeApprover(request);

        ApprovalDecisionResult result = await CreateEngine()
            .DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, null);

        result.Status.Should().Be(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task The_same_principal_deciding_twice_is_refused()
    {
        ApprovalRequest request = RaisePending(minApprovals: 2);
        ArrangeFind(request);
        _principal.Principal.Returns(ApproverPrincipal);
        _decisions.HasDecidedAsync(request.Id, ApproverPrincipal, Arg.Any<CancellationToken>()).Returns(true);

        Func<Task> decidingAgain = () => CreateEngine().DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, null);

        (await decidingAgain.Should().ThrowAsync<ApproverAlreadyDecidedException>())
            .Which.Code.Should().Be("WORKFLOW_APPROVER_ALREADY_DECIDED");
    }

    [Fact]
    public async Task N_of_M_approval_counting_reaches_approved_only_at_the_configured_count()
    {
        ApprovalRequest request = RaisePending(minApprovals: 2);
        ArrangeFind(request);
        ArrangeApprover(request);

        ApprovalDecisionResult afterOne = await CreateEngine()
            .DecideAsync(request.Id, ApprovalDecisionOutcome.Approved, null);

        afterOne.Status.Should().Be(ApprovalRequestStatus.Pending);
        afterOne.ApprovalCount.Should().Be(1);
    }

    [Fact]
    public async Task A_single_rejection_ends_a_two_of_three_request_outright()
    {
        ApprovalRequest request = RaisePending(minApprovals: 3);
        ArrangeFind(request);
        request.RegisterApproval("user:someone-else", Now.AddMinutes(-5));
        ArrangeApprover(request);

        ApprovalDecisionResult result = await CreateEngine()
            .DecideAsync(request.Id, ApprovalDecisionOutcome.Rejected, "not this quarter");

        result.Status.Should().Be(ApprovalRequestStatus.Rejected);
    }

    [Fact]
    public async Task Deciding_a_request_that_does_not_exist_is_refused()
    {
        Guid missing = Guid.NewGuid();
        _requests.FindAsync(missing, Arg.Any<CancellationToken>()).Returns((ApprovalRequest?)null);

        Func<Task> deciding = () => CreateEngine().DecideAsync(missing, ApprovalDecisionOutcome.Approved, null);

        (await deciding.Should().ThrowAsync<ApprovalRequestNotFoundException>())
            .Which.Code.Should().Be("WORKFLOW_APPROVAL_REQUEST_NOT_FOUND");
    }

    [Fact]
    public async Task Cancel_withdraws_a_pending_request()
    {
        ApprovalRequest request = RaisePending(minApprovals: 1);
        ArrangeFind(request);

        await CreateEngine().CancelAsync(request.Id);

        request.Status.Should().Be(ApprovalRequestStatus.Cancelled);
    }

    [Fact]
    public async Task ListPendingAsync_maps_every_pending_request_to_a_summary()
    {
        ApprovalRequest first = RaisePending(minApprovals: 1);
        ApprovalRequest second = RaisePending(minApprovals: 2);

        _requests.ListPendingAsync(null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ApprovalRequest>)new List<ApprovalRequest> { first, second });

        IReadOnlyList<ApprovalRequestSummary> summaries = await CreateEngine().ListPendingAsync();

        summaries.Should().HaveCount(2);
        summaries.Select(summary => summary.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task FindAsync_returns_null_for_an_unknown_request()
    {
        Guid missing = Guid.NewGuid();
        _requests.FindAsync(missing, Arg.Any<CancellationToken>()).Returns((ApprovalRequest?)null);

        ApprovalRequestSummary? summary = await CreateEngine().FindAsync(missing);

        summary.Should().BeNull();
    }

    private void ArrangeFind(ApprovalRequest request)
        => _requests.FindAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

    private void ArrangeApprover(ApprovalRequest request)
    {
        _principal.Principal.Returns(ApproverPrincipal);
        _decisions.HasDecidedAsync(request.Id, ApproverPrincipal, Arg.Any<CancellationToken>()).Returns(false);
        _roles.ListEffectivePermissionsAsync(ApproverUserId, Store, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyCollection<string>)new List<string> { RequiredPermission });
    }

    private static ApprovalPolicy DefinePolicy(Money? threshold, int minApprovals = 1, bool allowSelfApproval = false)
        => ApprovalPolicy.Define(
            Tenant, "procurement", "purchase-order", "post", RequiredPermission, threshold, minApprovals, allowSelfApproval);

    private static ApprovalRequest RaisePending(
        int minApprovals,
        string requestedBy = RequesterPrincipal,
        bool allowSelfApproval = false)
    {
        ApprovalPolicy policy = DefinePolicy(new Money(5000m, "ZAR"), minApprovals, allowSelfApproval);

        return ApprovalRequest.Raise(
            Tenant, Store, policy, Guid.NewGuid(), requestedBy, new Money(5000m, "ZAR"), null, Now);
    }
}
