using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Application.Abstractions.Sync;

/// <summary>
/// The only source of hybrid logical clock stamps (ADR-007).
/// </summary>
/// <remarks>
/// Separate from <see cref="IClock"/> and not derived from it. <see cref="IClock"/> answers "when did
/// this happen", which is a business fact that goes on the row. This answers "where does this sit in
/// the order of events across nodes", which is a replication mechanism. Conflating them would mean a
/// clock correction reordering history, and a test that needs to control ordering having to control
/// time.
/// </remarks>
public interface IHybridClock
{
    /// <summary>Issues the next stamp. Strictly greater than every stamp this node has issued.</summary>
    HlcStamp Next();

    /// <summary>
    /// Folds a stamp received from a peer into this node's clock, then issues the next one.
    /// </summary>
    /// <param name="remote">The peer's stamp.</param>
    /// <returns>A stamp strictly greater than both <paramref name="remote"/> and anything issued here.</returns>
    /// <remarks>
    /// This is the "hybrid" half. Receiving an event from the future has to move this node's logical
    /// clock past it, or the node would keep issuing stamps that order <em>before</em> events it has
    /// already seen — and a last-writer-wins rule evaluated on those stamps would undo the peer's
    /// change every time.
    /// </remarks>
    HlcStamp Observe(HlcStamp remote);

    /// <summary>The highest stamp this node has issued or observed, without issuing a new one.</summary>
    HlcStamp Current { get; }
}

/// <summary>Who this node is in the terminal → store → cloud chain.</summary>
public interface INodeIdentity
{
    /// <summary>
    /// The node id, at most <see cref="HlcStamp.NodeIdMaxLength"/> characters — <c>store:{id}</c>,
    /// <c>cloud</c>, <c>terminal:{id}</c>. Stable for the life of the installation.
    /// </summary>
    string NodeId { get; }

    /// <summary>What tier this node is. Decides <c>StoreWins</c> and <c>CloudWins</c>.</summary>
    NodeKind Kind { get; }
}

/// <summary>How an entity replicates, resolved from its <c>[Replicated]</c> declaration.</summary>
/// <param name="Scope">Which tiers the entity replicates between.</param>
/// <param name="ConflictPolicy">How a divergence is settled.</param>
public readonly record struct ReplicationDescriptor(ReplicationScope Scope, ConflictPolicy ConflictPolicy)
{
    /// <summary>True when changes to this entity never leave the node that wrote them.</summary>
    public bool IsNodeLocal => Scope == ReplicationScope.NodeLocal;
}

/// <summary>
/// The <c>[Replicated]</c> declarations, read once per type and cached.
/// </summary>
/// <remarks>
/// A cache rather than a convenience. The outbox behaviour asks this for every changed row, and a
/// till on a busy Saturday changes a lot of rows — reflecting per row would put
/// <c>GetCustomAttribute</c> in the hot path of every sale.
/// </remarks>
public interface IReplicationRegistry
{
    /// <summary>How a CLR type replicates, or <c>null</c> if it carries no declaration.</summary>
    /// <param name="entityType">The entity's CLR type.</param>
    ReplicationDescriptor? Find(Type entityType);

    /// <summary>How a CLR type replicates, by name, for an operation arriving off the wire.</summary>
    /// <param name="entityTypeName">The entity's CLR type name, as sent.</param>
    ReplicationDescriptor? FindByName(string entityTypeName);

    /// <summary>Resolves a CLR type from the name an operation was sent under.</summary>
    /// <param name="entityTypeName">The entity's CLR type name, as sent.</param>
    /// <returns>The type, or <c>null</c> if this node does not know it.</returns>
    /// <remarks>
    /// Resolved from a closed set built at startup, never from <c>Type.GetType</c>. A receiver that
    /// materialised whatever type name a peer sent would be a deserialisation gadget with an HTTP
    /// endpoint in front of it.
    /// </remarks>
    Type? ResolveEntityType(string entityTypeName);
}

/// <summary>A row as the replication layer sees it: its JSON state and its ordering stamp.</summary>
/// <param name="Payload">The row's current state, as a JSON object.</param>
/// <param name="Stamp">The row's current ordering stamp.</param>
/// <param name="IsDeleted">True when the local row is already soft-deleted.</param>
public readonly record struct ReplicaRow(string Payload, HlcStamp Stamp, bool IsDeleted);

/// <summary>
/// Reads and writes replicated rows generically, by CLR type and id.
/// </summary>
/// <remarks>
/// The one place in the system that touches an entity without knowing what it is. It is a port rather
/// than code in the receiver because doing it means EF metadata, change tracking and type conversion
/// — all of which belong to Infrastructure — while <em>deciding</em> what to apply is protocol and
/// belongs to <c>VumaRetail.Sync</c>.
/// </remarks>
public interface IReplicaWriter
{
    /// <summary>Reads a row's replication state, or <c>null</c> if this node does not have it.</summary>
    /// <param name="entityType">The entity's CLR type.</param>
    /// <param name="entityId">The row's primary key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Soft-deleted rows are included. A node that could not see its own tombstone would re-create a
    /// row every time an older upsert for it was replayed.
    /// </remarks>
    Task<ReplicaRow?> FindAsync(Type entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an operation to a row, creating it if this node does not have it.
    /// </summary>
    /// <param name="entityType">The entity's CLR type.</param>
    /// <param name="operation">The operation to apply.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Mutates tracked entities and returns; the command pipeline commits (ADR-044).
    /// </remarks>
    Task ApplyAsync(Type entityType, SyncOperation operation, CancellationToken cancellationToken = default);

    /// <summary>Serialises a tracked entity to the JSON form the wire and the outbox both use.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The row's state as a JSON object.</returns>
    string Serialise(Entity entity);
}

/// <summary>
/// Records which rows this node changed because a peer told it to, rather than of its own accord.
/// </summary>
/// <remarks>
/// <para>
/// Loop prevention, and it has to exist. Without it, the store applies a change from the cloud, the
/// outbox behaviour sees a modified row and captures it, the store sends it back to the cloud, the
/// cloud applies it and captures it, and two machines pass one supplier's phone number back and
/// forth until somebody notices the disk filling.
/// </para>
/// <para>
/// <b>Per row, not per region.</b> The obvious design is a flag that is true while a batch is being
/// applied — and it does not work, because of where the two components sit in the pipeline. The
/// outbox behaviour occupies slot 300 and captures <em>after</em> the handler returns, by which time
/// a <c>using</c> region inside that handler has already closed. A test caught exactly that: the
/// receiver suppressed capture correctly and the outbox captured everything anyway.
/// </para>
/// <para>
/// Recording ids instead is also the more truthful model. What must not be re-captured is a specific
/// set of rows, not everything that happened to be written near them — and when the cloud eventually
/// needs to fan a store's change out to that store's siblings, "which rows came from a peer" is
/// exactly the question it will have to answer.
/// </para>
/// </remarks>
public interface IReplicationScope
{
    /// <summary>The rows written by the current apply region. The outbox skips these.</summary>
    IReadOnlySet<Guid> AppliedRowIds { get; }

    /// <summary>
    /// Opens a region in which rows written through <see cref="IReplicaWriter"/> are recorded as
    /// remote, and clears anything a previous region recorded.
    /// </summary>
    /// <returns>A region that closes when disposed. The recorded ids outlive it by design.</returns>
    IDisposable BeginApply();

    /// <summary>Records that a row was written because a peer asked for it.</summary>
    /// <param name="rowId">The row's primary key.</param>
    void RecordApplied(Guid rowId);
}

/// <summary>The transactional outbox (ADR-006).</summary>
public interface IOutboxRepository
{
    /// <summary>Queues a captured change. Committed by the pipeline, in the change's own transaction.</summary>
    /// <param name="message">The captured change.</param>
    void Add(OutboxMessage message);

    /// <summary>
    /// The next batch due for delivery, oldest stamp first.
    /// </summary>
    /// <param name="batchSize">How many to take.</param>
    /// <param name="dueAt">Rows whose backoff has expired by this instant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<OutboxMessage>> ListDueAsync(
        int batchSize,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default);

    /// <summary>How many rows are waiting or have failed, for the health surface.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountOutstandingAsync(CancellationToken cancellationToken = default);
}

/// <summary>The idempotent inbox (ADR-006).</summary>
public interface IInboxRepository
{
    /// <summary>Records receipt and outcome.</summary>
    /// <param name="message">The receipt record.</param>
    void Add(InboxMessage message);

    /// <summary>
    /// True when this node has already processed <paramref name="operationId"/> from
    /// <paramref name="sourceNode"/>.
    /// </summary>
    /// <param name="sourceNode">The sending node.</param>
    /// <param name="operationId">The sender's idempotency key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The fast path only. The guarantee is the unique index — this check is check-then-act and two
    /// concurrent deliveries of one batch will both pass it. Its job is to avoid doing the work
    /// twice, not to make doing it twice impossible.
    /// </remarks>
    Task<bool> HasProcessedAsync(
        string sourceNode,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Where each peer has got to, per direction.</summary>
public interface ISyncCursorRepository
{
    /// <summary>The cursor for a peer and direction, or <c>null</c> if there has never been contact.</summary>
    /// <param name="peerNode">The peer node id.</param>
    /// <param name="direction">Which way the cursor tracks.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SyncCursor?> FindAsync(
        string peerNode,
        SyncDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>Every cursor this node keeps, for the health surface.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SyncCursor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts tracking a peer.</summary>
    /// <param name="cursor">The new cursor.</param>
    void Add(SyncCursor cursor);
}

/// <summary>The conflict review queue (ADR-007).</summary>
public interface IConflictRepository
{
    /// <summary>Opens a conflict for review.</summary>
    /// <param name="entry">The conflict.</param>
    void Add(ConflictEntry entry);

    /// <summary>Finds a conflict by id within the current tenant.</summary>
    /// <param name="conflictId">The conflict.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ConflictEntry?> FindAsync(Guid conflictId, CancellationToken cancellationToken = default);

    /// <summary>The queue, newest first.</summary>
    /// <param name="openOnly">Only entries still waiting for a person.</param>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ConflictEntry>> ListAsync(
        bool openOnly,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The seam a batch of operations crosses to reach a peer.
/// </summary>
/// <remarks>
/// ADR-022's shape: an interface with a working fake, so the whole protocol is testable without a
/// second host in the room. The store server ships an HTTP implementation pointed at the cloud; the
/// tests ship an in-process one pointed at a receiver.
/// </remarks>
public interface ISyncTransport
{
    /// <summary>Ships a batch and waits for the peer's acknowledgement.</summary>
    /// <param name="batch">The operations to ship.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the peer did with each operation.</returns>
    /// <exception cref="SyncTransportException">The peer could not be reached, or refused the batch.</exception>
    Task<SyncAcknowledgement> SendAsync(SyncBatch batch, CancellationToken cancellationToken = default);

    /// <summary>The peer this transport talks to.</summary>
    string PeerNode { get; }
}

/// <summary>One operation on the wire.</summary>
/// <param name="OperationId">The sender's idempotency key.</param>
/// <param name="EntityType">The CLR type name of the target entity.</param>
/// <param name="EntityId">The primary key of the target row.</param>
/// <param name="Operation">Upsert or soft delete.</param>
/// <param name="Stamp">The sender's ordering stamp.</param>
/// <param name="Payload">The row's state after the change, as JSON.</param>
/// <param name="OccurredAt">When the change happened, UTC.</param>
public sealed record SyncOperation(
    Guid OperationId,
    string EntityType,
    Guid EntityId,
    SyncOperationKind Operation,
    HlcStamp Stamp,
    string Payload,
    DateTimeOffset OccurredAt)
{
    /// <summary>The company database that owns the operation. Null is reserved for legacy batches.</summary>
    public Guid? CompanyId { get; init; }
}

/// <summary>A batch of operations from one node, for one tenant.</summary>
/// <param name="SourceNode">The sending node.</param>
/// <param name="SourceKind">What tier the sender is. Decides <c>StoreWins</c> and <c>CloudWins</c>.</param>
/// <param name="TenantId">The tenant every operation in the batch belongs to.</param>
/// <param name="StoreId">The store, where the batch is store-scoped.</param>
/// <param name="Operations">The operations, oldest stamp first.</param>
public sealed record SyncBatch(
    string SourceNode,
    NodeKind SourceKind,
    Guid TenantId,
    Guid? StoreId,
    IReadOnlyList<SyncOperation> Operations)
{
    /// <summary>The one company database represented by this batch. Registry batches use null.</summary>
    public Guid? CompanyId { get; init; }
}

/// <summary>What the receiver did with one operation.</summary>
/// <param name="OperationId">The operation.</param>
/// <param name="Outcome">What happened to it.</param>
/// <param name="ConflictId">The conflict entry raised, where the outcome was a conflict.</param>
/// <param name="Detail">Why, where the outcome needs explaining.</param>
public sealed record SyncOperationResult(
    Guid OperationId,
    InboxOutcome Outcome,
    Guid? ConflictId = null,
    string? Detail = null);

/// <summary>The receiver's answer to a batch.</summary>
/// <param name="ReceiverNode">The node that processed the batch.</param>
/// <param name="Results">One result per operation, in the order they were sent.</param>
/// <param name="AcknowledgedStamp">The highest stamp the receiver has now durably processed.</param>
public sealed record SyncAcknowledgement(
    string ReceiverNode,
    IReadOnlyList<SyncOperationResult> Results,
    HlcStamp AcknowledgedStamp)
{
    /// <summary>How many operations were applied or already known — that is, are safely at the far end.</summary>
    public int SettledCount => Results.Count(result =>
        result.Outcome is InboxOutcome.Applied or InboxOutcome.Duplicate or InboxOutcome.Conflicted);
}

/// <summary>
/// The peer could not be reached, or refused the batch.
/// </summary>
/// <remarks>
/// An infrastructure failure rather than a <c>DomainException</c>: nothing about the business said
/// no, and the dispatcher's answer is to back off and try again rather than to tell anybody. R1 turns
/// on that distinction — a store whose link is down keeps trading and keeps queueing.
/// </remarks>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The underlying failure, where there is one.</param>
public sealed class SyncTransportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
