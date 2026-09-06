namespace VumaRetail.Contracts.Workflow;

/// <summary>What a caller sends to define a new approval policy.</summary>
/// <param name="Module">The gated action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="RequiredPermission">The permission a decider must hold.</param>
/// <param name="ThresholdAmount">The gating amount, or <c>null</c> to gate every occurrence.</param>
/// <param name="Currency">The currency of <paramref name="ThresholdAmount"/>, required when it is set.</param>
/// <param name="MinApprovals">How many approvals are required.</param>
/// <param name="AllowSelfApproval">Whether the requester may decide their own request.</param>
public sealed record DefineApprovalPolicyRequest(
    string Module,
    string EntityType,
    string Action,
    string RequiredPermission,
    decimal? ThresholdAmount,
    string? Currency,
    int MinApprovals = 1,
    bool AllowSelfApproval = false);

/// <summary>What a caller sends to decide a pending approval request.</summary>
/// <param name="Outcome"><c>Approved</c> or <c>Rejected</c>.</param>
/// <param name="Comment">Free text from the decider.</param>
public sealed record DecideApprovalRequest(string Outcome, string? Comment = null);

/// <summary>What a caller sends to attach a document.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="EntityType">What kind of business record this belongs to.</param>
/// <param name="EntityId">The business record's id.</param>
/// <param name="Kind">What sort of document this is.</param>
/// <param name="Title">A human title.</param>
/// <param name="ContentType">The MIME type of the bytes.</param>
/// <param name="ContentBase64">The bytes, base64-encoded.</param>
public sealed record AttachDocumentRequest(
    string Module,
    string EntityType,
    Guid EntityId,
    string Kind,
    string Title,
    string ContentType,
    string ContentBase64);

/// <summary>What a caller sends to attach a new version to an existing document.</summary>
/// <param name="ContentType">The MIME type of the bytes.</param>
/// <param name="ContentBase64">The bytes, base64-encoded.</param>
public sealed record AttachDocumentVersionRequest(string ContentType, string ContentBase64);
