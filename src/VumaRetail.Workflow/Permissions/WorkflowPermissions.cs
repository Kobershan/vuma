using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Workflow.Permissions;

/// <summary>
/// What the <c>workflow</c> module lets somebody do.
/// </summary>
/// <remarks>
/// Deciding an approval requires holding the <em>policy's own</em> <c>RequiredPermission</c> as well
/// as <see cref="ApprovalDecide"/> — the generic grant gets a user into the inbox, the policy-specific
/// one is what actually lets them act on any given request. See <see cref="Application.Abstractions.Workflow.IApprovalService"/>.
/// </remarks>
public sealed class WorkflowPermissions : IModulePermissions
{
    /// <summary>Configure which actions require approval, and under what rules.</summary>
    public const string PolicyConfigure = "workflow.policy.configure";

    /// <summary>See the unified pending-approval inbox.</summary>
    public const string ApprovalView = "workflow.approval.view";

    /// <summary>Approve or reject a pending request, subject to the policy's own required permission.</summary>
    public const string ApprovalDecide = "workflow.approval.decide";

    /// <summary>See documents attached to a business record.</summary>
    public const string DocumentView = "workflow.document.view";

    /// <summary>Attach or generate a document.</summary>
    public const string DocumentAttach = "workflow.document.attach";

    /// <inheritdoc />
    public string Module => "workflow";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(
            PermissionKey.Parse(PolicyConfigure),
            "Decide which actions across the business require approval, and by whom.",
            IsHighRisk: true),
        new(PermissionKey.Parse(ApprovalView), "See approvals waiting for a decision, across every module."),
        new(PermissionKey.Parse(ApprovalDecide), "Approve or reject requests this user is entitled to decide."),
        new(PermissionKey.Parse(DocumentView), "See documents attached to a business record."),
        new(PermissionKey.Parse(DocumentAttach), "Attach or generate a document on a business record."),
    ];
}

/// <summary>
/// The <c>workflow</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core: approvals, notifications and documents are infrastructure every other module configures
/// against (ADR-019), not a feature a tenant buys or does without.
/// </remarks>
public sealed class WorkflowModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "workflow";

    /// <inheritdoc />
    public string LicenceFlag => "workflow";

    /// <inheritdoc />
    public string Description => "Approvals, notifications and document attachments.";

    /// <inheritdoc />
    public bool IsCore => true;
}
