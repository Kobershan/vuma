using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>
/// Captures outbox rows for entities written on a service-owned company context.
/// </summary>
/// <remarks>
/// The pipeline's <c>OutboxBehaviour</c> only sees the ambient context, while reservation legs,
/// compensation releases and split segments all commit on contexts they opened themselves (each
/// leg its own serialisable transaction, ADR-102). Without this, those rows would never
/// replicate — a store that burned down would lose exactly the holds that explain its stock.
/// Everything here is derived from each entity's own <c>[Replicated]</c> declaration, the same
/// rule the behaviour follows: node-local projections are skipped, everything else is captured
/// with the node's stamp and serialised payload.
/// </remarks>
internal static class CompanyOutboxCapture
{
    /// <summary>Captures one outbox row per replicable entity, into the same context (same transaction).</summary>
    public static void Capture(
        VumaRetailDbContext db,
        IReplicationRegistry replication,
        IReplicaWriter replicas,
        IHybridClock hybridClock,
        INodeIdentity node,
        IClock clock,
        params Entity[] rows)
    {
        ArgumentNullException.ThrowIfNull(db);

        foreach (Entity row in rows)
        {
            if (replication.Find(row.GetType()) is not { } descriptor || descriptor.IsNodeLocal)
            {
                continue;
            }

            HlcStamp stamp = hybridClock.Next();
            row.MarkSyncStamp(stamp);
            row.MarkSyncState(SyncState.Pending);

            db.OutboxMessages.Add(OutboxMessage.Capture(
                row.TenantId,
                row.StoreId,
                UuidV7.NewGuid(),
                node.NodeId,
                row.GetType().Name,
                row.Id,
                SyncOperationKind.Upsert,
                descriptor.Scope,
                descriptor.ConflictPolicy,
                stamp,
                replicas.Serialise(row),
                clock.UtcNow));
        }
    }
}
