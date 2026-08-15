using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Sync.Protocol;

/// <summary>
/// Applies an inbound batch: deduplicate, resolve, apply, acknowledge (ADR-006, ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// The receiver is the half of ADR-006 that turns an at-least-once transport into an exactly-once
/// effect, and every design choice in it follows from one assumption: <b>every batch may arrive more
/// than once, in any order, arbitrarily late.</b> Not as an error path — as the normal way a store
/// with bad connectivity catches up.
/// </para>
/// <para>
/// So: a replay is a success, not a conflict. An operation older than what the node already has is
/// discarded by the policy rather than by a special case. The cursor moves only on what actually
/// settled. And nothing here decides anything from data the sender chose beyond its own stamp — the
/// conflict policy comes from this node's own <c>[Replicated]</c> declaration, because a sender that
/// could nominate a policy could overwrite any row on this node.
/// </para>
/// <para>
/// It mutates tracked entities and returns. The command pipeline owns the transaction (ADR-044), so
/// the whole batch — the applied rows, the inbox receipts and the cursor — commits together or not at
/// all. A batch that half-committed would leave inbox rows saying operations were processed that were
/// not.
/// </para>
/// </remarks>
/// <param name="registry">The <c>[Replicated]</c> declarations. The only source of conflict policy.</param>
/// <param name="replicas">Reads and writes rows generically.</param>
/// <param name="inbox">The dedup ledger.</param>
/// <param name="conflicts">The review queue.</param>
/// <param name="cursors">Where each peer has got to.</param>
/// <param name="hybridClock">Folds each remote stamp into this node's clock.</param>
/// <param name="node">This node's identity and tier.</param>
/// <param name="tenant">The ambient tenant, re-scoped to the batch's tenant.</param>
/// <param name="clock">Wall-clock time, for the receipts.</param>
/// <param name="scope">Suppresses outbox capture while remote changes are applied.</param>
/// <param name="logger">Where the batch summary goes.</param>
public sealed class SyncReceiver(
    IReplicationRegistry registry,
    IReplicaWriter replicas,
    IInboxRepository inbox,
    IConflictRepository conflicts,
    ISyncCursorRepository cursors,
    IHybridClock hybridClock,
    INodeIdentity node,
    ITenantContext tenant,
    IClock clock,
    IReplicationScope scope,
    ILogger<SyncReceiver> logger)
{
    /// <summary>Applies a batch and returns what happened to each operation.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="SyncTenantMismatchException">The batch is for a tenant the caller is not.</exception>
    public async Task<SyncAcknowledgement> ReceiveAsync(SyncBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        GuardTenant(batch);

        // Re-scope rather than bypass the tenant filter. The cloud serves every store, so it has to
        // move between tenants — but doing it by narrowing to the batch's tenant keeps the global
        // query filter working, whereas bypassing it would leave every read inside this method
        // unfiltered and one missed WHERE away from writing a store's sales into another tenant.
        tenant.SetTenant(batch.TenantId, batch.StoreId);

        List<SyncOperationResult> results = new(batch.Operations.Count);
        HlcStamp settled = HlcStamp.MinValue;
        int settledCount = 0;

        using (scope.BeginApply())
        {
            foreach (SyncOperation operation in batch.Operations.OrderBy(item => item.Stamp))
            {
                SyncOperationResult result = await ApplySafelyAsync(batch, operation, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(result);

                if (result.Outcome is InboxOutcome.Rejected)
                {
                    continue;
                }

                settledCount++;

                if (operation.Stamp > settled)
                {
                    settled = operation.Stamp;
                }
            }
        }

        await AdvanceCursorAsync(batch, settled, settledCount, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Received {OperationCount} operations from {SourceNode} for tenant {TenantId}: "
            + "{AppliedCount} applied, {DuplicateCount} duplicate, {ConflictCount} conflicted, "
            + "{RejectedCount} rejected",
            batch.Operations.Count,
            batch.SourceNode,
            batch.TenantId,
            results.Count(result => result.Outcome == InboxOutcome.Applied),
            results.Count(result => result.Outcome == InboxOutcome.Duplicate),
            results.Count(result => result.Outcome == InboxOutcome.Conflicted),
            results.Count(result => result.Outcome == InboxOutcome.Rejected));

        return new SyncAcknowledgement(node.NodeId, results, settled);
    }

    private void GuardTenant(SyncBatch batch)
    {
        // Guid.Empty means the host serves many tenants and resolves per request — the cloud tier.
        // Anything else is a host pinned to one tenant, and a batch for a different one is either a
        // misconfigured store or somebody trying their luck.
        if (tenant.TenantId != Guid.Empty && tenant.TenantId != batch.TenantId)
        {
            throw new SyncTenantMismatchException(batch.TenantId, tenant.TenantId);
        }
    }

    /// <summary>
    /// Applies one operation, turning a failure of that operation into a rejection of that operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, one unparseable payload takes down the whole batch — and because
    /// <c>ListDueAsync</c> re-selects the same oldest-stamped rows on every pass, the poisoned
    /// operation is in every subsequent batch too. Nothing behind it ever ships again. A store would
    /// stop replicating entirely, indefinitely, because of one bad row, and the only symptom would be
    /// a queue depth that climbs and never falls.
    /// </para>
    /// <para>
    /// Only <see cref="DomainException"/> is caught, and deliberately so. A malformed payload, an
    /// unknown entity or a value that will not convert are all statements about <em>this operation</em>
    /// and are answered by refusing it. A dropped connection or a deadlock is a statement about the
    /// batch, and must roll the whole thing back so the sender retries — swallowing that would
    /// acknowledge operations this node never applied.
    /// </para>
    /// </remarks>
    private async Task<SyncOperationResult> ApplySafelyAsync(
        SyncBatch batch,
        SyncOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyAsync(batch, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException refused)
        {
            logger.LogWarning(
                refused,
                "Refusing operation {OperationId} ({EntityType} {EntityId}) from {SourceNode}: {ErrorCode}",
                operation.OperationId,
                operation.EntityType,
                operation.EntityId,
                batch.SourceNode,
                refused.Code);

            return await RecordAsync(
                batch,
                operation,
                InboxOutcome.Rejected,
                $"{refused.Code}: {refused.Message}",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SyncOperationResult> ApplyAsync(
        SyncBatch batch,
        SyncOperation operation,
        CancellationToken cancellationToken)
    {
        // Fold the sender's stamp into this node's clock before anything else, and do it even for an
        // operation that is about to be rejected. Causality is a property of what this node has seen,
        // not of what it chose to keep.
        hybridClock.Observe(operation.Stamp);

        if (await inbox.HasProcessedAsync(batch.SourceNode, operation.OperationId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new SyncOperationResult(operation.OperationId, InboxOutcome.Duplicate);
        }

        Type? entityType = registry.ResolveEntityType(operation.EntityType);
        ReplicationDescriptor? descriptor = registry.FindByName(operation.EntityType);

        if (entityType is null || descriptor is not { } replication)
        {
            return await RecordAsync(
                batch,
                operation,
                InboxOutcome.Rejected,
                $"This node does not replicate '{operation.EntityType}'.",
                cancellationToken).ConfigureAwait(false);
        }

        if (replication.IsNodeLocal)
        {
            // The sender is offering something it was never supposed to send. Refused rather than
            // ignored, because the sender has a bug and silence would keep it.
            return await RecordAsync(
                batch,
                operation,
                InboxOutcome.Rejected,
                $"'{operation.EntityType}' is node-local and does not replicate.",
                cancellationToken).ConfigureAwait(false);
        }

        ReplicaRow? existing = await replicas
            .FindAsync(entityType, operation.EntityId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not { } local)
        {
            // Nothing to conflict with. A delete for a row this node never had is still applied, as a
            // tombstone — otherwise a late upsert of the same row would resurrect it.
            await replicas.ApplyAsync(entityType, operation, cancellationToken).ConfigureAwait(false);

            return await RecordAsync(batch, operation, InboxOutcome.Applied, null, cancellationToken)
                .ConfigureAwait(false);
        }

        ConflictOutcome outcome = ConflictResolver.Resolve(
            replication.ConflictPolicy,
            local.Stamp,
            operation.Stamp,
            node.Kind,
            batch.SourceKind);

        switch (outcome)
        {
            case ConflictOutcome.TakeRemote:
                await replicas.ApplyAsync(entityType, operation, cancellationToken).ConfigureAwait(false);
                return await RecordAsync(batch, operation, InboxOutcome.Applied, null, cancellationToken)
                    .ConfigureAwait(false);

            case ConflictOutcome.Append:
                // ADR-005: entries accumulate. This node already has a row with this id, so this is
                // the same entry arriving again rather than a competing version of it — the whole
                // point of an append-only entity is that there is nothing to merge.
                return await RecordAsync(
                    batch,
                    operation,
                    InboxOutcome.Applied,
                    "Append-only entry already present.",
                    cancellationToken).ConfigureAwait(false);

            case ConflictOutcome.Review:
                return await OpenConflictAsync(batch, operation, local, cancellationToken).ConfigureAwait(false);

            case ConflictOutcome.KeepLocal:
            default:
                // Settled by policy, deliberately, and the sender is told. Not a rejection: the
                // operation reached this node and was considered, which is exactly what the sender's
                // cursor needs to know so it stops re-sending it.
                return await RecordAsync(
                    batch,
                    operation,
                    InboxOutcome.Conflicted,
                    $"Superseded by the local version under {replication.ConflictPolicy}.",
                    cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SyncOperationResult> OpenConflictAsync(
        SyncBatch batch,
        SyncOperation operation,
        ReplicaRow local,
        CancellationToken cancellationToken)
    {
        ConflictEntry entry = ConflictEntry.Open(
            batch.TenantId,
            batch.StoreId,
            operation.EntityType,
            operation.EntityId,
            operation.OperationId,
            batch.SourceNode,
            local.Payload,
            operation.Payload,
            local.Stamp,
            operation.Stamp,
            clock.UtcNow);

        conflicts.Add(entry);

        await RecordAsync(
            batch,
            operation,
            InboxOutcome.Conflicted,
            "Routed to the review queue; the local row is unchanged.",
            cancellationToken).ConfigureAwait(false);

        return new SyncOperationResult(
            operation.OperationId,
            InboxOutcome.Conflicted,
            entry.Id,
            "Routed to the review queue; the local row is unchanged.");
    }

    private Task<SyncOperationResult> RecordAsync(
        SyncBatch batch,
        SyncOperation operation,
        InboxOutcome outcome,
        string? detail,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        inbox.Add(InboxMessage.Record(
            batch.TenantId,
            batch.StoreId,
            operation.OperationId,
            batch.SourceNode,
            operation.EntityType,
            operation.EntityId,
            operation.Stamp,
            clock.UtcNow,
            outcome,
            detail));

        return Task.FromResult(new SyncOperationResult(operation.OperationId, outcome, null, detail));
    }

    private async Task AdvanceCursorAsync(
        SyncBatch batch,
        HlcStamp settled,
        int settledCount,
        CancellationToken cancellationToken)
    {
        SyncCursor? cursor = await cursors
            .FindAsync(batch.SourceNode, SyncDirection.Inbound, cancellationToken)
            .ConfigureAwait(false);

        if (cursor is null)
        {
            cursor = SyncCursor.Start(batch.TenantId, batch.StoreId, batch.SourceNode, SyncDirection.Inbound);
            cursors.Add(cursor);
        }

        // Contact is recorded even when nothing settled — an empty batch is a heartbeat, and "we
        // have heard from this store" is worth knowing on its own.
        cursor.Advance(settled, settledCount, clock.UtcNow);
    }
}

/// <summary>Thrown when a batch names a tenant the caller is not authenticated for.</summary>
/// <param name="batchTenantId">The tenant the batch claims.</param>
/// <param name="callerTenantId">The tenant the caller actually is.</param>
/// <remarks>
/// The one check standing between "a store may replicate its own data" and "a store may write any
/// row in the estate". It is a refusal rather than a filter, because a batch that named the wrong
/// tenant is a bug or an attack and neither improves by having part of it silently dropped.
/// </remarks>
public sealed class SyncTenantMismatchException(Guid batchTenantId, Guid callerTenantId)
    : DomainException(
        "SYNC_TENANT_MISMATCH",
        $"The batch is for tenant {batchTenantId} but the caller is tenant {callerTenantId}. "
        + "A node replicates its own tenant's data and nobody else's.");
