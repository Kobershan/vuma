using System.Security.Cryptography;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Documents;

/// <summary>
/// Generation, attachment and versioning over <see cref="IDocumentBlobStore"/> and
/// <see cref="IDocumentRenderer"/>.
/// </summary>
/// <remarks>
/// Content-addressed: every version is written under a key derived from its own SHA-256, and that
/// hash is checked again on the way back out (<see cref="OpenVersionAsync"/>) — the same "checked
/// before trusted" posture the backup vault's checksum takes before a restore proceeds.
/// </remarks>
/// <param name="documents">Document metadata.</param>
/// <param name="versions">Version metadata.</param>
/// <param name="blobStore">Where the bytes actually live.</param>
/// <param name="renderer">Turns a template and a model into bytes.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="principal">Who is acting.</param>
public sealed class DocumentService(
    IDocumentRepository documents,
    IDocumentVersionRepository versions,
    IDocumentBlobStore blobStore,
    IDocumentRenderer renderer,
    ITenantContext tenant,
    IPrincipalAccessor principal) : IDocumentService
{
    /// <inheritdoc />
    public async Task<DocumentVersionSummary> AttachAsync(
        string module,
        string entityType,
        Guid entityId,
        string kind,
        string title,
        string contentType,
        Stream content,
        Guid? storeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);

        Document document = Document.Create(
            tenant.TenantId,
            storeId ?? tenant.StoreId,
            module,
            entityType,
            entityId,
            kind,
            title,
            contentType,
            DocumentSource.Uploaded);

        documents.Add(document);

        return await StoreVersionAsync(document, 1, bytes, contentType, DocumentSource.Uploaded, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DocumentVersionSummary> AttachVersionAsync(
        Guid documentId,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        Document document = await FindDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        byte[] bytes = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);

        int versionNumber = document.RegisterNewVersion(contentType, DocumentSource.Uploaded);

        return await StoreVersionAsync(document, versionNumber, bytes, contentType, DocumentSource.Uploaded, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DocumentVersionSummary> GenerateAsync(
        string module,
        string entityType,
        Guid entityId,
        string kind,
        string title,
        DocumentRenderRequest renderRequest,
        Guid? storeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderRequest);

        RenderedDocument rendered = await renderer.RenderAsync(renderRequest, cancellationToken).ConfigureAwait(false);

        byte[] bytes;

        await using (rendered.Content.ConfigureAwait(false))
        {
            bytes = await ReadAllAsync(rendered.Content, cancellationToken).ConfigureAwait(false);
        }

        Document document = Document.Create(
            tenant.TenantId,
            storeId ?? tenant.StoreId,
            module,
            entityType,
            entityId,
            kind,
            title,
            rendered.ContentType,
            DocumentSource.Generated);

        documents.Add(document);

        return await StoreVersionAsync(document, 1, bytes, rendered.ContentType, DocumentSource.Generated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenVersionAsync(
        Guid documentId,
        int? versionNumber,
        CancellationToken cancellationToken = default)
    {
        Document document = await FindDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        int requested = versionNumber ?? document.CurrentVersionNumber;

        DocumentVersion version = await versions
            .FindAsync(documentId, requested, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DocumentNotFoundException($"Document {documentId} has no version {requested}.");

        Stream blob = await blobStore.GetAsync(version.StorageKey, cancellationToken).ConfigureAwait(false);

        byte[] bytes;

        await using (blob.ConfigureAwait(false))
        {
            bytes = await ReadAllAsync(blob, cancellationToken).ConfigureAwait(false);
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (!string.Equals(hash, version.ContentHash, StringComparison.Ordinal))
        {
            throw new DocumentContentTamperedException(documentId, requested);
        }

        return new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
    public async Task<DocumentSummary?> FindAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document? document = await documents.FindAsync(documentId, cancellationToken).ConfigureAwait(false);

        return document is null ? null : ToSummary(document);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentSummary>> ListForEntityAsync(
        string module,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Document> found = await documents
            .ListForEntityAsync(module, entityType, entityId, cancellationToken)
            .ConfigureAwait(false);

        return [.. found.Select(ToSummary)];
    }

    private async Task<Document> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken)
        => await documents.FindAsync(documentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DocumentNotFoundException($"No document {documentId} was found.");

    private async Task<DocumentVersionSummary> StoreVersionAsync(
        Document document,
        int versionNumber,
        byte[] bytes,
        string contentType,
        DocumentSource source,
        CancellationToken cancellationToken)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string storageKey = $"{document.TenantId:N}/{document.Id:N}/{versionNumber:D4}-{hash}";

        using (MemoryStream stream = new(bytes, writable: false))
        {
            await blobStore.PutAsync(storageKey, stream, cancellationToken).ConfigureAwait(false);
        }

        DocumentVersion version = DocumentVersion.Record(
            document.TenantId,
            document.StoreId,
            document.Id,
            versionNumber,
            storageKey,
            hash,
            bytes.LongLength,
            contentType,
            source,
            principal.Principal);

        versions.Add(version);

        return new DocumentVersionSummary(document.Id, versionNumber, hash, bytes.LongLength);
    }

    private static DocumentSummary ToSummary(Document document) => new(
        document.Id,
        document.Module,
        document.EntityType,
        document.EntityId,
        document.Kind,
        document.Title,
        document.ContentType,
        document.CurrentVersionNumber,
        document.IsGenerated);

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }
}

/// <summary>
/// A working <see cref="IDocumentRenderer"/> that stores the model as formatted plain text.
/// </summary>
/// <remarks>
/// Infrastructure-light by this stage's brief. Real templated rendering — QuestPDF receipts and
/// statements — is the stage that first needs one; this is the seam it plugs into without anything
/// upstream, including every command and query in this file, having to change.
/// </remarks>
public sealed class PassthroughDocumentRenderer : IDocumentRenderer
{
    /// <inheritdoc />
    public Task<RenderedDocument> RenderAsync(
        DocumentRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string text = string.Join(
            Environment.NewLine,
            [$"template: {request.TemplateKind}", .. request.Model.Select(pair => $"{pair.Key}: {pair.Value}")]);

        MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(text));

        return Task.FromResult(new RenderedDocument(stream, request.ContentType));
    }
}
