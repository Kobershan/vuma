using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Contracts.Workflow;
using VumaRetail.Domain.Workflow;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Workflow.Approvals;
using VumaRetail.Workflow.Permissions;

namespace VumaRetail.IntegrationTests.Workflow;

/// <summary>
/// The approval engine end to end, against real PostgreSQL (<c>docs/stages/STAGE-05-workflow.md</c>
/// "Tests / acceptance"): define a policy → raise a request through <see cref="IApprovalService"/>
/// from a throwaway test handler → decide it → assert the persisted state and the
/// <see cref="ApprovalDecisionEntry"/> row; the unified inbox aggregates every module.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApprovalWorkflowTests(PostgresFixture fixture)
{
    // A real, catalogue-declared permission stands in for the module-specific "who may approve a
    // purchase order" grant a real procurement module (Stage 12) would declare. ApprovalPolicy places
    // no constraint linking RequiredPermission's own module to the policy's Module/EntityType/Action —
    // they are independent fields — so any well-formed, declared permission proves the mechanism.
    private const string RequiredPermission = PlatformPermissions.StoreManage;

    [Fact]
    public async Task Defining_a_policy_and_raising_below_threshold_auto_approves_with_no_pending_request()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 1);

        ApprovalOutcome outcome = await harness.SendAsync(new RaiseTestApprovalCommand(
            "procurement", "purchase-order", "post", Guid.NewGuid(), 1000m, "ZAR", "small order"));

        outcome.Kind.Should().Be(ApprovalOutcomeKind.AutoApproved);
        outcome.MayProceed.Should().BeTrue();
    }

    [Fact]
    public async Task Raising_at_or_above_threshold_creates_a_pending_request_that_survives_the_transaction()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        Guid policyId = await DefinePolicyAsync(harness, minApprovals: 1);

        Guid subjectId = Guid.NewGuid();
        ApprovalOutcome outcome = await harness.SendAsync(new RaiseTestApprovalCommand(
            "procurement", "purchase-order", "post", subjectId, 9000m, "ZAR", "big order"));

        outcome.Kind.Should().Be(ApprovalOutcomeKind.Pending);
        outcome.RequestId.Should().NotBeNull();

        ApprovalRequestSummary summary = await harness.QueryAsync(new GetApprovalRequestQuery(outcome.RequestId!.Value));

        summary.Status.Should().Be(ApprovalRequestStatus.Pending);
        summary.PolicyId.Should().Be(policyId);
        summary.SubjectEntityId.Should().Be(subjectId);
        summary.Amount!.Value.Amount.Should().Be(9000m);
    }

    [Fact]
    public async Task Deciding_a_request_persists_the_new_status_and_an_append_only_decision_entry()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 1);
        Guid approverId = await harness.CreateUserAsync(
            "approver",
            permissions: [WorkflowPermissions.ApprovalDecide, RequiredPermission]);

        ApprovalOutcome raised = await harness.SendAsync(new RaiseTestApprovalCommand(
            "procurement", "purchase-order", "post", Guid.NewGuid(), 9000m, "ZAR", null));

        HttpClient approver = await harness.SignInAsync("approver");

        HttpResponseMessage decideResponse = await approver.PostAsJsonAsync(
            $"/api/v1/workflow/approvals/{raised.RequestId}/decide",
            new DecideApprovalRequest("Approved", "looks right"));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        ApprovalRequestSummary summary = await harness.QueryAsync(new GetApprovalRequestQuery(raised.RequestId!.Value));
        summary.Status.Should().Be(ApprovalRequestStatus.Approved);
        summary.ApprovalCount.Should().Be(1);

        // The append-only decision history, read straight from the database rather than through the
        // engine, so this assertion does not simply restate what DecideAsync already returned.
        await harness.InScopeAsync(async provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            ApprovalDecisionEntry entry = await context.ApprovalDecisionEntries
                .SingleAsync(candidate => candidate.ApprovalRequestId == raised.RequestId!.Value);

            entry.Outcome.Should().Be(ApprovalDecisionOutcome.Approved);
            entry.Comment.Should().Be("looks right");
            entry.DecidedBy.Should().Be($"user:{approverId}");

            return true;
        });
    }

    [Fact]
    public async Task Rejection_by_a_second_decider_ends_a_two_of_three_request_outright()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 3);

        await harness.CreateUserAsync(
            "approver-1", permissions: [WorkflowPermissions.ApprovalDecide, RequiredPermission]);
        await harness.CreateUserAsync(
            "approver-2", permissions: [WorkflowPermissions.ApprovalDecide, RequiredPermission]);

        ApprovalOutcome raised = await harness.SendAsync(new RaiseTestApprovalCommand(
            "procurement", "purchase-order", "post", Guid.NewGuid(), 9000m, "ZAR", null));

        HttpClient approverOne = await harness.SignInAsync("approver-1");
        (await approverOne.PostAsJsonAsync(
                $"/api/v1/workflow/approvals/{raised.RequestId}/decide",
                new DecideApprovalRequest("Approved")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        HttpClient approverTwo = await harness.SignInAsync("approver-2");
        HttpResponseMessage rejection = await approverTwo.PostAsJsonAsync(
            $"/api/v1/workflow/approvals/{raised.RequestId}/decide",
            new DecideApprovalRequest("Rejected", "budget exceeded"));

        rejection.StatusCode.Should().Be(HttpStatusCode.OK);

        ApprovalRequestSummary summary = await harness.QueryAsync(new GetApprovalRequestQuery(raised.RequestId!.Value));
        summary.Status.Should().Be(ApprovalRequestStatus.Rejected);
    }

    [Fact]
    public async Task The_unified_inbox_lists_pending_requests_regardless_of_which_module_raised_them()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 1, module: "procurement", entityType: "purchase-order");
        await DefinePolicyAsync(
            harness, minApprovals: 1, module: "hr", entityType: "leave-request",
            permission: IdentityPermissions.RoleManage);

        await harness.CreateUserAsync("viewer", permissions: WorkflowPermissions.ApprovalView);

        await harness.SendAsync(new RaiseTestApprovalCommand(
            "procurement", "purchase-order", "post", Guid.NewGuid(), 9000m, "ZAR", null));
        await harness.SendAsync(new RaiseTestApprovalCommand(
            "hr", "leave-request", "post", Guid.NewGuid(), null, null, null));

        HttpClient viewer = await harness.SignInAsync("viewer");
        IReadOnlyList<ApprovalRequestSummary>? inbox = await viewer
            .GetFromJsonAsync<IReadOnlyList<ApprovalRequestSummary>>("/api/v1/workflow/approvals");

        inbox.Should().NotBeNull();
        inbox!.Select(request => request.Module).Should().BeEquivalentTo(["procurement", "hr"]);
        inbox.Should().OnlyContain(request => request.Status == ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task Self_approval_is_refused_over_http_with_a_422_rule_violation()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 1, allowSelfApproval: false);

        await harness.CreateUserAsync(
            "requester", permissions: [WorkflowPermissions.ApprovalDecide, RequiredPermission]);
        HttpClient requester = await harness.SignInAsync("requester");

        // Raised as this signed-in user through the real dispatcher (WorkflowTestEndpointsStartupFilter),
        // so ApprovalRequest.RequestedBy is genuinely "user:{requesterId}" — the same identity that then
        // tries to decide it.
        Guid requestId = await RaiseOverHttpAsync(requester, "procurement", "purchase-order", "post", 9000m, "ZAR");

        HttpResponseMessage decideResponse = await requester.PostAsJsonAsync(
            $"/api/v1/workflow/approvals/{requestId}/decide",
            new DecideApprovalRequest("Approved"));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        JsonDocument problem = JsonDocument.Parse(await decideResponse.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("WORKFLOW_SELF_APPROVAL_NOT_ALLOWED");
    }

    [Fact]
    public async Task Self_approval_is_permitted_over_http_when_the_policy_explicitly_allows_it()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await DefinePolicyAsync(harness, minApprovals: 1, allowSelfApproval: true);

        await harness.CreateUserAsync(
            "requester", permissions: [WorkflowPermissions.ApprovalDecide, RequiredPermission]);
        HttpClient requester = await harness.SignInAsync("requester");

        Guid requestId = await RaiseOverHttpAsync(requester, "procurement", "purchase-order", "post", 9000m, "ZAR");

        HttpResponseMessage decideResponse = await requester.PostAsJsonAsync(
            $"/api/v1/workflow/approvals/{requestId}/decide",
            new DecideApprovalRequest("Approved"));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_second_active_policy_for_the_same_key_is_refused_with_409_conflict()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await harness.CreateUserAsync("configurer", permissions: WorkflowPermissions.PolicyConfigure);
        HttpClient configurer = await harness.SignInAsync("configurer");

        DefineApprovalPolicyRequest request = new(
            "procurement", "purchase-order", "post", RequiredPermission, 5000m, "ZAR", 1, false);

        (await configurer.PostAsJsonAsync("/api/v1/workflow/approval-policies", request))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage secondResponse = await configurer.PostAsJsonAsync(
            "/api/v1/workflow/approval-policies", request);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        JsonDocument problem = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("WORKFLOW_POLICY_ALREADY_ACTIVE");
    }

    [Fact]
    public async Task Defining_a_policy_without_the_permission_is_forbidden()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await harness.CreateUserAsync("cashier");
        HttpClient cashier = await harness.SignInAsync("cashier");

        HttpResponseMessage response = await cashier.PostAsJsonAsync(
            "/api/v1/workflow/approval-policies",
            new DefineApprovalPolicyRequest(
                "procurement", "purchase-order", "post", RequiredPermission, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Defining_a_policy_unauthenticated_is_unauthorised()
    {
        await using ApiHarness harness = await CreateHarnessAsync();

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/api/v1/workflow/approval-policies",
            new DefineApprovalPolicyRequest(
                "procurement", "purchase-order", "post", RequiredPermission, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deciding_a_request_that_does_not_exist_is_a_404_not_found()
    {
        await using ApiHarness harness = await CreateHarnessAsync();
        await harness.CreateUserAsync("approver", permissions: WorkflowPermissions.ApprovalDecide);
        HttpClient approver = await harness.SignInAsync("approver");

        HttpResponseMessage response = await approver.PostAsJsonAsync(
            $"/api/v1/workflow/approvals/{Guid.NewGuid()}/decide",
            new DecideApprovalRequest("Approved"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> RaiseOverHttpAsync(
        HttpClient client, string module, string entityType, string action, decimal? amount, string? currency)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/test/workflow/raise",
            new RaiseTestApprovalCommand(module, entityType, action, Guid.NewGuid(), amount, currency, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("requestId").GetGuid();
    }

    private static async Task<Guid> DefinePolicyAsync(
        ApiHarness harness,
        int minApprovals,
        bool allowSelfApproval = false,
        string module = "procurement",
        string entityType = "purchase-order",
        string permission = RequiredPermission)
        => await harness.SendAsync(new DefineApprovalPolicyCommand(
            module, entityType, "post", permission, 5000m, "ZAR", minApprovals, allowSelfApproval));

    private Task<ApiHarness> CreateHarnessAsync() => ApiHarness.CreateAsync(
        fixture,
        configureServices: services =>
        {
            // Registered explicitly rather than by assembly scan: AddVumaMessaging's Scrutor scan
            // would sweep the whole IntegrationTests assembly, including other tests' own throwaway
            // handlers (VumaRetail.IntegrationTests.Messaging.EchoCommandHandler among them) whose
            // dependencies this host was never meant to satisfy.
            services.AddScoped<
                VumaRetail.Application.Abstractions.ICommandHandler<RaiseTestApprovalCommand, ApprovalOutcome>,
                RaiseTestApprovalCommandHandler>();
            services.AddSingleton<IStartupFilter, WorkflowTestEndpointsStartupFilter>();
        });
}
