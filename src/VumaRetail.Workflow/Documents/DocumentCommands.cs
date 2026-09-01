using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;

namespace VumaRetail.Workflow.Documents;

/// <summary>The largest document this stage's primitives will accept in one call.</summary>
/// <remarks>
/// Twenty-five megabytes, generously above anything Stage 05 needs to prove and well short of the
/// multi-gigabyte streaming <see cref="Application.Abstractions.Backup.IBackupVault"/> already handles
/// for a different purpose. A module needing larger objects has a real streaming requirement and
/// should be pointed at that port instead of raising this constant.
/// </remarks>
internal static class DocumentSizeLimit
{
    /// <summary>The limit, in bytes.</summary>
    public const int MaxBytes = 25 * 1024 * 1024;
}

/// <summary>Attaches a new document to a business record.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="EntityType">What kind of business record this belongs to.</param>
/// <param name="EntityId">The business record's id.</param>
/// <param name="Kind">What sort of document this is.</param>
/// <param name="Title">A human title.</param>
/// <param name="ContentType">The MIME type of the bytes.</param>
/// <param name="Content">The bytes.</param>
/// <param name="StoreId">The store, where the business record has one.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AttachDocumentCommand(
    string Module,
    string EntityType,
    Guid EntityId,
    string Kind,
    string Title,
    string ContentType,
    byte[] Content,
    Guid? StoreId = null) : ICommand<DocumentVersionSummary>;

/// <summary>Rejects a malformed attachment before it reaches the handler.</summary>
public sealed class AttachDocumentCommandValidator : AbstractValidator<AttachDocumentCommand>
{
    /// <summary>Builds the rules.</summary>
    public AttachDocumentCommandValidator()
    {
        RuleFor(command => command.Module).NotEmpty().MaximumLength(64);
        RuleFor(command => command.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Kind).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Title).NotEmpty().MaximumLength(Domain.Workflow.Document.MaxTitleLength);
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Content).NotEmpty()
            .Must(content => content.Length <= DocumentSizeLimit.MaxBytes)
            .WithMessage($"A document may not exceed {DocumentSizeLimit.MaxBytes:N0} bytes.");
    }
}

/// <summary>Attaches the document through the document service.</summary>
/// <param name="documentService">Generation, attachment and versioning.</param>
public sealed class AttachDocumentCommandHandler(IDocumentService documentService)
    : ICommandHandler<AttachDocumentCommand, DocumentVersionSummary>
{
    /// <inheritdoc />
    public async Task<DocumentVersionSummary> HandleAsync(
        AttachDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Genuinely awaited, not just returned from inside the using block: DocumentService.AttachAsync
        // reads this stream after its own first await (Document.Create has no I/O, but the version
        // this stage first exercised over a real database showed it clearly on AttachVersionAsync,
        // which awaits a database lookup before it ever reads the stream). A using block that returns
        // the inner Task without awaiting it disposes the stream the moment control yields — the
        // continuation then reads a closed MemoryStream. Both attach handlers had the same shape; both
        // are fixed here.
        using MemoryStream content = new(command.Content, writable: false);

        return await documentService.AttachAsync(
            command.Module,
            command.EntityType,
            command.EntityId,
            command.Kind,
            command.Title,
            command.ContentType,
            content,
            command.StoreId,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Attaches a new version to an existing document.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="ContentType">The MIME type of the bytes.</param>
/// <param name="Content">The bytes.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AttachDocumentVersionCommand(Guid DocumentId, string ContentType, byte[] Content)
    : ICommand<DocumentVersionSummary>;

/// <summary>Rejects a malformed version before it reaches the handler.</summary>
public sealed class AttachDocumentVersionCommandValidator : AbstractValidator<AttachDocumentVersionCommand>
{
    /// <summary>Builds the rules.</summary>
    public AttachDocumentVersionCommandValidator()
    {
        RuleFor(command => command.DocumentId).NotEmpty();
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Content).NotEmpty()
            .Must(content => content.Length <= DocumentSizeLimit.MaxBytes)
            .WithMessage($"A document may not exceed {DocumentSizeLimit.MaxBytes:N0} bytes.");
    }
}

/// <summary>Attaches the version through the document service.</summary>
/// <param name="documentService">Generation, attachment and versioning.</param>
public sealed class AttachDocumentVersionCommandHandler(IDocumentService documentService)
    : ICommandHandler<AttachDocumentVersionCommand, DocumentVersionSummary>
{
    /// <inheritdoc />
    public async Task<DocumentVersionSummary> HandleAsync(
        AttachDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // See the note on AttachDocumentCommandHandler: genuinely awaited, so the using block does not
        // dispose the stream before DocumentService.AttachVersionAsync reads it — this handler is the
        // one where the bug was actually observed, because FindDocumentAsync's database round trip is
        // a real await that yields control before the stream is ever touched.
        using MemoryStream content = new(command.Content, writable: false);

        return await documentService
            .AttachVersionAsync(command.DocumentId, command.ContentType, content, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Renders and attaches a generated document.</summary>
/// <param name="Module">The owning module.</param>
/// <param name="EntityType">What kind of business record this belongs to.</param>
/// <param name="EntityId">The business record's id.</param>
/// <param name="Kind">What sort of document this is.</param>
/// <param name="Title">A human title.</param>
/// <param name="TemplateKind">Which template to render.</param>
/// <param name="Model">The values the template fills in.</param>
/// <param name="ContentType">The MIME type the rendered document should carry.</param>
/// <param name="StoreId">The store, where the business record has one.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record GenerateDocumentCommand(
    string Module,
    string EntityType,
    Guid EntityId,
    string Kind,
    string Title,
    string TemplateKind,
    IReadOnlyDictionary<string, string> Model,
    string ContentType,
    Guid? StoreId = null) : ICommand<DocumentVersionSummary>;

/// <summary>Rejects a malformed generation request before it reaches the handler.</summary>
public sealed class GenerateDocumentCommandValidator : AbstractValidator<GenerateDocumentCommand>
{
    /// <summary>Builds the rules.</summary>
    public GenerateDocumentCommandValidator()
    {
        RuleFor(command => command.Module).NotEmpty().MaximumLength(64);
        RuleFor(command => command.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Kind).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Title).NotEmpty().MaximumLength(Domain.Workflow.Document.MaxTitleLength);
        RuleFor(command => command.TemplateKind).NotEmpty().MaximumLength(64);
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Generates the document through the document service.</summary>
/// <param name="documentService">Generation, attachment and versioning.</param>
public sealed class GenerateDocumentCommandHandler(IDocumentService documentService)
    : ICommandHandler<GenerateDocumentCommand, DocumentVersionSummary>
{
    /// <inheritdoc />
    public Task<DocumentVersionSummary> HandleAsync(
        GenerateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return documentService.GenerateAsync(
            command.Module,
            command.EntityType,
            command.EntityId,
            command.Kind,
            command.Title,
            new DocumentRenderRequest(command.TemplateKind, command.Model, command.ContentType),
            command.StoreId,
            cancellationToken);
    }
}
