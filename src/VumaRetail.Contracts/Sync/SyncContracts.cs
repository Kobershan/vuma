namespace VumaRetail.Contracts.Sync;

/// <summary>
/// One replicated operation as it crosses the wire.
/// </summary>
/// <remarks>
/// The stamp is a string rather than a structured object on purpose. It is opaque to every client
/// but the sync engine, it sorts correctly as text, and a client that never parses it can never
/// depend on its internal shape — which leaves ADR-007 free to change the encoding without a new API
/// version.
/// </remarks>
/// <param name="OperationId">The sender's idempotency key. The receiver deduplicates on this.</param>
/// <param name="EntityType">The CLR type name of the target entity, for example <c>Store</c>.</param>
/// <param name="EntityId">The primary key of the target row.</param>
/// <param name="Operation"><c>Upsert</c> or <c>Delete</c>.</param>
/// <param name="Stamp">The sender's hybrid logical clock stamp, in its sortable string form.</param>
/// <param name="Payload">The row's state after the change, as a JSON object.</param>
/// <param name="OccurredAt">When the change happened, UTC.</param>
public sealed record SyncOperationDto(
    Guid OperationId,
    string EntityType,
    Guid EntityId,
    string Operation,
    string Stamp,
    string Payload,
    DateTimeOffset OccurredAt)
{
    /// <summary>The owning company database.</summary>
    public Guid? CompanyId { get; init; }
}

/// <summary>
/// A batch of operations from one node, for one tenant.
/// </summary>
/// <remarks>
/// One tenant per batch, not one batch per upload. The receiver re-scopes the tenant context once per
/// batch, and a mixed batch would mean re-scoping per operation — which is both slower and the sort
/// of code where one missed re-scope writes a store's sales into another tenant.
/// </remarks>
/// <param name="SourceNode">The sending node — <c>store:{id}</c>, <c>terminal:{id}</c>, <c>cloud</c>.</param>
/// <param name="SourceKind">The sending tier: <c>Terminal</c>, <c>Store</c> or <c>Cloud</c>.</param>
/// <param name="TenantId">The tenant every operation belongs to.</param>
/// <param name="StoreId">The store, where the batch is store-scoped.</param>
/// <param name="Operations">The operations, oldest stamp first.</param>
public sealed record SyncBatchRequest(
    string SourceNode,
    string SourceKind,
    Guid TenantId,
    Guid? StoreId,
    IReadOnlyList<SyncOperationDto> Operations)
{
    /// <summary>The company database represented by this batch.</summary>
    public Guid? CompanyId { get; init; }
}

/// <summary>What the receiver did with one operation.</summary>
/// <param name="OperationId">The operation.</param>
/// <param name="Outcome">
/// <c>Applied</c>, <c>Duplicate</c>, <c>Conflicted</c> or <c>Rejected</c>. Only <c>Rejected</c> means
/// the sender should look at anything — a duplicate is the normal result of the retry that an
/// at-least-once transport is built on.
/// </param>
/// <param name="ConflictId">The conflict entry raised, where the outcome was <c>Conflicted</c>.</param>
/// <param name="Detail">Why, where the outcome needs explaining.</param>
public sealed record SyncOperationOutcomeDto(
    Guid OperationId,
    string Outcome,
    Guid? ConflictId = null,
    string? Detail = null);

/// <summary>The receiver's answer to a batch.</summary>
/// <param name="ReceiverNode">The node that processed it.</param>
/// <param name="AcknowledgedStamp">The highest stamp now durably processed. The sender's cursor moves to this.</param>
/// <param name="Results">One result per operation, in the order they were sent.</param>
public sealed record SyncBatchResponse(
    string ReceiverNode,
    string AcknowledgedStamp,
    IReadOnlyList<SyncOperationOutcomeDto> Results);

/// <summary>How one peer relationship is doing.</summary>
/// <param name="PeerNode">The peer.</param>
/// <param name="Direction"><c>Outbound</c> or <c>Inbound</c>.</param>
/// <param name="AcknowledgedStamp">The highest stamp confirmed in this direction.</param>
/// <param name="AcknowledgedCount">How many operations have been confirmed, ever.</param>
/// <param name="LastContactAt">When this node and the peer last exchanged anything, UTC.</param>
public sealed record SyncCursorDto(
    string PeerNode,
    string Direction,
    string AcknowledgedStamp,
    long AcknowledgedCount,
    DateTimeOffset? LastContactAt);

/// <summary>
/// This node's replication health.
/// </summary>
/// <remarks>
/// Read by the back office and, from Stage 30, the Android app. Nothing here restricts anybody:
/// ADR-028's fourth rule is that a vendor-side outage never restricts a tenant, and "we have not
/// heard from the cloud in a while" is precisely the signal a nervous implementation would wire to an
/// enforcement ladder.
/// </remarks>
/// <param name="NodeId">This node.</param>
/// <param name="NodeKind">This node's tier.</param>
/// <param name="CurrentStamp">This node's current hybrid logical clock reading.</param>
/// <param name="OutstandingOperations">How many outbox rows are waiting or have failed.</param>
/// <param name="OpenConflicts">How many conflicts are waiting for a person.</param>
/// <param name="Cursors">One entry per peer per direction.</param>
public sealed record SyncStatusResponse(
    string NodeId,
    string NodeKind,
    string CurrentStamp,
    int OutstandingOperations,
    int OpenConflicts,
    IReadOnlyList<SyncCursorDto> Cursors);

/// <summary>A divergence waiting for a person, with both versions kept.</summary>
/// <param name="Id">The conflict entry.</param>
/// <param name="EntityType">The CLR type name of the diverged entity.</param>
/// <param name="EntityId">The primary key of the diverged row.</param>
/// <param name="SourceNode">The node the remote version came from.</param>
/// <param name="LocalVersion">This node's version, as JSON.</param>
/// <param name="RemoteVersion">The peer's version, as JSON.</param>
/// <param name="LocalStamp">This node's stamp for the row.</param>
/// <param name="RemoteStamp">The peer's stamp for the row.</param>
/// <param name="DetectedAt">When the divergence was detected, UTC.</param>
/// <param name="Resolution"><c>Unresolved</c>, <c>KeptLocal</c> or <c>TookRemote</c>.</param>
/// <param name="ResolvedAt">When a person settled it, UTC.</param>
/// <param name="ResolvedBy">Who settled it.</param>
/// <param name="ResolutionNote">Why.</param>
public sealed record ConflictResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string SourceNode,
    string LocalVersion,
    string RemoteVersion,
    string LocalStamp,
    string RemoteStamp,
    DateTimeOffset DetectedAt,
    string Resolution,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? ResolutionNote);

/// <summary>Settles a conflict.</summary>
/// <param name="Resolution"><c>KeptLocal</c> or <c>TookRemote</c>.</param>
/// <param name="Note">Why. Required — this is a person overruling the system about a business record.</param>
public sealed record ResolveConflictRequest(string Resolution, string Note);
