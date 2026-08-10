using Amazon.S3;
using Amazon.S3.Model;
using VumaRetail.Application.Abstractions.Backup;

namespace VumaRetail.Infrastructure.Backup;

/// <summary>Which vault a host uses and how to reach it.</summary>
public sealed class BackupVaultOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Backup:Vault";

    /// <summary><c>FileSystem</c> or <c>S3</c>.</summary>
    public string Provider { get; set; } = "FileSystem";

    /// <summary>Where a filesystem vault keeps its objects.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>The S3 bucket.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// The S3 endpoint, for an S3-compatible service that is not AWS.
    /// </summary>
    /// <remarks>
    /// <c>CLAUDE.md</c> §4 names Backblaze B2, Wasabi and MinIO alongside AWS S3. All three are
    /// reached by pointing the SDK at their endpoint and turning on path-style addressing, which is
    /// what <see cref="ForcePathStyle"/> does.
    /// </remarks>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>The AWS region, where the endpoint is AWS itself.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>The access key id. A secret; never committed.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>The secret access key. A secret; never committed.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Path-style addressing, which every non-AWS S3-compatible service needs.</summary>
    public bool ForcePathStyle { get; set; } = true;
}

/// <summary>
/// A vault on the local filesystem.
/// </summary>
/// <remarks>
/// <para>
/// ADR-022's "fake implementation shipped so the system is fully testable" — but it is more than a
/// test double, and it is not a stub. Pointed at a NAS or a mounted network share it is a working
/// backup target, which is what a single-store customer with no cloud subscription actually has. It
/// is also what the drill in this stage's test suite runs against, so the restore path R4 depends on
/// is exercised on every CI run rather than only where somebody has provisioned a bucket.
/// </para>
/// <para>
/// Keys are treated as relative paths and are checked: a key that escapes the vault directory is
/// refused. Object keys carry a tenant id, and a tenant id that could contain <c>../</c> would be a
/// path traversal with a write on the end of it.
/// </para>
/// </remarks>
/// <param name="options">Where the objects go.</param>
public sealed class FileSystemBackupVault(BackupVaultOptions options) : IBackupVault
{
    private readonly string _root = ResolveRoot(options);

    /// <inheritdoc />
    public string Description => $"filesystem:{_root}";

    /// <inheritdoc />
    public async Task<long> PutAsync(
        string objectKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = PathFor(objectKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written to a temporary name and moved into place. A crash mid-write would otherwise leave
        // a truncated object at the key the ledger says holds a complete snapshot — which is the one
        // thing a backup must never do.
        string staging = $"{path}.{Guid.NewGuid():N}.partial";

        try
        {
            long written;

            await using (FileStream file = File.Create(staging))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                written = file.Length;
            }

            File.Move(staging, path, overwrite: true);

            return written;
        }
        catch
        {
            File.Delete(staging);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        string path = PathFor(objectKey);

        return File.Exists(path)
            ? Task.FromResult<Stream>(File.OpenRead(path))
            : throw new BackupObjectNotFoundException(objectKey);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(PathFor(objectKey)));

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (!Directory.Exists(_root))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> keys =
        [
            .. Directory
                .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Where(key => !key.EndsWith(".partial", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        return Task.FromResult(keys);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(objectKey));

        return Task.CompletedTask;
    }

    private static string ResolveRoot(BackupVaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.Directory)
            ? throw new InvalidOperationException(
                $"{BackupVaultOptions.SectionName}:Directory is not configured for the filesystem vault.")
            : Path.GetFullPath(options.Directory);
    }

    private string PathFor(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        string candidate = Path.GetFullPath(Path.Combine(_root, objectKey));

        // The traversal check. Object keys are tenant-prefixed, and a key that could climb out of the
        // vault root would let one tenant's snapshot overwrite another's — or anything else the
        // service account can write.
        return candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? candidate
            : throw new ArgumentException($"'{objectKey}' resolves outside the vault.", nameof(objectKey));
    }
}

/// <summary>
/// A vault on S3 or any S3-compatible service (<c>CLAUDE.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// The production adapter. It is written against the AWS SDK and configured by endpoint, so the same
/// class serves AWS S3, Backblaze B2, Wasabi and MinIO — which is the point of choosing an
/// S3-compatible interface rather than a vendor's own.
/// </para>
/// <para>
/// <b>Untested in CI, and that is stated rather than hidden.</b> Nothing in this repository has a
/// bucket or a credential, so <see cref="FileSystemBackupVault"/> is what the drill exercises and
/// what proves the restore path. This class is compiled and reviewed; it is logged in
/// <c>docs/PROGRESS.md</c> under "Deferred — needs real credentials" until somebody runs it against
/// a real endpoint.
/// </para>
/// </remarks>
/// <param name="client">The configured S3 client.</param>
/// <param name="options">The bucket and endpoint.</param>
public sealed class S3BackupVault(IAmazonS3 client, BackupVaultOptions options) : IBackupVault
{
    /// <inheritdoc />
    public string Description => $"s3:{options.Bucket}";

    /// <inheritdoc />
    public async Task<long> PutAsync(
        string objectKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);

        long start = content.CanSeek ? content.Position : 0;

        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = objectKey,
                InputStream = content,
                AutoCloseStream = false,
                // Server-side encryption on top of the snapshot's own AES-GCM. Belt and braces, and
                // the braces are ours: the object is already unreadable without Vuma's key, so a
                // provider-side key compromise is not a data breach.
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
            },
            cancellationToken).ConfigureAwait(false);

        return content.CanSeek ? content.Position - start : 0;
    }

    /// <inheritdoc />
    public async Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        try
        {
            GetObjectResponse response = await client
                .GetObjectAsync(options.Bucket, objectKey, cancellationToken)
                .ConfigureAwait(false);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new BackupObjectNotFoundException(objectKey);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        try
        {
            await client
                .GetObjectMetadataAsync(options.Bucket, objectKey, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        List<string> keys = [];
        string? continuation = null;

        do
        {
            ListObjectsV2Response page = await client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = options.Bucket,
                    Prefix = prefix,
                    ContinuationToken = continuation,
                },
                cancellationToken).ConfigureAwait(false);

            keys.AddRange(page.S3Objects.Select(item => item.Key));
            continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuation is not null);

        return keys;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        await client.DeleteObjectAsync(options.Bucket, objectKey, cancellationToken).ConfigureAwait(false);
    }
}
