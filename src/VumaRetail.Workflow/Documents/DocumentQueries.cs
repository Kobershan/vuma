using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;

namespace VumaRetail.Workflow.Documents;

/// <summary>A document's metadata.</summary>
/// <param name="DocumentId">The document.</param>
public sealed record GetDocumentQuery(Guid DocumentId) : IQuery<DocumentSummary>;

/// <summary>Finds the document through the document service.</summary>
/// <param name="documentService">Generation, attachment and versioning.</param>
public sealed class GetDocumentQueryHandler(IDocumentService documentService)
    : IQueryHandler<GetDocumentQuery, DocumentSummary>
{
    /// <inheritdoc />
    public async Task<DocumentSummary> HandleAsync(
        GetDocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await documentService.FindAsync(query.DocumentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DocumentNotFoundException($"No document {query.DocumentId} was found.");
    }
}

/// <summary>Every document attached to a business record.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="EntityType">What kind of business record.</param>
/// <param name="EntityId">The business record's id.</param>
public sealed record ListDocumentsForEntityQuery(string Module, string EntityType, Guid EntityId)
    : IQuery<IReadOnlyList<DocumentSummary>>;

/// <summary>Lists documents through the document service.</summary>
/// <param name="documentService">Generation, attachment and versioning.</param>
public sealed class ListDocumentsForEntityQueryHandler(IDocumentService documentService)
    : IQueryHandler<ListDocumentsForEntityQuery, IReadOnlyList<DocumentSummary>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentSummary>> HandleAsync(
        ListDocumentsForEntityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return documentService.ListForEntityAsync(query.Module, query.EntityType, query.EntityId, cancellationToken);
    }
}
