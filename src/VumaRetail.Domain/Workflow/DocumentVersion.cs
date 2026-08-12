using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// One immutable version of a <see cref="Document"/>'s bytes.
/// </summary>
/// <remarks>
/// The row that actually points at storage. <see cref="StorageKey"/> is what
/// <c>IDocumentBlobStore</c> was asked to hold the bytes under, and <see cref="ContentHash"/> is the
/// SHA-256 of what was written — checked on read the same way a backup snapshot's checksum is checked
/// before a restore proceeds (<c>SYNC_AND_BACKUP.md</c>), so a version that has been altered in the
/// blob store is caught rather than served.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class DocumentVersion : Entity
{
    private DocumentVersion(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private DocumentVersion()
    {
    }

    /// <summary>The document this version belongs to.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Which version this is. Matches <see cref="Document.CurrentVersionNumber"/> at the time it was current.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>The key this version's bytes are stored under in <c>IDocumentBlobStore</c>.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>The SHA-256 of the bytes, as lower-case hex.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>The size of the bytes, in bytes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The MIME type of this version.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Whether this version was uploaded or rendered by an <c>IDocumentRenderer</c>.</summary>
    public DocumentSource Source { get; private set; }

    /// <summary>The principal that produced this version — who uploaded it, or what generated it.</summary>
    public string GeneratedBy { get; private set; } = string.Empty;

    /// <summary>The length of a SHA-256 digest in lower-case hex.</summary>
    public const int ContentHashLength = 64;

    /// <summary>Records a version.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, where the document has one.</param>
    /// <param name="documentId">The document this version belongs to.</param>
    /// <param name="versionNumber">Which version this is.</param>
    /// <param name="storageKey">The blob store key.</param>
    /// <param name="contentHash">The SHA-256 of the bytes, as lower-case hex.</param>
    /// <param name="sizeBytes">The size of the bytes.</param>
    /// <param name="contentType">The MIME type.</param>
    /// <param name="source">Whether this version is uploaded or generated.</param>
    /// <param name="generatedBy">The principal that produced this version.</param>
    /// <returns>The version.</returns>
    public static DocumentVersion Record(
        Guid tenantId,
        Guid? storeId,
        Guid documentId,
        int versionNumber,
        string storageKey,
        string contentHash,
        long sizeBytes,
        string contentType,
        DocumentSource source,
        string generatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedBy);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "A document version cannot have a negative size.");
        }

        return new DocumentVersion(tenantId, storeId)
        {
            DocumentId = documentId,
            VersionNumber = versionNumber,
            StorageKey = storageKey,
            ContentHash = contentHash,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            Source = source,
            GeneratedBy = generatedBy,
        };
    }
}
