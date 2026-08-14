using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;

namespace VumaRetail.Infrastructure.Sync;

/// <summary>
/// Captures every replicated change into the outbox, inside the transaction that produced it (ADR-006).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline slot <see cref="PipelineOrder.Outbox"/> (300), which sits <em>inside</em>
/// <see cref="PipelineOrder.Transaction"/> (200). Stage 03 reserved the numbers for exactly this: the
/// behaviour runs after the handler and before the commit, so the outbox rows and the changes they
/// describe are one atomic write. A crash between the two would leave a store quietly diverged from
/// the cloud with nothing in any log to say so, which is the failure ADR-006 exists to make
/// impossible.
/// </para>
/// <para>
/// <b>Modules never write outbox rows.</b> Everything here is derived from the EF change tracker and
/// the entity's own <c>[Replicated]</c> declaration. A module that could opt in could also forget to,
/// and a forgotten capture is a table that silently stopped replicating — discovered months later,
/// when somebody asks why head office's stock figures are wrong.
/// </para>
/// <para>
/// Queries are skipped without touching the change tracker. A report must not pay for replication,
/// and enumerating the tracked entities of a context that has just materialised ten thousand rows is
/// not free.
/// </para>
/// </remarks>
/// <param name="context">The database context whose change tracker is read.</param>
/// <param name="registry">The <c>[Replicated]</c> declarations.</param>
/// <param name="replicas">Serialises a row to the payload form the wire uses.</param>
/// <param name="hybridClock">Issues the ordering stamp for each captured change.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">Wall-clock time, for <c>occurred_at</c>.</param>
/// <param name="scope">Which rows this node wrote because a peer asked it to.</param>
/// <param name="stamper">
/// Applies the audit stamp before the payload is serialised. Without this the payload would carry
/// the blank <c>created_by</c> and year-0001 <c>created_at</c> the row has before
/// <c>AuditInterceptor</c> runs, and every other tier would store those blanks for ever — see
/// <see cref="AuditStamper"/>.
/// </param>
public sealed class OutboxBehaviour(
    VumaRetailDbContext context,
    IReplicationRegistry registry,
    IReplicaWriter replicas,
    IHybridClock hybridClock,
    INodeIdentity node,
    IClock clock,
    IReplicationScope scope,
    AuditStamper stamper) : IPipelineBehaviour
{
    /// <inheritdoc />
    public int Order => PipelineOrder.Outbox;

    /// <inheritdoc />
    public async Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(next);

        TResult result = await next(cancellationToken).ConfigureAwait(false);

        if (envelope.Kind != MessageKind.Command)
        {
            return result;
        }

        Capture();

        return result;
    }

    private void Capture()
    {
        DateTimeOffset now = clock.UtcNow;

        // Stamp before serialising. AuditInterceptor runs at SaveChanges, which is *after* this
        // behaviour, so a payload built first would carry created_by = "" and created_at =
        // 0001-01-01 — and every other tier would store those blanks permanently, because
        // created_at/created_by are written once and never again. Stamping is idempotent per save,
        // so the interceptor stamping again costs nothing.
        //
        // It also rewrites a hard delete into a soft delete, which is what lets Classify below and
        // the interceptor agree about what happened to a row.
        stamper.Stamp(context);

        // Materialised before anything is added, because adding outbox rows mutates the change
        // tracker and iterating it while it changes is an InvalidOperationException on the first
        // command that writes two rows.
        List<EntityEntry<Entity>> changed = [.. context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)];

        List<OutboxMessage> captured = [];

        IReadOnlySet<Guid> appliedFromAPeer = scope.AppliedRowIds;

        foreach (EntityEntry<Entity> entry in changed)
        {
            if (registry.Find(entry.Metadata.ClrType) is not { } descriptor || descriptor.IsNodeLocal)
            {
                continue;
            }

            // Loop prevention. This row was written because a peer asked for it, so capturing it
            // would send it straight back where it came from — and the peer would apply it, capture
            // it, and return it, for ever.
            if (appliedFromAPeer.Contains(entry.Entity.Id))
            {
                continue;
            }

            HlcStamp stamp = hybridClock.Next();

            // Stamped on the row as well as on the operation. The row's stamp is what the receiving
            // node compares against next time (ADR-007); without it every conflict policy that
            // depends on order has nothing to order by.
            entry.Entity.MarkSyncStamp(stamp);
            entry.Entity.MarkSyncState(SyncState.Pending);

            captured.Add(OutboxMessage.Capture(
                entry.Entity.TenantId,
                entry.Entity.StoreId,
                UuidV7.NewGuid(),
                node.NodeId,
                entry.Metadata.ClrType.Name,
                entry.Entity.Id,
                Classify(entry),
                descriptor.Scope,
                descriptor.ConflictPolicy,
                stamp,
                replicas.Serialise(entry.Entity),
                now));
        }

        foreach (OutboxMessage message in captured)
        {
            context.Add(message);
        }
    }

    /// <summary>
    /// Whether the change is an upsert or a delete, as the far end will see it.
    /// </summary>
    /// <remarks>
    /// A <see cref="EntityState.Deleted"/> entry never reaches here — <c>AuditInterceptor</c> rewrites
    /// a hard delete into a soft delete at <c>SaveChanges</c>, which is after this behaviour runs. So
    /// a delete is detected the same way the interceptor detects it: by the <c>deleted_at</c> column
    /// being newly set. The two must agree, or the audit trail and the outbox would disagree about
    /// what happened to a row.
    /// </remarks>
    private static SyncOperationKind Classify(EntityEntry<Entity> entry)
    {
        if (entry.State is EntityState.Deleted)
        {
            return SyncOperationKind.Delete;
        }

        bool softDeleted = entry.State is EntityState.Modified
            && entry.Property(entity => entity.DeletedAt).IsModified
            && entry.Entity.DeletedAt is not null;

        return softDeleted ? SyncOperationKind.Delete : SyncOperationKind.Upsert;
    }
}
