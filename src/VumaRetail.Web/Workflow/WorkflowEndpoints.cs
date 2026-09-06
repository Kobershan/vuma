using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Contracts.Workflow;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;
using VumaRetail.Web.Api;
using VumaRetail.Workflow.Approvals;
using VumaRetail.Workflow.Documents;
using VumaRetail.Workflow.Notifications;
using VumaRetail.Workflow.Permissions;

namespace VumaRetail.Web.Workflow;

/// <summary>
/// Approval policies, the unified approval inbox, notifications and document attachments, as API
/// (ADR-019).
/// </summary>
/// <remarks>
/// R3: no capability exists in the desktop UI or the Android app before it exists here. Every write on
/// this group is an ordinary write — none claims the ADR-028 payment exemption, so all of it is
/// refused with <c>403 LICENCE_READ_ONLY</c> while a tenant is read-only, which is correct: a lapsed
/// tenant should not be approving purchase orders any more than it should be raising them.
/// </remarks>
public static class WorkflowEndpoints
{
    /// <summary>Maps the workflow endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaWorkflow(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workflow = endpoints.MapVumaApi().MapGroup("/workflow").WithTags("Workflow");

        RouteGroupBuilder policies = workflow.MapGroup("/approval-policies");

        policies.MapGet("/", (IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListApprovalPoliciesQuery(), cancellationToken))
            .RequirePermission(WorkflowPermissions.PolicyConfigure)
            .Produces<IReadOnlyList<ApprovalPolicySummary>>()
            .WithSummary("Every configured approval policy, active or not.");

        policies.MapPost("/", DefinePolicyAsync)
            .RequirePermission(WorkflowPermissions.PolicyConfigure)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Defines a new approval policy on a gated action.")
            .WithDescription(
                "No policy for a module.entityType.action key means the action is not gated. A "
                + "second active policy on the same key is refused — deactivate the first.");

        policies.MapPost("/{policyId:guid}/deactivate", (Guid policyId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => DeactivateAsync(policyId, dispatcher, cancellationToken))
            .RequirePermission(WorkflowPermissions.PolicyConfigure)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Takes a policy out of force. Requests already raised under it are unaffected.");

        RouteGroupBuilder approvals = workflow.MapGroup("/approvals");

        approvals.MapGet("/", (Guid? storeId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListPendingApprovalsQuery(storeId), cancellationToken))
            .RequirePermission(WorkflowPermissions.ApprovalView)
            .Produces<IReadOnlyList<ApprovalRequestSummary>>()
            .WithSummary("The unified pending-approval inbox — every module, one list.");

        approvals.MapGet("/{requestId:guid}", (Guid requestId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new GetApprovalRequestQuery(requestId), cancellationToken))
            .RequirePermission(WorkflowPermissions.ApprovalView)
            .Produces<ApprovalRequestSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One request's full state.");

        approvals.MapPost("/{requestId:guid}/decide", DecideAsync)
            .RequirePermission(WorkflowPermissions.ApprovalDecide)
            .Produces<ApprovalDecisionResult>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Approves or rejects a pending request.")
            .WithDescription(
                "Deciding also requires holding the policy's own required permission in the "
                + "request's store, and the requester may not decide their own request unless the "
                + "policy explicitly allows self-approval.");

        approvals.MapPost("/{requestId:guid}/cancel", (Guid requestId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => CancelAsync(requestId, dispatcher, cancellationToken))
            .RequirePermission(WorkflowPermissions.ApprovalDecide)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Withdraws a pending request.");

        RouteGroupBuilder notifications = workflow.MapGroup("/notifications").RequireAuthorization();

        notifications.MapGet("/", (bool? unreadOnly, int? limit, IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListMyNotificationsQuery(unreadOnly ?? false, limit ?? 50), cancellationToken))
            .Produces<IReadOnlyList<NotificationSummary>>()
            .WithSummary("The caller's own notifications, newest first.");

        notifications.MapGet("/unread-count", (IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new GetUnreadNotificationCountQuery(), cancellationToken))
            .Produces<int>()
            .WithSummary("How many unread in-app notifications the caller has — the inbox badge count.");

        notifications.MapPost("/{notificationId:guid}/read", (Guid notificationId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => MarkReadAsync(notificationId, dispatcher, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Marks one of the caller's own notifications read.");

        RouteGroupBuilder documents = workflow.MapGroup("/documents");

        documents.MapGet("/", (string module, string entityType, Guid entityId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListDocumentsForEntityQuery(module, entityType, entityId), cancellationToken))
            .RequirePermission(WorkflowPermissions.DocumentView)
            .Produces<IReadOnlyList<DocumentSummary>>()
            .WithSummary("Every document attached to a business record.");

        documents.MapGet("/{documentId:guid}", (Guid documentId, IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new GetDocumentQuery(documentId), cancellationToken))
            .RequirePermission(WorkflowPermissions.DocumentView)
            .Produces<DocumentSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A document's metadata.");

        documents.MapPost("/", AttachAsync)
            .RequirePermission(WorkflowPermissions.DocumentAttach)
            .Produces<DocumentVersionSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Attaches a new document to a business record.");

        documents.MapPost("/{documentId:guid}/versions", AttachVersionAsync)
            .RequirePermission(WorkflowPermissions.DocumentAttach)
            .Produces<DocumentVersionSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Attaches a new version to an existing document. Never overwrites a prior one.");

        documents.MapGet("/{documentId:guid}/versions/{versionNumber:int}/content", DownloadAsync)
            .RequirePermission(WorkflowPermissions.DocumentView)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Downloads one version's bytes, checked against its recorded content hash.");

        return endpoints;
    }

    private static async Task<Created<Guid>> DefinePolicyAsync(
        DefineApprovalPolicyRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid policyId = await dispatcher.SendAsync(
            new DefineApprovalPolicyCommand(
                request.Module,
                request.EntityType,
                request.Action,
                request.RequiredPermission,
                request.ThresholdAmount,
                request.Currency,
                request.MinApprovals,
                request.AllowSelfApproval),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/workflow/approval-policies/{policyId}", policyId);
    }

    private static async Task<NoContent> DeactivateAsync(
        Guid policyId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DeactivateApprovalPolicyCommand(policyId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<ApprovalDecisionResult>> DecideAsync(
        Guid requestId,
        DecideApprovalRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApprovalDecisionResult result = await dispatcher.SendAsync(
            new DecideApprovalCommand(requestId, ParseOutcome(request.Outcome), request.Comment),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(result);
    }

    private static async Task<NoContent> CancelAsync(Guid requestId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelApprovalRequestCommand(requestId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkReadAsync(Guid notificationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new MarkNotificationReadCommand(notificationId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Created<DocumentVersionSummary>> AttachAsync(
        AttachDocumentRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentVersionSummary result = await dispatcher.SendAsync(
            new AttachDocumentCommand(
                request.Module,
                request.EntityType,
                request.EntityId,
                request.Kind,
                request.Title,
                request.ContentType,
                DecodeContent(request.ContentBase64)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/workflow/documents/{result.DocumentId}", result);
    }

    private static async Task<Created<DocumentVersionSummary>> AttachVersionAsync(
        Guid documentId,
        AttachDocumentVersionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentVersionSummary result = await dispatcher.SendAsync(
            new AttachDocumentVersionCommand(documentId, request.ContentType, DecodeContent(request.ContentBase64)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/workflow/documents/{documentId}/versions/{result.VersionNumber}/content",
            result);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> DownloadAsync(
        Guid documentId,
        int versionNumber,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        await using Stream content = await documentService
            .OpenVersionAsync(documentId, versionNumber, cancellationToken)
            .ConfigureAwait(false);

        await using MemoryStream buffer = new();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        DocumentSummary? document = await documentService.FindAsync(documentId, cancellationToken).ConfigureAwait(false);

        return TypedResults.File(buffer.ToArray(), document?.ContentType ?? "application/octet-stream");
    }

    private static byte[] DecodeContent(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new MalformedWorkflowFieldException(nameof(AttachDocumentRequest.ContentBase64), "not valid base64");
        }
    }

    private static ApprovalDecisionOutcome ParseOutcome(string value)
        => Enum.TryParse(value, ignoreCase: true, out ApprovalDecisionOutcome outcome) && Enum.IsDefined(outcome)
            ? outcome
            : throw new MalformedWorkflowFieldException(nameof(DecideApprovalRequest.Outcome), value);
}

/// <summary>A field on a workflow request is not one of the values it may be.</summary>
/// <param name="field">The field.</param>
/// <param name="value">What was sent.</param>
public sealed class MalformedWorkflowFieldException(string field, string value)
    : DomainException(
        "WORKFLOW_FIELD_MALFORMED",
        $"'{value}' is not a valid {field}.",
        DomainProblemKind.Malformed);
