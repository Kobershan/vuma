using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sync;

/// <summary>
/// One replicated change, queued in the same transaction as the change itself (ADR-006).
/// </summary>
/// <remarks>
/// <para>
/// The transactional outbox exists because there is no way to write a row and send a message
/// atomically across two systems. Writing the message to the *same database* in the *same
/// transaction* turns that distributed problem into a local one: either both landed or neither did,
/// and a separate dispatcher then ships what it finds. A crash at any point loses nothing and
/// duplicates at worst — which is why the receiver deduplicates.
/// </para>
/// <para>
/// Rows are written by <c>OutboxBehaviour</c> from the EF change tracker, never by a module. A module
/// that could write one could also forget to, and a forgotten outbox row is a store that has quietly
/// stopped replicating one table.
/// </para>
/// <para>
/// The entity is itself <see cref="ReplicationScope.NodeLocal"/>. An outbox that replicated would
/// produce an outbox row describing an outbox row, for ever.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.AppendOnly)]
public sealed class OutboxMessage : Entity
{
    private OutboxMessage(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private OutboxMessage()
    {
    }

    /// <summary>
    /// The idempotency key the receiver deduplicates on, with <see cref="SourceNode"/>.
    /// </summary>
    /// <remarks>
    /// A UUID v7 generated at capture time. It identifies the *operation*, not the row: two changes to
    /// one entity are two operations, and re-sending one of them twice is one operation delivered
    /// twice. That distinction is what makes exactly-once effect achievable over an at-least-once
    /// transport.
    /// </remarks>
    public Guid OperationId { get; private set; }

    /// <summary>The node that captured the change — <c>store:{id}</c>, <c>cloud</c>, <c>terminal:{id}</c>.</summary>
    public string SourceNode { get; private set; } = string.Empty;

    /// <summary>The CLR type name of the entity that changed, for example <c>Store</c>.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The primary key of the row that changed.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>Whether the far end should upsert the row or soft-delete it.</summary>
    public SyncOperationKind Operation { get; private set; }

    /// <summary>Which tiers this change is meant to reach, from the entity's <c>[Replicated]</c> declaration.</summary>
    public ReplicationScope Scope { get; private set; }

    /// <summary>How a divergence on this entity is settled, from the same declaration.</summary>
    public ConflictPolicy ConflictPolicy { get; private set; }

    /// <summary>The operation's ordering stamp (ADR-007), stored in its sortable string form.</summary>
    /// <remarks>
    /// The stamp of the <em>change</em>, which is also what the row it describes carries in
    /// <c>Entity.SyncStamp</c>. Named for the operation rather than shadowing the base column,
    /// because the outbox row and the row it describes are stamped at the same instant and confusing
    /// the two is how a replayed operation gets the wrong order.
    /// </remarks>
    public string OperationStamp { get; private set; } = HlcStamp.MinValue.ToString();

    /// <summary>The row's state after the change, as JSON.</summary>
    public string Payload { get; private set; } = "{}";

    /// <summary>When the change happened, UTC, from <c>IClock</c>. Not the stamp — see <see cref="HlcStamp"/>.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Where the row is in its journey.</summary>
    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;

    /// <summary>How many delivery attempts have been made. Drives the backoff.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The earliest the dispatcher should try again, or <c>null</c> to try now.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>When the receiver acknowledged it, or <c>null</c>.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>Why the last attempt failed, truncated. For the operator, not for a retry decision.</summary>
    public string? LastError { get; private set; }

    /// <summary>The operation's ordering stamp, parsed.</summary>
    public HlcStamp OperationHlc => HlcStamp.Parse(OperationStamp);

    /// <summary>Queues a change for replication. Called by the outbox behaviour only.</summary>
    /// <param name="tenantId">The tenant the changed row belongs to.</param>
    /// <param name="storeId">The store the changed row belongs to, if any.</param>
    /// <param name="operationId">The idempotency key.</param>
    /// <param name="sourceNode">The capturing node.</param>
    /// <param name="entityType">The CLR type name of the changed entity.</param>
    /// <param name="entityId">The primary key of the changed row.</param>
    /// <param name="operation">Upsert or delete.</param>
    /// <param name="scope">The entity's replication scope.</param>
    /// <param name="conflictPolicy">The entity's conflict policy.</param>
    /// <param name="stamp">The ordering stamp.</param>
    /// <param name="payload">The row's state after the change, as JSON.</param>
    /// <param name="occurredAt">When the change happened, UTC.</param>
    public static OutboxMessage Capture(
        Guid tenantId,
        Guid? storeId,
        Guid operationId,
        string sourceNode,
        string entityType,
        Guid entityId,
        SyncOperationKind operation,
        ReplicationScope scope,
        ConflictPolicy conflictPolicy,
        HlcStamp stamp,
        string payload,
        DateTimeOffset occurredAt)
        => new(tenantId, storeId)
        {
            OperationId = operationId,
            SourceNode = sourceNode,
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            Scope = scope,
            ConflictPolicy = conflictPolicy,
            OperationStamp = stamp.ToString(),
            Payload = payload,
            OccurredAt = occurredAt,
        };

    /// <summary>Marks the row as handed to the transport.</summary>
    public void MarkInFlight()
    {
        Status = OutboxStatus.InFlight;
        AttemptCount++;
    }

    /// <summary>Marks the row as acknowledged by the receiver.</summary>
    /// <param name="at">When the acknowledgement arrived, UTC.</param>
    public void MarkDispatched(DateTimeOffset at)
    {
        Status = OutboxStatus.Dispatched;
        DispatchedAt = at;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>Records a failed attempt and when to try again.</summary>
    /// <param name="error">What went wrong, for the operator.</param>
    /// <param name="nextAttemptAt">The earliest the dispatcher should retry.</param>
    /// <remarks>
    /// Never terminal. A store on a bad ADSL line is the normal case, not the exception, and a row
    /// that gave up would be a sale the cloud never hears about — so the backoff caps and the row
    /// stays retryable for ever. What is bounded is the *rate*, not the number of attempts.
    /// </remarks>
    public void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(error);

        Status = OutboxStatus.Failed;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>The widest an error message is stored. Matches the column.</summary>
    public const int MaxErrorLength = 1024;
}
