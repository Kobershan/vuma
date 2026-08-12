using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Backup;

namespace VumaRetail.Infrastructure.Backup;

/// <summary>
/// Dump → encrypt → upload → checksum → record, and the restore that proves it (R4).
/// </summary>
/// <remarks>
/// <para>
/// The ledger row is written and committed <em>before</em> the dump starts, and updated afterwards.
/// That costs two transactions and is worth it: a run that only recorded itself on success would
/// leave a crashed backup indistinguishable from a backup nobody scheduled, and those want very
/// different phone calls. It is also why this is a service rather than work inside a command handler
/// — a full dump is minutes of streaming I/O, and holding a write transaction open across it would
/// block every till in the store.
/// </para>
/// <para>
/// A temporary file, not memory. A year of trading does not fit in the RAM of a back-office PC, and
/// the moment the pipeline buffers it does, the backup starts failing on exactly the databases most
/// worth backing up.
/// </para>
/// <para>
/// The checksum is of the ciphertext, so the cloud tier can confirm an object is intact without
/// holding a key that decrypts a tenant's data — which is the whole posture ADR-024 sets out.
/// </para>
/// </remarks>
/// <param name="engine">Takes and restores the database snapshot.</param>
/// <param name="cipher">Encrypts before anything leaves the machine.</param>
/// <param name="vault">Where the object goes.</param>
/// <param name="snapshots">The ledger.</param>
/// <param name="unitOfWork">The transaction the ledger rows are written in.</param>
/// <param name="tenant">Whose data is being backed up.</param>
/// <param name="node">Which node took it.</param>
/// <param name="clock">When.</param>
/// <param name="logger">Where the run summaries go.</param>
public sealed class BackupService(
    IBackupEngine engine,
    ISnapshotCipher cipher,
    IBackupVault vault,
    IBackupSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    INodeIdentity node,
    IClock clock,
    ILogger<BackupService> logger) : IBackupService
{
    /// <inheritdoc />
    public async Task<BackupSnapshot> CreateSnapshotAsync(
        BackupKind kind = BackupKind.Full,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAt = clock.UtcNow;
        string objectKey = ObjectKeyFor(tenant.TenantId, node.NodeId, startedAt);

        BackupSnapshot snapshot = BackupSnapshot.Begin(
            tenant.TenantId,
            tenant.StoreId,
            kind,
            node.NodeId,
            objectKey,
            startedAt);

        snapshots.Add(snapshot);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        string staging = Path.Combine(Path.GetTempPath(), $"vuma-snapshot-{snapshot.Id:N}.enc");

        try
        {
            (long size, string checksum) = await WriteSnapshotAsync(staging, objectKey, cancellationToken)
                .ConfigureAwait(false);

            snapshot.Complete(size, checksum, clock.UtcNow);

            logger.LogInformation(
                "Snapshot {SnapshotId} written to {ObjectKey} in {Vault} — {ByteCount} bytes, sha256 {Checksum}",
                snapshot.Id,
                objectKey,
                vault.Description,
                size,
                checksum);
        }
#pragma warning disable CA1031 // Any failure must be recorded on the ledger before it propagates.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            snapshot.Fail(failure.Message, clock.UtcNow);

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            logger.LogError(failure, "Snapshot {SnapshotId} failed", snapshot.Id);

            throw;
        }
        finally
        {
            Discard(staging);
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return snapshot;
    }

    /// <inheritdoc />
    public async Task<BackupSnapshot> VerifySnapshotAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        BackupSnapshot snapshot = await snapshots.FindAsync(snapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new SnapshotNotFoundException(snapshotId);

        if (!snapshot.IsRestorable)
        {
            throw new SnapshotNotRestorableException(snapshotId, snapshot.Status);
        }

        string actual = await ChecksumOfVaultObjectAsync(snapshot.ObjectKey, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(actual, snapshot.Checksum, StringComparison.Ordinal))
        {
            throw new SnapshotChecksumMismatchException(snapshotId, snapshot.Checksum, actual);
        }

        snapshot.MarkVerified(clock.UtcNow);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Snapshot {SnapshotId} verified against {ObjectKey}", snapshotId, snapshot.ObjectKey);

        return snapshot;
    }

    /// <inheritdoc />
    public async Task<BackupSnapshot> RestoreSnapshotAsync(
        Guid snapshotId,
        string targetConnectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetConnectionString);

        BackupSnapshot snapshot = await snapshots.FindAsync(snapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new SnapshotNotFoundException(snapshotId);

        if (!snapshot.IsRestorable)
        {
            throw new SnapshotNotRestorableException(snapshotId, snapshot.Status);
        }

        // Checked before a single byte is restored. A restore that proceeded on a corrupted object
        // would produce a database that looks restored and fails at the till, which is worse than
        // not restoring at all — the shop would stop looking for the real backup.
        string actual = await ChecksumOfVaultObjectAsync(snapshot.ObjectKey, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(actual, snapshot.Checksum, StringComparison.Ordinal))
        {
            throw new SnapshotChecksumMismatchException(snapshotId, snapshot.Checksum, actual);
        }

        string staging = Path.Combine(Path.GetTempPath(), $"vuma-restore-{snapshot.Id:N}.dump");

        try
        {
            await using (Stream encrypted = await vault.GetAsync(snapshot.ObjectKey, cancellationToken)
                .ConfigureAwait(false))
            await using (FileStream plaintext = File.Create(staging))
            {
                await cipher.DecryptAsync(encrypted, plaintext, cancellationToken).ConfigureAwait(false);
            }

            await using (FileStream dump = File.OpenRead(staging))
            {
                await engine.RestoreAsync(dump, targetConnectionString, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Discard(staging);
        }

        snapshot.MarkRestored(clock.UtcNow);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Snapshot {SnapshotId} restored. A restore replaces a database — this line is a warning "
            + "so it is visible in a log nobody was watching at the time.",
            snapshotId);

        return snapshot;
    }

    /// <summary>
    /// The object key: tenant, then node, then a sortable timestamp.
    /// </summary>
    /// <remarks>
    /// Tenant first so one bucket can hold many tenants and a prefix policy can separate them at the
    /// storage layer as well as in code. Sortable timestamp so a retention job and a human listing
    /// the prefix see the same order without parsing anything.
    /// </remarks>
    private static string ObjectKeyFor(Guid tenantId, string nodeId, DateTimeOffset at)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"tenants/{tenantId:D}/{Sanitise(nodeId)}/{at.UtcDateTime:yyyy/MM/dd}/snapshot-{at.UtcDateTime:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.vsnap");

    /// <summary>Keeps a node id from turning an object key into a path.</summary>
    private static string Sanitise(string nodeId)
        => new([.. nodeId.Select(character => char.IsLetterOrDigit(character) ? character : '-')]);

    private async Task<(long Size, string Checksum)> WriteSnapshotAsync(
        string staging,
        string objectKey,
        CancellationToken cancellationToken)
    {
        string dump = $"{staging}.dump";

        try
        {
            await using (FileStream plaintext = File.Create(dump))
            {
                await engine.DumpAsync(plaintext, cancellationToken).ConfigureAwait(false);
            }

            await using (FileStream source = File.OpenRead(dump))
            await using (FileStream encrypted = File.Create(staging))
            {
                await cipher.EncryptAsync(source, encrypted, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Discard(dump);
        }

        string checksum;
        long size;

        await using (FileStream encrypted = File.OpenRead(staging))
        {
            checksum = await ChecksumAsync(encrypted, cancellationToken).ConfigureAwait(false);
            size = encrypted.Length;
        }

        await using (FileStream encrypted = File.OpenRead(staging))
        {
            await vault.PutAsync(objectKey, encrypted, cancellationToken).ConfigureAwait(false);
        }

        return (size, checksum);
    }

    private async Task<string> ChecksumOfVaultObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        await using Stream stored = await vault.GetAsync(objectKey, cancellationToken).ConfigureAwait(false);

        return await ChecksumAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ChecksumAsync(Stream content, CancellationToken cancellationToken)
    {
        byte[] hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A staging file left behind is untidy, not a failure. Losing the snapshot because the
            // cleanup threw would be the failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
