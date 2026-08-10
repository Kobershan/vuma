using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sync;

/// <summary>
/// The receiver's record that it has already seen an operation (ADR-006).
/// </summary>
/// <remarks>
/// <para>
/// Exactly-once *effect* over an at-least-once transport is achieved here and nowhere else. The
/// transport may deliver a batch twice — a timeout on the ack, a resumed upload, an operator
/// re-running a catch-up — and the receiver's only defence is a unique index on
/// <c>(tenant_id, source_node, operation_id)</c>.
/// </para>
/// <para>
/// A unique index rather than a lookup, because a lookup is check-then-act and two concurrent
/// deliveries of one batch will both pass the check. The database is the only participant that can
/// settle that race, so it is asked to.
/// </para>
/// <para>
/// <see cref="ReplicationScope.NodeLocal"/>: what one node has seen is that node's business, and
/// replicating it would be replicating the mechanism that stops replication looping.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.AppendOnly)]
public sealed class InboxMessage : Entity
{
    private InboxMessage(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private InboxMessage()
    {
    }

    /// <summary>The operation's idempotency key, as issued by the sender.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>The node that sent it. Half of the dedup key — two nodes may issue the same id.</summary>
    public string SourceNode { get; private set; } = string.Empty;

    /// <summary>The CLR type name of the entity the operation targets.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The primary key of the row the operation targets.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>The sender's ordering stamp for the operation, in its sortable string form.</summary>
    public string OperationStamp { get; private set; } = HlcStamp.MinValue.ToString();

    /// <summary>When this node received it, UTC.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>What the receiver did with it.</summary>
    public InboxOutcome Outcome { get; private set; }

    /// <summary>Why it was rejected, where it was. Null on a successful apply.</summary>
    public string? Detail { get; private set; }

    /// <summary>Records the receipt and the outcome of an operation.</summary>
    /// <param name="tenantId">The tenant the operation belongs to.</param>
    /// <param name="storeId">The store the operation belongs to, if any.</param>
    /// <param name="operationId">The sender's idempotency key.</param>
    /// <param name="sourceNode">The sending node.</param>
    /// <param name="entityType">The CLR type name of the target entity.</param>
    /// <param name="entityId">The primary key of the target row.</param>
    /// <param name="stamp">The sender's ordering stamp.</param>
    /// <param name="receivedAt">When this node received it, UTC.</param>
    /// <param name="outcome">What the receiver did.</param>
    /// <param name="detail">Why, where the outcome needs explaining.</param>
    public static InboxMessage Record(
        Guid tenantId,
        Guid? storeId,
        Guid operationId,
        string sourceNode,
        string entityType,
        Guid entityId,
        HlcStamp stamp,
        DateTimeOffset receivedAt,
        InboxOutcome outcome,
        string? detail = null)
        => new(tenantId, storeId)
        {
            OperationId = operationId,
            SourceNode = sourceNode,
            EntityType = entityType,
            EntityId = entityId,
            OperationStamp = stamp.ToString(),
            ReceivedAt = receivedAt,
            Outcome = outcome,
            Detail = detail,
        };
}
