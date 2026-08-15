using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Contracts.Backup;
using VumaRetail.Contracts.Sync;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Sync.Queries;

/// <summary>
/// This node's replication health.
/// </summary>
/// <remarks>
/// Read-only in the strong sense: it must keep answering while a tenant is read-only, because "why
/// has nothing reached head office" is exactly the question somebody asks during a lapse, and
/// ADR-028 promises reporting keeps working.
/// </remarks>
public sealed record GetSyncStatusQuery : IQuery<SyncStatusResponse>;

/// <summary>Reads the cursors, the outbox depth and the open conflict count.</summary>
/// <param name="cursors">Where each peer has got to.</param>
/// <param name="outbox">How much is waiting.</param>
/// <param name="conflicts">The review queue.</param>
/// <param name="hybridClock">This node's clock reading.</param>
/// <param name="node">This node's identity.</param>
public sealed class GetSyncStatusQueryHandler(
    ISyncCursorRepository cursors,
    IOutboxRepository outbox,
    IConflictRepository conflicts,
    IHybridClock hybridClock,
    INodeIdentity node) : IQueryHandler<GetSyncStatusQuery, SyncStatusResponse>
{
    /// <inheritdoc />
    public async Task<SyncStatusResponse> HandleAsync(
        GetSyncStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SyncCursor> tracked = await cursors.ListAsync(cancellationToken).ConfigureAwait(false);
        int outstanding = await outbox.CountOutstandingAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ConflictEntry> open = await conflicts
            .ListAsync(openOnly: true, limit: MaxCountedConflicts, cancellationToken)
            .ConfigureAwait(false);

        return new SyncStatusResponse(
            node.NodeId,
            node.Kind.ToString(),
            hybridClock.Current.ToString(),
            outstanding,
            open.Count,
            [.. tracked.Select(cursor => new SyncCursorDto(
                cursor.PeerNode,
                cursor.Direction.ToString(),
                cursor.AcknowledgedStamp,
                cursor.AcknowledgedCount,
                cursor.LastContactAt))]);
    }

    /// <summary>
    /// The count is capped rather than exact. A dashboard needs "is it zero, a few, or a lot", and a
    /// <c>COUNT(*)</c> over an unbounded queue on every status poll is how a health endpoint becomes
    /// the slowest query in the system.
    /// </summary>
    private const int MaxCountedConflicts = 200;
}

/// <summary>The conflict review queue (ADR-007).</summary>
/// <param name="OpenOnly">Only entries still waiting for a person.</param>
/// <param name="Limit">How many to take.</param>
public sealed record GetConflictQueueQuery(bool OpenOnly = true, int Limit = 50)
    : IQuery<IReadOnlyList<ConflictResponse>>;

/// <summary>Reads the queue, keeping both versions on each entry.</summary>
/// <param name="conflicts">The review queue.</param>
public sealed class GetConflictQueueQueryHandler(IConflictRepository conflicts)
    : IQueryHandler<GetConflictQueueQuery, IReadOnlyList<ConflictResponse>>
{
    /// <summary>The most entries one page returns, whatever was asked for.</summary>
    public const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConflictResponse>> HandleAsync(
        GetConflictQueueQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ConflictEntry> entries = await conflicts
            .ListAsync(query.OpenOnly, Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        return [.. entries.Select(entry => new ConflictResponse(
            entry.Id,
            entry.EntityType,
            entry.EntityId,
            entry.SourceNode,
            entry.LocalVersion,
            entry.RemoteVersion,
            entry.LocalStamp,
            entry.RemoteStamp,
            entry.DetectedAt,
            entry.Resolution.ToString(),
            entry.ResolvedAt,
            entry.ResolvedBy,
            entry.ResolutionNote))];
    }
}

/// <summary>The snapshot ledger — requirement R4's evidence.</summary>
/// <param name="Limit">How many to take.</param>
public sealed record ListBackupSnapshotsQuery(int Limit = 50) : IQuery<IReadOnlyList<BackupSnapshotResponse>>;

/// <summary>Reads the snapshot ledger, newest first.</summary>
/// <param name="snapshots">The ledger.</param>
public sealed class ListBackupSnapshotsQueryHandler(IBackupSnapshotRepository snapshots)
    : IQueryHandler<ListBackupSnapshotsQuery, IReadOnlyList<BackupSnapshotResponse>>
{
    /// <summary>The most runs one page returns, whatever was asked for.</summary>
    public const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupSnapshotResponse>> HandleAsync(
        ListBackupSnapshotsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<BackupSnapshot> ledger = await snapshots
            .ListAsync(Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        return [.. ledger.Select(snapshot => new BackupSnapshotResponse(
            snapshot.Id,
            snapshot.Kind.ToString(),
            snapshot.SourceNode,
            snapshot.ObjectKey,
            snapshot.Status.ToString(),
            snapshot.StartedAt,
            snapshot.CompletedAt,
            snapshot.SizeBytes,
            snapshot.Checksum,
            snapshot.VerifiedAt,
            snapshot.RestoredAt,
            snapshot.Error))];
    }
}
