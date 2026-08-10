using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Sync;

/// <summary><c>sync.outbox_messages</c> — the transactional outbox (ADR-006).</summary>
internal sealed class OutboxMessageConfiguration : EntityConfiguration<OutboxMessage>
{
    protected override string Schema => Schemas.Sync;

    protected override string TableName => "outbox_messages";

    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(message => message.OperationId).IsRequired();

        builder.Property(message => message.SourceNode)
            .IsRequired()
            .HasMaxLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);

        builder.Property(message => message.EntityType).IsRequired().HasMaxLength(128);
        builder.Property(message => message.EntityId).IsRequired();

        builder.Property(message => message.Operation)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(message => message.Scope)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(message => message.ConflictPolicy)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(message => message.OperationStamp).IsRequired().HasMaxLength(StampMaxLength);

        // jsonb rather than text. The payload is queried during an investigation ("which sale did
        // this come from"), and jsonb is the difference between that being a query and a grep.
        builder.Property(message => message.Payload).IsRequired().HasColumnType("jsonb");

        builder.Property(message => message.OccurredAt).IsRequired();

        builder.Property(message => message.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(message => message.AttemptCount).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);

        // The dispatcher's only hot query: what is due, oldest first. Partial, because Dispatched is
        // the overwhelming majority of the table within a week of trading and indexing it would mean
        // an index the size of the queue it is meant to make cheap.
        builder.HasIndex(message => new { message.NextAttemptAt, message.OperationStamp })
            .HasDatabaseName("ix_outbox_messages_due")
            .HasFilter("status <> 'Dispatched'");

        // The idempotency key is unique per node. Two rows sharing one would make the receiver's
        // dedup discard a genuine second change.
        builder.HasIndex(message => new { message.SourceNode, message.OperationId })
            .IsUnique()
            .HasDatabaseName("ux_outbox_messages_source_node_operation_id");
    }

    /// <summary>14 digits, 6 digits, two separators and a node id.</summary>
    internal const int StampMaxLength = 14 + 1 + 6 + 1 + Domain.Primitives.HlcStamp.NodeIdMaxLength;
}

/// <summary><c>sync.inbox_messages</c> — the dedup ledger (ADR-006).</summary>
internal sealed class InboxMessageConfiguration : EntityConfiguration<InboxMessage>
{
    protected override string Schema => Schemas.Sync;

    protected override string TableName => "inbox_messages";

    protected override void ConfigureEntity(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.Property(message => message.OperationId).IsRequired();

        builder.Property(message => message.SourceNode)
            .IsRequired()
            .HasMaxLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);

        builder.Property(message => message.EntityType).IsRequired().HasMaxLength(128);
        builder.Property(message => message.EntityId).IsRequired();
        builder.Property(message => message.OperationStamp)
            .IsRequired()
            .HasMaxLength(OutboxMessageConfiguration.StampMaxLength);
        builder.Property(message => message.ReceivedAt).IsRequired();

        builder.Property(message => message.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(message => message.Detail).HasMaxLength(1024);

        // The guarantee, and the reason exactly-once effect is achievable at all. The receiver's
        // HasProcessedAsync check is check-then-act and two concurrent deliveries of one batch will
        // both pass it; this index is what settles that race, and it is deliberately not filtered on
        // deleted_at — a soft-deleted receipt must still block a replay.
        builder.HasIndex(message => new { message.TenantId, message.SourceNode, message.OperationId })
            .IsUnique()
            .HasDatabaseName("ux_inbox_messages_tenant_source_operation");
    }
}

/// <summary><c>sync.sync_cursors</c> — how far each peer has got.</summary>
internal sealed class SyncCursorConfiguration : EntityConfiguration<SyncCursor>
{
    protected override string Schema => Schemas.Sync;

    protected override string TableName => "sync_cursors";

    protected override void ConfigureEntity(EntityTypeBuilder<SyncCursor> builder)
    {
        builder.Property(cursor => cursor.PeerNode)
            .IsRequired()
            .HasMaxLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);

        builder.Property(cursor => cursor.Direction)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(cursor => cursor.AcknowledgedStamp)
            .IsRequired()
            .HasMaxLength(OutboxMessageConfiguration.StampMaxLength);

        builder.Property(cursor => cursor.AcknowledgedCount).IsRequired();

        // One cursor per peer per direction, among live rows. Two would mean two answers to "where
        // did we get to", and the dispatcher would pick whichever the database returned first.
        builder.HasIndex(cursor => new { cursor.TenantId, cursor.PeerNode, cursor.Direction })
            .IsUnique()
            .HasDatabaseName("ux_sync_cursors_tenant_peer_direction")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>sync.conflict_entries</c> — the review queue (ADR-007).</summary>
internal sealed class ConflictEntryConfiguration : EntityConfiguration<ConflictEntry>
{
    protected override string Schema => Schemas.Sync;

    protected override string TableName => "conflict_entries";

    protected override void ConfigureEntity(EntityTypeBuilder<ConflictEntry> builder)
    {
        builder.Property(entry => entry.EntityType).IsRequired().HasMaxLength(128);
        builder.Property(entry => entry.EntityId).IsRequired();
        builder.Property(entry => entry.OperationId).IsRequired();

        builder.Property(entry => entry.SourceNode)
            .IsRequired()
            .HasMaxLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);

        // Both versions in full, as jsonb. The whole point of the queue is that nothing was
        // discarded, so a diff would defeat it.
        builder.Property(entry => entry.LocalVersion).IsRequired().HasColumnType("jsonb");
        builder.Property(entry => entry.RemoteVersion).IsRequired().HasColumnType("jsonb");

        builder.Property(entry => entry.LocalStamp)
            .IsRequired()
            .HasMaxLength(OutboxMessageConfiguration.StampMaxLength);

        builder.Property(entry => entry.RemoteStamp)
            .IsRequired()
            .HasMaxLength(OutboxMessageConfiguration.StampMaxLength);

        builder.Property(entry => entry.DetectedAt).IsRequired();

        builder.Property(entry => entry.Resolution)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.ResolvedBy).HasMaxLength(128);
        builder.Property(entry => entry.ResolutionNote).HasMaxLength(1024);

        // The queue's own query: what is still open, newest first. Partial, because a resolved
        // conflict is history and history is most of the table.
        builder.HasIndex(entry => new { entry.TenantId, entry.DetectedAt })
            .HasDatabaseName("ix_conflict_entries_open")
            .HasFilter("resolution = 'Unresolved' AND deleted_at IS NULL");
    }
}

/// <summary><c>backup.snapshots</c> — requirement R4's audit trail.</summary>
internal sealed class BackupSnapshotConfiguration : EntityConfiguration<BackupSnapshot>
{
    protected override string Schema => Schemas.Backup;

    protected override string TableName => "snapshots";

    protected override void ConfigureEntity(EntityTypeBuilder<BackupSnapshot> builder)
    {
        builder.Property(snapshot => snapshot.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(snapshot => snapshot.SourceNode)
            .IsRequired()
            .HasMaxLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);

        builder.Property(snapshot => snapshot.ObjectKey).IsRequired().HasMaxLength(512);

        builder.Property(snapshot => snapshot.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(snapshot => snapshot.StartedAt).IsRequired();
        builder.Property(snapshot => snapshot.SizeBytes).IsRequired();

        // 64 hex characters, fixed. SHA-256 of the ciphertext, so it can be checked without the key.
        builder.Property(snapshot => snapshot.Checksum).IsRequired().HasMaxLength(64);

        builder.Property(snapshot => snapshot.Error).HasMaxLength(BackupSnapshot.MaxErrorLength);

        // The ledger's query: the recent runs, newest first, and "when did we last succeed".
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.StartedAt })
            .HasDatabaseName("ix_snapshots_tenant_started_at");

        // One object key per snapshot. Two rows pointing at one object would mean deleting one under
        // retention silently breaks the other's restore.
        builder.HasIndex(snapshot => snapshot.ObjectKey)
            .IsUnique()
            .HasDatabaseName("ux_snapshots_object_key");
    }
}
