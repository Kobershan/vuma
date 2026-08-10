using VumaRetail.Domain.Backup;

namespace VumaRetail.Application.Abstractions.Backup;

/// <summary>
/// S3-compatible object storage for encrypted snapshots (<c>CLAUDE.md</c> §4, ADR-022).
/// </summary>
/// <remarks>
/// Streams rather than byte arrays throughout. A full snapshot of a year's trading is not something
/// to hold in memory on a back-office PC with 8 GB in it, and the moment the interface says
/// <c>byte[]</c> every implementation has to.
/// </remarks>
public interface IBackupVault
{
    /// <summary>Writes an object, overwriting any object already at that key.</summary>
    /// <param name="objectKey">The key, tenant-prefixed.</param>
    /// <param name="content">The bytes to write. Read to the end.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many bytes were written.</returns>
    Task<long> PutAsync(string objectKey, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens an object for reading.</summary>
    /// <param name="objectKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The object's bytes. The caller disposes.</returns>
    /// <exception cref="BackupObjectNotFoundException">No object at that key.</exception>
    Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>True when an object exists at that key.</summary>
    /// <param name="objectKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Every key under a prefix, for retention and for reconciling the vault against the ledger.</summary>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Removes an object. Used by retention only.</summary>
    /// <param name="objectKey">The key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>A human-readable description of where this vault stores things, for logs and the health surface.</summary>
    string Description { get; }
}

/// <summary>Takes and restores database snapshots.</summary>
/// <remarks>
/// The PostgreSQL implementation shells out to <c>pg_dump</c> and <c>pg_restore</c> in the custom
/// format, which is the only supported way to get a consistent, restorable snapshot of a live
/// database. It is a port rather than a call site because Stage 09's terminals hold SQLite (ADR-003)
/// and will need their own.
/// </remarks>
public interface IBackupEngine
{
    /// <summary>Dumps the database to a stream.</summary>
    /// <param name="destination">Where to write the dump.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many bytes the dump was.</returns>
    /// <exception cref="BackupEngineException">The dump tool failed or is not installed.</exception>
    Task<long> DumpAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Restores a dump into a target database, replacing what is there.</summary>
    /// <param name="source">The dump.</param>
    /// <param name="targetConnectionString">The database to restore into.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="BackupEngineException">The restore tool failed or is not installed.</exception>
    Task RestoreAsync(
        Stream source,
        string targetConnectionString,
        CancellationToken cancellationToken = default);
}

/// <summary>Encrypts a snapshot before it leaves the machine.</summary>
/// <remarks>
/// <para>
/// A snapshot is every row a tenant has: customers, staff, prices, margins. <c>docs/SECURITY.md</c>
/// and POPIA both make it the vendor's problem the moment it lands in a bucket the vendor operates,
/// so it is encrypted with a key the vendor's storage credentials do not unlock.
/// </para>
/// <para>
/// Authenticated encryption, not a bare cipher. A tampered snapshot must fail to decrypt rather than
/// restore into plausible nonsense.
/// </para>
/// </remarks>
public interface ISnapshotCipher
{
    /// <summary>Encrypts a plaintext stream onto a destination stream.</summary>
    /// <param name="plaintext">The dump.</param>
    /// <param name="destination">Where the ciphertext goes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task EncryptAsync(Stream plaintext, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Decrypts a ciphertext stream onto a destination stream.</summary>
    /// <param name="ciphertext">The encrypted snapshot.</param>
    /// <param name="destination">Where the plaintext goes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="SnapshotDecryptionException">Wrong key, or the snapshot has been altered.</exception>
    Task DecryptAsync(Stream ciphertext, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>The snapshot ledger.</summary>
public interface IBackupSnapshotRepository
{
    /// <summary>Records a run.</summary>
    /// <param name="snapshot">The run.</param>
    void Add(BackupSnapshot snapshot);

    /// <summary>Finds a run by id within the current tenant.</summary>
    /// <param name="snapshotId">The run.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BackupSnapshot?> FindAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>The runs, newest first.</summary>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<BackupSnapshot>> ListAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>The most recent completed run, or <c>null</c> if there has never been one.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BackupSnapshot?> FindLatestCompletedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Takes, verifies and restores snapshots — requirement R4.
/// </summary>
/// <remarks>
/// The orchestration is a service rather than a command handler because a full dump is minutes of
/// streaming I/O and holding a database transaction open across it would block every write in the
/// store. The commands record the ledger row; this service does the work.
/// </remarks>
public interface IBackupService
{
    /// <summary>
    /// Dumps, encrypts, uploads and checksums. Returns the completed ledger row.
    /// </summary>
    /// <param name="kind">What the snapshot covers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BackupSnapshot> CreateSnapshotAsync(
        BackupKind kind = BackupKind.Full,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a snapshot back and confirms its checksum, without restoring it.
    /// </summary>
    /// <param name="snapshotId">The snapshot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Domain.Backup.SnapshotChecksumMismatchException">The object is not what was written.</exception>
    Task<BackupSnapshot> VerifySnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a snapshot into a target database. R4's actual claim.
    /// </summary>
    /// <param name="snapshotId">The snapshot.</param>
    /// <param name="targetConnectionString">The database to restore into.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The checksum is confirmed before a single byte is restored. A restore that proceeded on a
    /// corrupted object would produce a database that looks restored and fails at the till.
    /// </remarks>
    Task<BackupSnapshot> RestoreSnapshotAsync(
        Guid snapshotId,
        string targetConnectionString,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a vault has no object at the key it was asked for.</summary>
/// <param name="objectKey">The key.</param>
public sealed class BackupObjectNotFoundException(string objectKey)
    : Exception($"The backup vault holds no object at '{objectKey}'.");

/// <summary>Thrown when <c>pg_dump</c> or <c>pg_restore</c> fails or is not installed.</summary>
/// <param name="message">What went wrong, including the tool's own output.</param>
/// <param name="innerException">The underlying failure, where there is one.</param>
public sealed class BackupEngineException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Thrown when a snapshot will not decrypt — the wrong key, or an altered object.</summary>
/// <param name="innerException">The underlying cryptographic failure.</param>
public sealed class SnapshotDecryptionException(Exception? innerException = null)
    : Exception(
        "The snapshot could not be decrypted. Either the encryption key is not the one it was "
        + "written with, or the object has been altered since.",
        innerException);
