using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// The metadata for something attached to, or generated for, a business record — never the bytes
/// themselves, which live behind <c>IDocumentBlobStore</c> and are addressed through
/// <see cref="DocumentVersion"/>.
/// </summary>
/// <remarks>
/// <see cref="EntityId"/> is the business record this document belongs to — a purchase order, a
/// customer, an approval request — referenced by id only, exactly as <see cref="ApprovalRequest.SubjectEntityId"/>
/// is. A document is never overwritten in place: <see cref="RegisterNewVersion"/> only ever moves
/// <see cref="CurrentVersionNumber"/> forward, and the version it points away from stays retrievable —
/// the same non-destructive posture the stock ledger and the audit trail both take.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.LastWriterWins)]
public sealed class Document : Entity
{
    private Document(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Document()
    {
    }

    /// <summary>The owning module.</summary>
    public string Module { get; private set; } = string.Empty;

    /// <summary>What kind of business record this document belongs to.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The business record's id.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// What sort of document this is — <c>receipt</c>, <c>attachment</c>, <c>import-source</c>,
    /// <c>generated-report</c>. A module's own vocabulary, not a closed enum, because the set of
    /// document kinds grows with every module that attaches one.
    /// </summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>A human title, shown in a list.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The MIME type of the current version.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>The latest version number. Starts at 1, the version created alongside the document.</summary>
    public int CurrentVersionNumber { get; private set; } = 1;

    /// <summary>Whether the current version was rendered by an <c>IDocumentRenderer</c> rather than uploaded.</summary>
    public bool IsGenerated { get; private set; }

    /// <summary>The longest title that will be stored.</summary>
    public const int MaxTitleLength = 256;

    /// <summary>Creates a document at version 1. The caller records the matching <see cref="DocumentVersion"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, where the business record has one.</param>
    /// <param name="module">The owning module.</param>
    /// <param name="entityType">What kind of business record this belongs to.</param>
    /// <param name="entityId">The business record's id.</param>
    /// <param name="kind">What sort of document this is.</param>
    /// <param name="title">A human title.</param>
    /// <param name="contentType">The MIME type of the first version.</param>
    /// <param name="source">Whether the first version is uploaded or generated.</param>
    /// <returns>The document, at version 1.</returns>
    public static Document Create(
        Guid tenantId,
        Guid? storeId,
        string module,
        string entityType,
        Guid entityId,
        string kind,
        string title,
        string contentType,
        DocumentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return new Document(tenantId, storeId)
        {
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            Kind = kind,
            Title = title.Length <= MaxTitleLength ? title : title[..MaxTitleLength],
            ContentType = contentType,
            CurrentVersionNumber = 1,
            IsGenerated = source is DocumentSource.Generated,
        };
    }

    /// <summary>
    /// Advances the document to a new version. The caller records the matching
    /// <see cref="DocumentVersion"/> at the returned number.
    /// </summary>
    /// <param name="contentType">The new version's MIME type.</param>
    /// <param name="source">Whether the new version is uploaded or generated.</param>
    /// <returns>The new version number.</returns>
    public int RegisterNewVersion(string contentType, DocumentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        CurrentVersionNumber++;
        ContentType = contentType;
        IsGenerated = source is DocumentSource.Generated;

        return CurrentVersionNumber;
    }
}
