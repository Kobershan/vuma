using VumaRetail.Application.Abstractions.Workflow;

namespace VumaRetail.Infrastructure.Workflow;

/// <summary>Where a filesystem document blob store keeps its objects.</summary>
public sealed class DocumentBlobStoreOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Workflow:DocumentStore";

    /// <summary>The directory document bytes are written under.</summary>
    public string Directory { get; set; } = string.Empty;
}

/// <summary>
/// A document blob store on the local filesystem, on the same pattern as
/// <see cref="VumaRetail.Infrastructure.Backup.FileSystemBackupVault"/> (Stage 04).
/// </summary>
/// <remarks>
/// <para>
/// The working default for this stage's infrastructure-light brief. Written to a staging file and
/// moved into place, so a crash mid-write cannot leave a truncated object at a key a
/// <c>DocumentVersion</c> row already claims is complete.
/// </para>
/// <para>
/// Storage keys are content-addressed by <c>DocumentService</c>
/// (<c>{tenant}/{document}/{version}-{hash}</c>) and are treated as relative paths, checked the same
/// way the backup vault checks a tenant-prefixed key: a key that could climb out of the store's
/// directory is refused rather than followed.
/// </para>
/// </remarks>
/// <param name="options">Where the objects go.</param>
public sealed class FileSystemDocumentBlobStore(DocumentBlobStoreOptions options) : IDocumentBlobStore
{
    private readonly string _root = ResolveRoot(options);

    /// <inheritdoc />
    public async Task<long> PutAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = PathFor(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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
    public Task<Stream> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        string path = PathFor(storageKey);

        return File.Exists(path)
            ? Task.FromResult<Stream>(File.OpenRead(path))
            : throw new DocumentBlobNotFoundException(storageKey);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(storageKey));

        return Task.CompletedTask;
    }

    private static string ResolveRoot(DocumentBlobStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.Directory)
            ? throw new InvalidOperationException(
                $"{DocumentBlobStoreOptions.SectionName}:Directory is not configured for the filesystem document store.")
            : Path.GetFullPath(options.Directory);
    }

    private string PathFor(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        string candidate = Path.GetFullPath(Path.Combine(_root, storageKey));

        return candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? candidate
            : throw new ArgumentException($"'{storageKey}' resolves outside the document store.", nameof(storageKey));
    }
}
