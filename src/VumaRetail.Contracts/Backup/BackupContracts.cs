namespace VumaRetail.Contracts.Backup;

/// <summary>
/// One snapshot run — requirement R4's audit trail as a client sees it.
/// </summary>
/// <remarks>
/// <see cref="VerifiedAt"/> and <see cref="RestoredAt"/> are the two fields that matter and the two
/// that are usually missing from a backup UI. "Last backup: 03:00 today" is not evidence of anything;
/// "last restored into a scratch database: Tuesday" is.
/// </remarks>
/// <param name="Id">The run.</param>
/// <param name="Kind"><c>Full</c> or <c>Incremental</c>.</param>
/// <param name="SourceNode">The node the snapshot was taken on.</param>
/// <param name="ObjectKey">The vault object key.</param>
/// <param name="Status"><c>Running</c>, <c>Completed</c> or <c>Failed</c>.</param>
/// <param name="StartedAt">When the run started, UTC.</param>
/// <param name="CompletedAt">When it finished, UTC.</param>
/// <param name="SizeBytes">The encrypted object's size.</param>
/// <param name="Checksum">SHA-256 of the encrypted object, lower-case hex.</param>
/// <param name="VerifiedAt">When it was last read back and checksummed, UTC.</param>
/// <param name="RestoredAt">When it was last restored into a database, UTC.</param>
/// <param name="Error">Why it failed, where it did.</param>
public sealed record BackupSnapshotResponse(
    Guid Id,
    string Kind,
    string SourceNode,
    string ObjectKey,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long SizeBytes,
    string Checksum,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? RestoredAt,
    string? Error);

/// <summary>Takes a snapshot now.</summary>
/// <param name="Kind"><c>Full</c>. Reserved for <c>Incremental</c> once Stage 31 decides on WAL shipping.</param>
public sealed record CreateBackupSnapshotRequest(string Kind = "Full");
