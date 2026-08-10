using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementations of the Stage 04 sync and backup ports.
/// </summary>
/// <remarks>
/// None of these commit — the pipeline owns the transaction (ADR-044) — and none of them filter by
/// tenant or soft delete, because both are global query filters applied to every entity by
/// <see cref="VumaRetailDbContext"/>. The one deliberate exception is documented where it appears.
/// </remarks>
/// <param name="context">The database context.</param>
public sealed class OutboxRepository(VumaRetailDbContext context) : IOutboxRepository
{
    /// <inheritdoc />
    public void Add(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        context.OutboxMessages.Add(message);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ListDueAsync(
        int batchSize,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default)
        => await context.OutboxMessages
            .Where(message => message.Status != OutboxStatus.Dispatched)
            .Where(message => message.NextAttemptAt == null || message.NextAttemptAt <= dueAt)
            // By stamp, not by created_at. The stamp is the causal order (ADR-007); wall-clock
            // creation order is what the sending node's clock happened to say, and shipping out of
            // causal order would make the receiver's conflict resolution reach a different answer
            // than the sender's own history.
            .OrderBy(message => message.OperationStamp)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountOutstandingAsync(CancellationToken cancellationToken = default)
        => context.OutboxMessages
            .CountAsync(message => message.Status != OutboxStatus.Dispatched, cancellationToken);
}

/// <summary>The idempotent inbox (ADR-006).</summary>
/// <param name="context">The database context.</param>
public sealed class InboxRepository(VumaRetailDbContext context) : IInboxRepository
{
    /// <inheritdoc />
    public void Add(InboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        context.InboxMessages.Add(message);
    }

    /// <inheritdoc />
    public async Task<bool> HasProcessedAsync(
        string sourceNode,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNode);

        // The change tracker first: a batch that carries the same operation twice must not apply it
        // twice, and neither copy has reached the database yet.
        if (context.ChangeTracker.Entries<InboxMessage>().Any(entry =>
                entry.Entity.OperationId == operationId
                && string.Equals(entry.Entity.SourceNode, sourceNode, StringComparison.Ordinal)))
        {
            return true;
        }

        // IgnoreQueryFilters for the soft-delete half only — the tenant predicate is written back
        // explicitly. A soft-deleted receipt must still block a replay: it records that the
        // operation was processed, and pruning it is not the same as un-processing it.
        return await context.InboxMessages
            .IgnoreQueryFilters()
            .AnyAsync(
                message => message.TenantId == context.CurrentTenantId
                    && message.SourceNode == sourceNode
                    && message.OperationId == operationId,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Where each peer has got to.</summary>
/// <param name="context">The database context.</param>
public sealed class SyncCursorRepository(VumaRetailDbContext context) : ISyncCursorRepository
{
    /// <inheritdoc />
    public async Task<SyncCursor?> FindAsync(
        string peerNode,
        SyncDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerNode);

        // The change tracker first, so two batches from one peer inside a single request share a
        // cursor rather than each adding one and colliding on the unique index at commit.
        if (context.ChangeTracker.Entries<SyncCursor>().FirstOrDefault(entry =>
                string.Equals(entry.Entity.PeerNode, peerNode, StringComparison.Ordinal)
                && entry.Entity.Direction == direction) is { } tracked)
        {
            return tracked.Entity;
        }

        return await context.SyncCursors
            .FirstOrDefaultAsync(
                cursor => cursor.PeerNode == peerNode && cursor.Direction == direction,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncCursor>> ListAsync(CancellationToken cancellationToken = default)
        => await context.SyncCursors
            .OrderBy(cursor => cursor.PeerNode)
            .ThenBy(cursor => cursor.Direction)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(SyncCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        context.SyncCursors.Add(cursor);
    }
}

/// <summary>The conflict review queue (ADR-007).</summary>
/// <param name="context">The database context.</param>
public sealed class ConflictRepository(VumaRetailDbContext context) : IConflictRepository
{
    /// <inheritdoc />
    public void Add(ConflictEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.ConflictEntries.Add(entry);
    }

    /// <inheritdoc />
    public Task<ConflictEntry?> FindAsync(Guid conflictId, CancellationToken cancellationToken = default)
        => context.ConflictEntries.FirstOrDefaultAsync(entry => entry.Id == conflictId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConflictEntry>> ListAsync(
        bool openOnly,
        int limit,
        CancellationToken cancellationToken = default)
        => await context.ConflictEntries
            .Where(entry => !openOnly || entry.Resolution == ConflictResolution.Unresolved)
            .OrderByDescending(entry => entry.DetectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>The snapshot ledger — requirement R4's evidence.</summary>
/// <param name="context">The database context.</param>
public sealed class BackupSnapshotRepository(VumaRetailDbContext context) : IBackupSnapshotRepository
{
    /// <inheritdoc />
    public void Add(BackupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        context.BackupSnapshots.Add(snapshot);
    }

    /// <inheritdoc />
    public Task<BackupSnapshot?> FindAsync(Guid snapshotId, CancellationToken cancellationToken = default)
        => context.BackupSnapshots.FirstOrDefaultAsync(snapshot => snapshot.Id == snapshotId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupSnapshot>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => await context.BackupSnapshots
            .OrderByDescending(snapshot => snapshot.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<BackupSnapshot?> FindLatestCompletedAsync(CancellationToken cancellationToken = default)
        => context.BackupSnapshots
            .Where(snapshot => snapshot.Status == BackupStatus.Completed)
            .OrderByDescending(snapshot => snapshot.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
