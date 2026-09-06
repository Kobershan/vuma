using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Application.Abstractions.Workflow;

/// <summary>What a module is asking to have gated.</summary>
/// <param name="Module">The action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="SubjectEntityId">The business record being gated, by id.</param>
/// <param name="Amount">The amount at hand, if the action carries one.</param>
/// <param name="Reason">Free text for a decider's context.</param>
/// <param name="StoreId">The store the action is being taken in, if any.</param>
public sealed record ApprovalContext(
    string Module,
    string EntityType,
    string Action,
    Guid SubjectEntityId,
    Money? Amount = null,
    string? Reason = null,
    Guid? StoreId = null);

/// <summary>What evaluating a gated action produced.</summary>
/// <param name="Kind">Whether the action may proceed now, or is waiting.</param>
/// <param name="RequestId">The pending request's id, when <paramref name="Kind"/> is <c>Pending</c>.</param>
public sealed record ApprovalOutcome(ApprovalOutcomeKind Kind, Guid? RequestId = null)
{
    /// <summary>True when the caller may proceed with the gated action immediately.</summary>
    public bool MayProceed => Kind is ApprovalOutcomeKind.AutoApproved;

    /// <summary>An outcome meaning no policy applied, or the amount was below its threshold.</summary>
    public static ApprovalOutcome NoGate { get; } = new(ApprovalOutcomeKind.AutoApproved);
}

/// <summary>What deciding a request produced.</summary>
/// <param name="RequestId">The request.</param>
/// <param name="Status">Where it stands after this decision.</param>
/// <param name="ApprovalCount">How many approvals have been recorded.</param>
/// <param name="MinApprovals">How many are required.</param>
public sealed record ApprovalDecisionResult(
    Guid RequestId,
    ApprovalRequestStatus Status,
    int ApprovalCount,
    int MinApprovals);

/// <summary>A request's full state, for the inbox and for a single-request lookup.</summary>
/// <param name="Id">The request.</param>
/// <param name="PolicyId">The policy that produced it.</param>
/// <param name="Module">The gated action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="SubjectEntityId">The business record being gated.</param>
/// <param name="Amount">The amount at hand, if any.</param>
/// <param name="RequestedBy">Who raised it.</param>
/// <param name="RequestedAt">When, UTC.</param>
/// <param name="Reason">Free text from the requester.</param>
/// <param name="RequiredPermission">The permission a decider must hold.</param>
/// <param name="MinApprovals">How many approvals are required.</param>
/// <param name="ApprovalCount">How many have been recorded.</param>
/// <param name="RejectionCount">How many rejections have been recorded.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="DecidedAt">When it left <c>Pending</c>, if it has.</param>
public sealed record ApprovalRequestSummary(
    Guid Id,
    Guid PolicyId,
    string Module,
    string EntityType,
    string Action,
    Guid SubjectEntityId,
    Money? Amount,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    string? Reason,
    string RequiredPermission,
    int MinApprovals,
    int ApprovalCount,
    int RejectionCount,
    ApprovalRequestStatus Status,
    DateTimeOffset? DecidedAt);

/// <summary>
/// The single choke point for gating an action on approval (ADR-019, <c>CLAUDE.md</c> §7 rule 13).
/// </summary>
/// <remarks>
/// <para>
/// A module calls <see cref="EvaluateAsync"/> unconditionally on every occurrence of the action it
/// wants gated. Whether anything actually needs approving — whether a policy is even configured, and
/// whether the amount at hand reaches its threshold — is this service's business, not the caller's.
/// A caller that inspected a threshold itself, to decide whether to call this service at all, would be
/// the module hand-rolling approval logic that rule 13 exists to prevent.
/// </para>
/// <para>
/// <b>Exactly one implementation is permitted.</b> An architecture test fails the build on a second one,
/// proven by a deliberate violation before being committed.
/// </para>
/// </remarks>
public interface IApprovalService
{
    /// <summary>
    /// Evaluates a gated action against its configured policy, if there is one.
    /// </summary>
    /// <param name="context">What is being gated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <see cref="ApprovalOutcome.MayProceed"/> when there is no policy for this key, or the amount is
    /// below its threshold. Otherwise a pending request has been raised and its id is on the outcome.
    /// </returns>
    Task<ApprovalOutcome> EvaluateAsync(ApprovalContext context, CancellationToken cancellationToken = default);

    /// <summary>Records a decision on a pending request.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="outcome">What was decided.</param>
    /// <param name="comment">Free text from the decider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ApprovalRequestNotFoundException">No such request in this tenant.</exception>
    /// <exception cref="ApproverLacksRequiredPermissionException">
    /// The deciding principal does not hold the policy's required permission in the request's store.
    /// </exception>
    /// <exception cref="ApproverAlreadyDecidedException">This principal already decided this request.</exception>
    Task<ApprovalDecisionResult> DecideAsync(
        Guid requestId,
        ApprovalDecisionOutcome outcome,
        string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws a pending request.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ApprovalRequestNotFoundException">No such request in this tenant.</exception>
    Task CancelAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Finds a request by id.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApprovalRequestSummary?> FindAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Every pending request, across every module — the unified approval inbox.</summary>
    /// <param name="storeId">Restrict to one store, or <c>null</c> for the whole tenant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApprovalRequestSummary>> ListPendingAsync(
        Guid? storeId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A policy's configuration, for the admin screen that manages them.</summary>
/// <param name="Id">The policy.</param>
/// <param name="Module">The gated action's owning module.</param>
/// <param name="EntityType">What the action is about.</param>
/// <param name="Action">What is being done.</param>
/// <param name="ThresholdAmount">The gating amount, or <c>null</c> to gate every occurrence.</param>
/// <param name="RequiredPermission">The permission a decider must hold.</param>
/// <param name="MinApprovals">How many approvals are required.</param>
/// <param name="AllowSelfApproval">Whether the requester may decide their own request.</param>
/// <param name="IsActive">Whether this policy is currently in force.</param>
public sealed record ApprovalPolicySummary(
    Guid Id,
    string Module,
    string EntityType,
    string Action,
    Money? ThresholdAmount,
    string RequiredPermission,
    int MinApprovals,
    bool AllowSelfApproval,
    bool IsActive);

/// <summary>Reads and writes approval policies.</summary>
public interface IApprovalPolicyRepository
{
    /// <summary>Finds the active policy for a gated action, if one is configured.</summary>
    /// <param name="module">The action's owning module.</param>
    /// <param name="entityType">What the action is about.</param>
    /// <param name="action">What is being done.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApprovalPolicy?> FindActiveAsync(
        string module,
        string entityType,
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a policy by id.</summary>
    /// <param name="policyId">The policy.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApprovalPolicy?> FindAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>Every policy, active or not, for the admin screen that configures them.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApprovalPolicy>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a policy.</summary>
    /// <param name="policy">The policy to add.</param>
    void Add(ApprovalPolicy policy);
}

/// <summary>Reads and writes approval requests.</summary>
public interface IApprovalRequestRepository
{
    /// <summary>Finds a request by id.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApprovalRequest?> FindAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Every pending request, across every module — the unified inbox.</summary>
    /// <param name="storeId">Restrict to one store, or <c>null</c> for the whole tenant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        Guid? storeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a request.</summary>
    /// <param name="request">The request to add.</param>
    void Add(ApprovalRequest request);
}

/// <summary>Reads and writes the append-only decision history.</summary>
public interface IApprovalDecisionRepository
{
    /// <summary>Every decision recorded against a request, oldest first.</summary>
    /// <param name="approvalRequestId">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApprovalDecisionEntry>> ListForRequestAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this principal has already decided this request.
    /// </summary>
    /// <param name="approvalRequestId">The request.</param>
    /// <param name="decidedBy">The principal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The guard that keeps an <c>N</c>-of-<c>M</c> policy honest: without it, one person voting twice
    /// would satisfy a two-approver requirement alone, which is not a second approval at all.
    /// </remarks>
    Task<bool> HasDecidedAsync(
        Guid approvalRequestId,
        string decidedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a decision.</summary>
    /// <param name="entry">The decision to add.</param>
    void Add(ApprovalDecisionEntry entry);
}

/// <summary>Raised when a request id does not resolve within the current tenant.</summary>
/// <param name="requestId">The request that was asked for.</param>
public sealed class ApprovalRequestNotFoundException(Guid requestId)
    : DomainException(
        "WORKFLOW_APPROVAL_REQUEST_NOT_FOUND",
        $"No pending approval request {requestId} was found.",
        DomainProblemKind.NotFound);

/// <summary>Raised when a decider does not hold the policy's required permission in the request's store.</summary>
/// <param name="requestId">The request.</param>
/// <param name="requiredPermission">The permission that was missing.</param>
public sealed class ApproverLacksRequiredPermissionException(Guid requestId, string requiredPermission)
    : DomainException(
        "WORKFLOW_APPROVER_LACKS_PERMISSION",
        $"Deciding approval request {requestId} requires '{requiredPermission}', which this user does "
        + "not hold in the relevant store.",
        DomainProblemKind.Forbidden);

/// <summary>Raised when the same principal tries to decide a request they already decided.</summary>
/// <param name="requestId">The request.</param>
public sealed class ApproverAlreadyDecidedException(Guid requestId)
    : DomainException(
        "WORKFLOW_APPROVER_ALREADY_DECIDED",
        $"This user has already recorded a decision on approval request {requestId}.",
        DomainProblemKind.Conflict);

// ---------------------------------------------------------------------------------------------
// Notifications
// ---------------------------------------------------------------------------------------------

/// <summary>What to tell somebody, and where.</summary>
/// <param name="RecipientUserId">Who it is for.</param>
/// <param name="Category">A namespaced category.</param>
/// <param name="Title">A short summary.</param>
/// <param name="Body">The full message.</param>
/// <param name="Severity">How urgently it should be shown.</param>
/// <param name="Channels">
/// Which channels to raise this on. One <see cref="Domain.Workflow.Notification"/> row is created per
/// channel, so a recipient reading their in-app inbox and an email both accepted at once are two
/// self-contained facts rather than one row trying to describe both.
/// </param>
/// <param name="RelatedModule">The module raising the notification.</param>
/// <param name="RelatedEntityType">What kind of thing it relates to.</param>
/// <param name="RelatedEntityId">The id of the thing it relates to.</param>
/// <param name="ActionUrl">Where to send a user who acts on it.</param>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    string Category,
    string Title,
    string Body,
    NotificationSeverity Severity,
    IReadOnlyList<NotificationChannel> Channels,
    string? RelatedModule = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? ActionUrl = null);

/// <summary>What sending on a channel produced.</summary>
/// <param name="Success">Whether the channel accepted it.</param>
/// <param name="Error">Why it did not, when it did not.</param>
public readonly record struct NotificationDeliveryOutcome(bool Success, string? Error = null)
{
    /// <summary>The channel accepted it.</summary>
    public static NotificationDeliveryOutcome Delivered { get; } = new(true);

    /// <summary>The channel could not send it.</summary>
    /// <param name="error">Why.</param>
    public static NotificationDeliveryOutcome Failed(string error) => new(false, error);
}

/// <summary>One way of delivering a notification.</summary>
/// <remarks>
/// <c>InApp</c> is real and persisted — it is the one channel this product ships without a vendor
/// integration. <c>Email</c> and <c>Push</c> are working log implementations pending a real provider
/// (Deferred, <c>docs/PROGRESS.md</c> §3), following the pattern Stage 04b's <c>InProcessControlPlane</c>
/// set: a channel that does something real and observable rather than a silent no-op.
/// </remarks>
public interface INotificationChannel
{
    /// <summary>Which channel this is.</summary>
    NotificationChannel Kind { get; }

    /// <summary>Delivers a notification on this channel.</summary>
    /// <param name="notification">The notification to deliver.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<NotificationDeliveryOutcome> SendAsync(
        Domain.Workflow.Notification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>Raises a notification across one or more channels and records what happened.</summary>
public interface INotificationDispatcher
{
    /// <summary>Creates and dispatches a notification on every requested channel.</summary>
    /// <param name="request">What to tell somebody, and where.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The id of each notification row created, one per channel.</returns>
    Task<IReadOnlyList<Guid>> NotifyAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A notification, for a recipient's own list.</summary>
/// <param name="Id">The notification.</param>
/// <param name="Category">A namespaced category.</param>
/// <param name="Title">A short summary.</param>
/// <param name="Body">The full message.</param>
/// <param name="Severity">How urgently it should be shown.</param>
/// <param name="Channel">Which channel this row travelled on.</param>
/// <param name="RelatedModule">The module that raised it.</param>
/// <param name="RelatedEntityType">What kind of thing it relates to.</param>
/// <param name="RelatedEntityId">The id of the thing it relates to.</param>
/// <param name="ActionUrl">Where to send a user who acts on it.</param>
/// <param name="DeliveryStatus">Whether the channel accepted it.</param>
/// <param name="IsRead">Whether the recipient has read it.</param>
/// <param name="CreatedAt">When it was raised, UTC.</param>
public sealed record NotificationSummary(
    Guid Id,
    string Category,
    string Title,
    string Body,
    NotificationSeverity Severity,
    NotificationChannel Channel,
    string? RelatedModule,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? ActionUrl,
    NotificationDeliveryStatus DeliveryStatus,
    bool IsRead,
    DateTimeOffset CreatedAt);

/// <summary>Reads and writes notifications.</summary>
public interface INotificationRepository
{
    /// <summary>Finds a notification by id.</summary>
    /// <param name="notificationId">The notification.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Domain.Workflow.Notification?> FindAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>A recipient's own notifications, newest first.</summary>
    /// <param name="recipientUserId">The recipient.</param>
    /// <param name="unreadOnly">Restrict to unread, in-app notifications.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Domain.Workflow.Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>How many unread in-app notifications a recipient has — the inbox badge count.</summary>
    /// <param name="recipientUserId">The recipient.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    /// <summary>Adds a notification.</summary>
    /// <param name="notification">The notification to add.</param>
    void Add(Domain.Workflow.Notification notification);
}

/// <summary>Raised when a notification id does not resolve within the current tenant, or is not this user's.</summary>
/// <param name="notificationId">The notification that was asked for.</param>
public sealed class NotificationNotFoundException(Guid notificationId)
    : DomainException(
        "WORKFLOW_NOTIFICATION_NOT_FOUND",
        $"No notification {notificationId} was found.",
        DomainProblemKind.NotFound);

// ---------------------------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------------------------

/// <summary>Content-addressed object storage for document bytes.</summary>
/// <remarks>
/// The same shape as <c>IBackupVault</c> (Stage 04) and for the same reason: streams throughout,
/// because a generated statement or an imported price list is not something to hold whole in memory on
/// a back-office PC.
/// </remarks>
public interface IDocumentBlobStore
{
    /// <summary>Writes an object, overwriting any object already at that key.</summary>
    /// <param name="storageKey">The key.</param>
    /// <param name="content">The bytes to write. Read to the end.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many bytes were written.</returns>
    Task<long> PutAsync(string storageKey, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens an object for reading.</summary>
    /// <param name="storageKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The object's bytes. The caller disposes.</returns>
    /// <exception cref="DocumentBlobNotFoundException">No object at that key.</exception>
    Task<Stream> GetAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes an object. Used by retention only — a live <see cref="DocumentVersion"/> row must never dangle.</summary>
    /// <param name="storageKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>What to render, and from what.</summary>
/// <param name="TemplateKind">Which template — a module's own vocabulary, mirroring <see cref="Document.Kind"/>.</param>
/// <param name="Model">The values the template fills in, as strings — deliberately simple until a real template engine is wired.</param>
/// <param name="ContentType">The MIME type the rendered document should carry.</param>
public sealed record DocumentRenderRequest(
    string TemplateKind,
    IReadOnlyDictionary<string, string> Model,
    string ContentType);

/// <summary>A rendered document, ready to store.</summary>
/// <param name="Content">The bytes. The caller disposes.</param>
/// <param name="ContentType">The MIME type actually produced.</param>
public sealed record RenderedDocument(Stream Content, string ContentType);

/// <summary>
/// Turns a template and a model into document bytes.
/// </summary>
/// <remarks>
/// The working implementation shipped with this stage is a plain-text passthrough. Real templated
/// rendering — QuestPDF receipts, statements, reports — belongs to the stage that first needs one
/// (<c>CLAUDE.md</c> §6: <c>VumaRetail.Reporting</c>, built alongside Stage 09). This interface is the
/// seam that stage plugs into without anything upstream changing.
/// </remarks>
public interface IDocumentRenderer
{
    /// <summary>Renders a document.</summary>
    /// <param name="request">What to render.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<RenderedDocument> RenderAsync(DocumentRenderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What was attached or generated, for the caller to show back.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="VersionNumber">The version this call produced.</param>
/// <param name="ContentHash">The SHA-256 of the bytes, as lower-case hex.</param>
/// <param name="SizeBytes">The size of the bytes.</param>
public sealed record DocumentVersionSummary(Guid DocumentId, int VersionNumber, string ContentHash, long SizeBytes);

/// <summary>A document's metadata, without its bytes.</summary>
/// <param name="Id">The document.</param>
/// <param name="Module">The owning module.</param>
/// <param name="EntityType">What kind of business record this belongs to.</param>
/// <param name="EntityId">The business record's id.</param>
/// <param name="Kind">What sort of document this is.</param>
/// <param name="Title">A human title.</param>
/// <param name="ContentType">The MIME type of the current version.</param>
/// <param name="CurrentVersionNumber">The latest version number.</param>
/// <param name="IsGenerated">Whether the current version was rendered rather than uploaded.</param>
public sealed record DocumentSummary(
    Guid Id,
    string Module,
    string EntityType,
    Guid EntityId,
    string Kind,
    string Title,
    string ContentType,
    int CurrentVersionNumber,
    bool IsGenerated);

/// <summary>Generation, attachment and versioning primitives for module document needs.</summary>
/// <remarks>
/// Infrastructure-light by design (this stage's brief): storage is a key-value blob store and
/// rendering is a passthrough. What is real is the versioning discipline — nothing is overwritten, and
/// every version's hash is checked on the way back out.
/// </remarks>
public interface IDocumentService
{
    /// <summary>Attaches a new document, uploaded rather than generated.</summary>
    /// <param name="module">The owning module.</param>
    /// <param name="entityType">What kind of business record this belongs to.</param>
    /// <param name="entityId">The business record's id.</param>
    /// <param name="kind">What sort of document this is.</param>
    /// <param name="title">A human title.</param>
    /// <param name="contentType">The MIME type of the bytes.</param>
    /// <param name="content">The bytes. Read to the end.</param>
    /// <param name="storeId">The store, where the business record has one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<DocumentVersionSummary> AttachAsync(
        string module,
        string entityType,
        Guid entityId,
        string kind,
        string title,
        string contentType,
        Stream content,
        Guid? storeId,
        CancellationToken cancellationToken = default);

    /// <summary>Attaches a new version to an existing document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="contentType">The MIME type of the bytes.</param>
    /// <param name="content">The bytes. Read to the end.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="DocumentNotFoundException">No such document in this tenant.</exception>
    Task<DocumentVersionSummary> AttachVersionAsync(
        Guid documentId,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Renders and stores a new document.</summary>
    /// <param name="module">The owning module.</param>
    /// <param name="entityType">What kind of business record this belongs to.</param>
    /// <param name="entityId">The business record's id.</param>
    /// <param name="kind">What sort of document this is.</param>
    /// <param name="title">A human title.</param>
    /// <param name="renderRequest">What to render.</param>
    /// <param name="storeId">The store, where the business record has one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<DocumentVersionSummary> GenerateAsync(
        string module,
        string entityType,
        Guid entityId,
        string kind,
        string title,
        DocumentRenderRequest renderRequest,
        Guid? storeId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a specific version's bytes, or the current one.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="versionNumber">Which version, or <c>null</c> for the current one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="DocumentNotFoundException">No such document, or no such version, in this tenant.</exception>
    Task<Stream> OpenVersionAsync(
        Guid documentId,
        int? versionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a document's metadata.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<DocumentSummary?> FindAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Every document attached to a business record.</summary>
    /// <param name="module">The owning module.</param>
    /// <param name="entityType">What kind of business record.</param>
    /// <param name="entityId">The business record's id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<DocumentSummary>> ListForEntityAsync(
        string module,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes document metadata.</summary>
public interface IDocumentRepository
{
    /// <summary>Finds a document by id.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Document?> FindAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Every document attached to a business record.</summary>
    /// <param name="module">The owning module.</param>
    /// <param name="entityType">What kind of business record.</param>
    /// <param name="entityId">The business record's id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Document>> ListForEntityAsync(
        string module,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a document.</summary>
    /// <param name="document">The document to add.</param>
    void Add(Document document);
}

/// <summary>Reads and writes document versions.</summary>
public interface IDocumentVersionRepository
{
    /// <summary>Finds one version of a document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="versionNumber">Which version.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<DocumentVersion?> FindAsync(
        Guid documentId,
        int versionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Every version of a document, oldest first.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<DocumentVersion>> ListForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a version.</summary>
    /// <param name="version">The version to add.</param>
    void Add(DocumentVersion version);
}

/// <summary>Raised when a document id does not resolve within the current tenant, or a version does not exist.</summary>
/// <param name="message">What was not found.</param>
public sealed class DocumentNotFoundException(string message)
    : DomainException("WORKFLOW_DOCUMENT_NOT_FOUND", message, DomainProblemKind.NotFound);

/// <summary>Raised when a blob store has no object at the key it was asked for.</summary>
/// <param name="storageKey">The key.</param>
public sealed class DocumentBlobNotFoundException(string storageKey)
    : DomainException(
        "WORKFLOW_DOCUMENT_BLOB_NOT_FOUND",
        $"No document content was found at storage key '{storageKey}'.",
        DomainProblemKind.NotFound);

/// <summary>Raised when a stored document's bytes no longer match the hash recorded for them.</summary>
/// <param name="documentId">The document.</param>
/// <param name="versionNumber">The version.</param>
public sealed class DocumentContentTamperedException(Guid documentId, int versionNumber)
    : DomainException(
        "WORKFLOW_DOCUMENT_CONTENT_TAMPERED",
        $"Document {documentId} version {versionNumber} does not match its recorded content hash.",
        DomainProblemKind.Conflict);
