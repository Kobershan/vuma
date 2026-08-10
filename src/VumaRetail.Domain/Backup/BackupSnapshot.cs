using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Backup;

/// <summary>What a snapshot covers.</summary>
public enum BackupKind
{
    /// <summary>Every table, every row. The only kind Stage 04 takes.</summary>
    Full = 0,

    /// <summary>
    /// Changes since the previous snapshot. Reserved: Stage 31 decides whether WAL shipping is added
    /// on top of full snapshots, and the enum value exists so that decision is not a schema change.
    /// </summary>
    Incremental = 1,
}

/// <summary>Where a snapshot run got to.</summary>
public enum BackupStatus
{
    /// <summary>Started. A row in this state after a crash is how an interrupted run is visible at all.</summary>
    Running = 0,

    /// <summary>Written to the vault, checksummed, and recorded.</summary>
    Completed = 1,

    /// <summary>Failed, with the reason.</summary>
    Failed = 2,
}

/// <summary>
/// One snapshot run — requirement R4's audit trail.
/// </summary>
/// <remarks>
/// <para>
/// R4 is not "backups are taken". It is "store burns down → new box, run restore, back trading",
/// which is a claim about a <em>restore</em> and can only be supported by evidence that one has
/// happened. So this row records not just that a snapshot was written, but its SHA-256, when it was
/// last verified, and when it was last restored. A snapshot nobody has ever restored is a hope.
/// </para>
/// <para>
/// The row is created in <see cref="BackupStatus.Running"/> before the dump starts, deliberately. A
/// run that only recorded itself on success would leave a crashed backup indistinguishable from a
/// backup nobody scheduled — and those want very different phone calls.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.StoreToCloud"/>: backup health is exactly what a
/// multi-store operator needs to see without walking into each shop. The snapshot <em>content</em>
/// never travels this way — it goes to the vault as an encrypted object, and only the metadata is a
/// row.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class BackupSnapshot : Entity
{
    private BackupSnapshot(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private BackupSnapshot()
    {
    }

    /// <summary>What the snapshot covers.</summary>
    public BackupKind Kind { get; private set; }

    /// <summary>The node the snapshot was taken on.</summary>
    public string SourceNode { get; private set; } = string.Empty;

    /// <summary>The vault object key. Tenant-prefixed, so one bucket can hold many tenants safely.</summary>
    public string ObjectKey { get; private set; } = string.Empty;

    /// <summary>Where the run got to.</summary>
    public BackupStatus Status { get; private set; } = BackupStatus.Running;

    /// <summary>When the run started, UTC.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it finished, UTC, or <c>null</c> while it runs.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The encrypted object's size in bytes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>
    /// SHA-256 of the encrypted object, lower-case hex.
    /// </summary>
    /// <remarks>
    /// Of the ciphertext rather than the plaintext, so it can be checked without the key — which is
    /// what a monitoring job in the cloud tier is able to do. The plaintext's integrity is covered by
    /// AES-GCM's authentication tag, which fails the decrypt outright.
    /// </remarks>
    public string Checksum { get; private set; } = string.Empty;

    /// <summary>When the snapshot was last read back and its checksum confirmed, UTC.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>When the snapshot was last restored into a database, UTC. R4's actual evidence.</summary>
    public DateTimeOffset? RestoredAt { get; private set; }

    /// <summary>Why the run failed, where it did.</summary>
    public string? Error { get; private set; }

    /// <summary>True when the snapshot is on the vault and checksummed.</summary>
    public bool IsRestorable => Status == BackupStatus.Completed;

    /// <summary>Opens a snapshot run.</summary>
    /// <param name="tenantId">The tenant being backed up.</param>
    /// <param name="storeId">The store, where the node is store-scoped.</param>
    /// <param name="kind">What the snapshot covers.</param>
    /// <param name="sourceNode">The node taking it.</param>
    /// <param name="objectKey">The vault object key it will be written to.</param>
    /// <param name="startedAt">When the run started, UTC.</param>
    public static BackupSnapshot Begin(
        Guid tenantId,
        Guid? storeId,
        BackupKind kind,
        string sourceNode,
        string objectKey,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        return new BackupSnapshot(tenantId, storeId)
        {
            Kind = kind,
            SourceNode = sourceNode,
            ObjectKey = objectKey,
            StartedAt = startedAt,
        };
    }

    /// <summary>Records a successful run.</summary>
    /// <param name="sizeBytes">The encrypted object's size.</param>
    /// <param name="checksum">SHA-256 of the encrypted object, lower-case hex.</param>
    /// <param name="completedAt">When the run finished, UTC.</param>
    public void Complete(long sizeBytes, string checksum, DateTimeOffset completedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);

        Status = BackupStatus.Completed;
        SizeBytes = sizeBytes;
        Checksum = checksum;
        CompletedAt = completedAt;
        Error = null;
    }

    /// <summary>Records a failed run.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="failedAt">When it failed, UTC.</param>
    public void Fail(string error, DateTimeOffset failedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Status = BackupStatus.Failed;
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        CompletedAt = failedAt;
    }

    /// <summary>Records that the object was read back and its checksum matched.</summary>
    /// <param name="at">When, UTC.</param>
    /// <exception cref="SnapshotNotRestorableException">The run did not complete.</exception>
    public void MarkVerified(DateTimeOffset at)
    {
        if (!IsRestorable)
        {
            throw new SnapshotNotRestorableException(Id, Status);
        }

        VerifiedAt = at;
    }

    /// <summary>Records that the snapshot was restored into a database.</summary>
    /// <param name="at">When, UTC.</param>
    /// <exception cref="SnapshotNotRestorableException">The run did not complete.</exception>
    public void MarkRestored(DateTimeOffset at)
    {
        if (!IsRestorable)
        {
            throw new SnapshotNotRestorableException(Id, Status);
        }

        RestoredAt = at;
    }

    /// <summary>The widest an error message is stored. Matches the column.</summary>
    public const int MaxErrorLength = 1024;
}

/// <summary>Thrown when a snapshot that never completed is verified or restored.</summary>
/// <param name="snapshotId">The snapshot.</param>
/// <param name="status">Where the run actually got to.</param>
public sealed class SnapshotNotRestorableException(Guid snapshotId, BackupStatus status)
    : DomainException(
        "BACKUP_SNAPSHOT_NOT_RESTORABLE",
        $"Snapshot {snapshotId} is {status}. Only a completed snapshot can be verified or restored.");

/// <summary>Thrown when a snapshot is asked for and this tenant has no such run.</summary>
/// <param name="snapshotId">The snapshot that was asked for.</param>
public sealed class SnapshotNotFoundException(Guid snapshotId)
    : DomainException(
        "BACKUP_SNAPSHOT_NOT_FOUND",
        $"No snapshot {snapshotId}.",
        DomainProblemKind.NotFound);

/// <summary>
/// Thrown when a snapshot read back from the vault is not the snapshot that was written.
/// </summary>
/// <param name="snapshotId">The snapshot.</param>
/// <param name="expected">The checksum recorded when it was written.</param>
/// <param name="actual">The checksum of what came back.</param>
/// <remarks>
/// The failure R4 is actually about. A restore that proceeded on a corrupted object would produce a
/// database that looks restored, and the shop would discover the truth at the till.
/// </remarks>
public sealed class SnapshotChecksumMismatchException(Guid snapshotId, string expected, string actual)
    : DomainException(
        "BACKUP_SNAPSHOT_CHECKSUM_MISMATCH",
        $"Snapshot {snapshotId} does not match the checksum recorded when it was written "
        + $"(expected {expected}, got {actual}). It will not be restored.");
