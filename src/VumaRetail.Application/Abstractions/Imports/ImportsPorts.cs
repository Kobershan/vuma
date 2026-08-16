using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Abstractions.Imports;

/// <summary>
/// One field a target accepts, described well enough for a screen to build a mapping UI from it and
/// for auto-mapping to bind it without a human.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Aliases"/> is the difference between an import feature people use and one they abandon.
/// A supplier writes <c>Item Code</c>, another writes <c>SKU</c>, another writes <c>Stock Code</c>,
/// and all three mean the field this codebase calls <c>sku</c>. Carrying the aliases with the field —
/// rather than in a lookup table somewhere else — means adding a target field and teaching the
/// matcher about it are the same edit.
/// </para>
/// <para>
/// <see cref="Example"/> is not decoration: the commonest import failure is a person binding the
/// right column to a field whose format they guessed wrong, and an example next to the field is what
/// stops it.
/// </para>
/// </remarks>
/// <param name="Name">The field's canonical name, as a mapping binds it.</param>
/// <param name="Type">What a cell is parsed into.</param>
/// <param name="IsRequired">True when a batch cannot be mapped without binding it.</param>
/// <param name="Description">What the field means, in a sentence a shopkeeper would read.</param>
/// <param name="Example">A well-formed value.</param>
/// <param name="Aliases">Header texts that mean this field, matched case- and space-insensitively.</param>
public sealed record ImportFieldDescriptor(
    string Name,
    ImportFieldType Type,
    bool IsRequired,
    string Description,
    string Example,
    IReadOnlyList<string> Aliases)
{
    /// <summary>True when a source header means this field.</summary>
    /// <param name="header">The header text out of the file.</param>
    /// <remarks>
    /// Normalised on both sides by stripping everything that is not a letter or a digit, so
    /// <c>"Unit Price"</c>, <c>"unit_price"</c> and <c>"UNIT-PRICE"</c> are one header.
    /// </remarks>
    public bool MatchesHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        string normalised = Normalise(header);

        return Normalise(Name) == normalised
            || Aliases.Any(alias => Normalise(alias) == normalised);
    }

    /// <summary>Strips a header down to its letters and digits, lower-cased.</summary>
    /// <param name="value">The header.</param>
    /// <returns>The comparable form.</returns>
    public static string Normalise(string value)
        => new([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}

/// <summary>What one target accepts, and what it is called on a screen.</summary>
/// <param name="Kind">The target.</param>
/// <param name="Name">Its name, for a person.</param>
/// <param name="Description">What importing into it does.</param>
/// <param name="RequiresStore">
/// True when a batch aimed at this target must name a store — stock is counted somewhere, prices are
/// not necessarily.
/// </param>
/// <param name="NaturalKeyFields">
/// The fields that together identify one record — what a duplicate is matched on, both against the
/// tenant's existing data and against the rest of the same file.
/// </param>
/// <param name="Fields">The field catalogue.</param>
/// <remarks>
/// <see cref="NaturalKeyFields"/> is declared rather than inferred from which fields are required.
/// The two coincide for partners and items and come apart immediately for stock, whose quantity is
/// required and is emphatically not part of what identifies a row — inferring it would mean two
/// people counting the same shelf differently went unnoticed, which is the single thing a stocktake
/// import must catch. It is also on the wire, so a mapping screen can tell somebody what their rows
/// will be matched on before they commit.
/// </remarks>
public sealed record ImportTargetDescriptor(
    ImportTargetKind Kind,
    string Name,
    string Description,
    bool RequiresStore,
    IReadOnlyList<string> NaturalKeyFields,
    IReadOnlyList<ImportFieldDescriptor> Fields);

/// <summary>A file, read into headers and rows, before any mapping has happened.</summary>
/// <param name="Headers">The header row, in file order.</param>
/// <param name="Rows">The data rows, in file order.</param>
public sealed record ImportSheet(IReadOnlyList<string> Headers, IReadOnlyList<ImportSourceRow> Rows);

/// <summary>One raw row out of a file.</summary>
/// <param name="RowNumber">Its line number, header counted as 1.</param>
/// <param name="Values">Its cells, keyed by header.</param>
public sealed record ImportSourceRow(int RowNumber, IReadOnlyDictionary<string, string?> Values);

/// <summary>How to read a particular file.</summary>
/// <param name="Worksheet">The worksheet, for a multi-sheet workbook. First sheet when <c>null</c>.</param>
/// <param name="Delimiter">The CSV delimiter. Detected from the header row when <c>null</c>.</param>
/// <param name="MaxRows">The ceiling on data rows, above which the read is refused.</param>
public sealed record ImportReadOptions(string? Worksheet = null, char? Delimiter = null, int MaxRows = 50_000);

/// <summary>Turns uploaded bytes into headers and rows. One implementation per format.</summary>
/// <remarks>
/// Deliberately knows nothing about targets, mapping or validation — a reader's whole job is "what
/// does this file say", and keeping it that narrow is what lets Stage 12's scheduled supplier feed
/// and Stage 21b's Connect proposals reuse it without inheriting an import batch.
/// </remarks>
public interface IImportSourceReader
{
    /// <summary>The format this reader handles.</summary>
    ImportSourceFormat Format { get; }

    /// <summary>Reads a file.</summary>
    /// <param name="content">The uploaded bytes. Not disposed by the reader.</param>
    /// <param name="options">How to read it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The headers and the rows.</returns>
    /// <exception cref="ImportSourceException">The bytes are not readable as this format.</exception>
    Task<ImportSheet> ReadAsync(
        Stream content, ImportReadOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Picks the reader for a format.</summary>
public interface IImportSourceReaderFactory
{
    /// <summary>The reader for a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="NotSupportedException">No reader is registered for that format.</exception>
    IImportSourceReader For(ImportSourceFormat format);
}

/// <summary>
/// Extracts text from a page image. The seam OCR would arrive behind.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §4 names Tesseract as the fallback for scanned PDFs. Tesseract needs a native
/// library and a trained data file, neither of which this build can acquire, so the interface exists
/// and the only implementation refuses (<c>PROGRESS.md</c>, "Deferred"). It is an interface rather
/// than a hole so that adding OCR later is a registration, not a change to the PDF reader.
/// </remarks>
public interface IOcrTextExtractor
{
    /// <summary>True when this extractor can actually do the work.</summary>
    bool IsAvailable { get; }

    /// <summary>Extracts text from a page image.</summary>
    /// <param name="pageImage">The rendered page.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The text.</returns>
    Task<string> ExtractAsync(ReadOnlyMemory<byte> pageImage, CancellationToken cancellationToken = default);
}

/// <summary>What a target handler did with one row.</summary>
/// <param name="Outcome">What it did.</param>
/// <param name="TargetEntityId">The entity it did it to.</param>
/// <param name="BeforeImage">
/// The entity's prior state as JSON, for an <see cref="ImportRowOutcome.Updated"/> row. The handler
/// chooses the shape and is the only thing that reads it back.
/// </param>
public sealed record ImportApplyResult(
    ImportRowOutcome Outcome, Guid TargetEntityId, string? BeforeImage)
{
    /// <summary>A row that created something.</summary>
    /// <param name="entityId">What it created.</param>
    public static ImportApplyResult Created(Guid entityId)
        => new(ImportRowOutcome.Created, entityId, null);

    /// <summary>A row that changed something that already existed.</summary>
    /// <param name="entityId">What it changed.</param>
    /// <param name="beforeImage">Its prior state, as JSON.</param>
    public static ImportApplyResult Updated(Guid entityId, string beforeImage)
        => new(ImportRowOutcome.Updated, entityId, beforeImage);

    /// <summary>A row that posted an append-only movement.</summary>
    /// <param name="entryId">The entry it posted.</param>
    public static ImportApplyResult Movement(Guid entryId)
        => new(ImportRowOutcome.Movement, entryId, null);
}

/// <summary>Why a row cannot be compensated, when it cannot.</summary>
/// <param name="RowNumber">The source row.</param>
/// <param name="Reason">What now depends on it.</param>
public sealed record ImportRollbackObstacle(int RowNumber, string Reason);

/// <summary>
/// Turns validated rows into business records, and — just as importantly — turns them back again.
/// </summary>
/// <remarks>
/// <para>
/// <b>A handler writes through the domain, never through the DbContext (ADR-079).</b> It builds
/// entities with the same <c>Create</c> factories the command handlers use and saves them through the
/// same repositories, so every invariant that protects the API protects the import. It does not call
/// another command's handler — <c>CONVENTIONS.md</c> §4 — because a handler that calls a handler is
/// how two transactions and two validation passes end up inside one command.
/// </para>
/// <para>
/// The three methods are the three phases and they are deliberately separate:
/// <see cref="ValidateAsync"/> may not write, <see cref="ApplyAsync"/> is the only one that may, and
/// <see cref="CanCompensateAsync"/> answers before <see cref="CompensateAsync"/> touches anything —
/// which is what makes ADR-076's all-or-nothing rollback possible rather than aspirational.
/// </para>
/// </remarks>
public interface IImportTargetHandler
{
    /// <summary>What this handler imports into.</summary>
    ImportTargetKind Kind { get; }

    /// <summary>What it accepts, and what to call it.</summary>
    ImportTargetDescriptor Descriptor { get; }

    /// <summary>
    /// Checks one row against the tenant's actual data — does the unit of measure exist, is the SKU
    /// already taken — on top of the structural parse the pipeline has already done.
    /// </summary>
    /// <param name="values">The row's mapped, parsed values, keyed by target field.</param>
    /// <param name="context">The batch's store, duplicate strategy and currency.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every semantic error, and whether the row would create, update or be skipped.</returns>
    Task<ImportSemanticVerdict> ValidateAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one row. The only method that writes outside the <c>imports</c> schema.</summary>
    /// <param name="values">The row's mapped, parsed values.</param>
    /// <param name="context">The batch's store, duplicate strategy and currency.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What it did, so the row can record it.</returns>
    Task<ImportApplyResult> ApplyAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks whether a committed row could be compensated, without compensating it.
    /// </summary>
    /// <param name="row">The committed row.</param>
    /// <param name="context">The batch's context.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>null</c> when it can, or what is in the way.</returns>
    Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default);

    /// <summary>Compensates a committed row (ADR-076).</summary>
    /// <param name="row">The committed row.</param>
    /// <param name="context">The batch's context.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The compensating entity, for a movement; <c>null</c> otherwise.</returns>
    Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default);
}

/// <summary>What a target handler concluded about one row's semantics.</summary>
/// <param name="Errors">Every semantic error; empty means the row would apply.</param>
/// <param name="WouldSkip">
/// True when the row matches something that exists and the batch says <c>Skip</c>. Not an error — the
/// preview shows it as a skip so the person sees how much of their file is a no-op before committing.
/// </param>
/// <param name="ExistingEntityId">What it matched, when it matched something.</param>
public sealed record ImportSemanticVerdict(
    IReadOnlyList<ImportRowError> Errors, bool WouldSkip, Guid? ExistingEntityId)
{
    /// <summary>A row that would create something new.</summary>
    public static ImportSemanticVerdict WouldCreate() => new([], false, null);

    /// <summary>A row that would change something that exists.</summary>
    /// <param name="existingEntityId">What it would change.</param>
    public static ImportSemanticVerdict WouldUpdate(Guid existingEntityId)
        => new([], false, existingEntityId);

    /// <summary>A row that exists already and will be left alone.</summary>
    /// <param name="existingEntityId">What it matched.</param>
    public static ImportSemanticVerdict Skip(Guid existingEntityId) => new([], true, existingEntityId);

    /// <summary>A row that will not apply.</summary>
    /// <param name="errors">Why not.</param>
    public static ImportSemanticVerdict Invalid(IReadOnlyList<ImportRowError> errors)
        => new(errors, false, null);
}

/// <summary>The batch's standing instructions, handed to every handler call.</summary>
/// <param name="BatchId">The batch, so a movement can reference the document that caused it.</param>
/// <param name="BatchNumber">Its number, for a ledger reference a person can follow back.</param>
/// <param name="StoreId">The store, where the target is store-scoped.</param>
/// <param name="DuplicateStrategy">What to do about a row that already exists.</param>
/// <param name="Currency">
/// The currency money fields are denominated in, resolved from the store or tenant by the caller —
/// never taken from the file, which is how §4.13 bricked a terminal.
/// </param>
public sealed record ImportContext(
    Guid BatchId,
    string BatchNumber,
    Guid? StoreId,
    ImportDuplicateStrategy DuplicateStrategy,
    string Currency);

/// <summary>
/// Answers the one question rollback rule 6 turns on: has anything happened to what this import
/// created?
/// </summary>
/// <remarks>
/// <para>
/// A port, and an unusual one: it is the only thing in this module that deliberately looks across
/// module boundaries. It has to. "Can this imported item be removed" is answered by the sales, POS,
/// inventory and finance schemas, and no single module's repository can see them all.
/// </para>
/// <para>
/// It is safe because of what it may do, not because of where it sits: it <b>reads</b>, it returns a
/// sentence rather than a row, and it names no other module's types in its signature. The one
/// implementation lives in Infrastructure, which already composes every schema, and it is the single
/// place a later stage adds "and the purchase orders" to (<c>CONVENTIONS.md</c> §2).
/// </para>
/// <para>
/// Each method returns a phrase for the refusal message — <c>"sold on sale POS-000123"</c> — or
/// <c>null</c> when nothing points at the entity. A phrase rather than a boolean because
/// <c>IMPORTS_ROLLBACK_BLOCKED</c> has to tell somebody <em>what</em> is in the way; "no" on its own
/// leaves them with a refusal and no next step.
/// </para>
/// </remarks>
public interface IImportUsageProbe
{
    /// <summary>What now references a partner, or <c>null</c> when nothing does.</summary>
    /// <param name="partnerId">The partner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<string?> DescribePartnerUsageAsync(Guid partnerId, CancellationToken cancellationToken = default);

    /// <summary>What now references an item, or <c>null</c> when nothing does.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Stock movements this same import posted are not usage — an import that created an item and
    /// opened its stock is one document, and treating its own second half as a blocker would make
    /// every combined file unrollbackable. The implementation excludes ledger entries whose reference
    /// is an import batch.
    /// </remarks>
    Task<string?> DescribeItemUsageAsync(Guid itemId, CancellationToken cancellationToken = default);
}

/// <summary>Finds the handler for a target.</summary>
public interface IImportTargetHandlerFactory
{
    /// <summary>The handler for a target kind.</summary>
    /// <param name="kind">The target.</param>
    /// <returns>The handler.</returns>
    /// <exception cref="NotSupportedException">No handler is registered for that target.</exception>
    IImportTargetHandler For(ImportTargetKind kind);

    /// <summary>Every registered target's catalogue, for the mapping UI.</summary>
    IReadOnlyList<ImportTargetDescriptor> Descriptors { get; }
}

/// <summary>Reads and writes <see cref="ImportBatch"/> aggregates.</summary>
public interface IImportBatchRepository
{
    /// <summary>Finds a batch with its rows and mappings loaded, or <c>null</c>.</summary>
    /// <param name="batchId">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ImportBatch?> FindAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a batch with its rows and mappings loaded and a row lock held, for a commit or rollback.
    /// </summary>
    /// <param name="batchId">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The locking read is what makes "a batch commits once" (business rule 4) true under two
    /// concurrent callers rather than merely likely. An unlocked read lets both see
    /// <c>Validated</c> and both apply their rows.
    /// </remarks>
    Task<ImportBatch?> FindForUpdateAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>Finds the batch that already committed these exact bytes, or <c>null</c>.</summary>
    /// <param name="contentHash">Lower-case hex SHA-256.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ImportBatch?> FindCommittedByContentHashAsync(
        string contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// A keyset page of batches ordered by number, then id. See <c>docs/API_STANDARDS.md</c> §8.
    /// </summary>
    /// <param name="targetKind">The target, or <c>null</c> for all.</param>
    /// <param name="status">The status, or <c>null</c> for all.</param>
    /// <param name="after">The cursor of the last row the caller already has, or <c>null</c>.</param>
    /// <param name="limit">How many rows, already clamped by the query handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Rows and mappings are deliberately <b>not</b> loaded here — a list of batches is a list of
    /// files, and eagerly loading fifty thousand rows per batch to render a table of twenty-five
    /// filenames is the mistake this remark exists to prevent.
    /// </remarks>
    Task<(IReadOnlyList<ImportBatch> Items, bool HasMore)> ListPageAsync(
        ImportTargetKind? targetKind,
        ImportBatchStatus? status,
        Application.Abstractions.KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A keyset page of one batch's rows, in file order — the preview.
    /// </summary>
    /// <param name="batchId">The batch.</param>
    /// <param name="status">Narrow to one row status, or <c>null</c> for all.</param>
    /// <param name="after">The cursor of the last row the caller already has, or <c>null</c>.</param>
    /// <param name="limit">How many rows, already clamped by the query handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Paged separately from the aggregate because a preview of a fifty-thousand-row file must not
    /// materialise fifty thousand rows to show somebody the first twenty-five.
    /// </remarks>
    Task<(IReadOnlyList<ImportRow> Items, bool HasMore)> ListRowsPageAsync(
        Guid batchId,
        ImportRowStatus? status,
        Application.Abstractions.KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new batch.</summary>
    /// <param name="batch">The batch.</param>
    void Add(ImportBatch batch);
}

/// <summary>Reads and writes <see cref="ImportMappingTemplate"/> aggregates.</summary>
public interface IImportMappingTemplateRepository
{
    /// <summary>Finds a template, or <c>null</c>.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ImportMappingTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>True when a template already uses that code in this tenant.</summary>
    /// <param name="code">The code, case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The active template matching a header signature and target, or <c>null</c>.</summary>
    /// <param name="targetKind">The target.</param>
    /// <param name="sourceSignature">The signature from <c>ImportMappingTemplate.SignatureOf</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ImportMappingTemplate?> FindMatchAsync(
        ImportTargetKind targetKind, string sourceSignature, CancellationToken cancellationToken = default);

    /// <summary>Every template, optionally narrowed to one target.</summary>
    /// <param name="targetKind">The target, or <c>null</c> for all.</param>
    /// <param name="includeInactive">True to include retired templates.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ImportMappingTemplate>> ListAsync(
        ImportTargetKind? targetKind,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new template.</summary>
    /// <param name="template">The template.</param>
    void Add(ImportMappingTemplate template);
}

/// <summary>Stores the uploaded bytes so a batch can be re-parsed without a second upload.</summary>
/// <remarks>
/// <para>
/// Separate from the batch row on purpose. A 40 MB workbook does not belong in a table that a list
/// endpoint pages over, and the bytes have a different lifetime from the record of what was imported:
/// the file can be dropped once the batch commits, while the batch and its rows are kept.
/// </para>
/// <para>
/// Not replicated. The store server is where an import happens and where the file was uploaded; the
/// cloud tier gets the batch, the rows and the outcome, which is what a head office asks about. R10
/// also applies — an uploaded customer list is exactly the sort of document that has no business
/// leaving the premises for vendor purposes.
/// </para>
/// </remarks>
public interface IImportFileStore
{
    /// <summary>Stores an upload.</summary>
    /// <param name="batchId">The batch the bytes belong to.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task StoreAsync(Guid batchId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Reads an upload back, or <c>null</c> when it has been dropped.</summary>
    /// <param name="batchId">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ReadOnlyMemory<byte>?> ReadAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>Drops an upload's bytes. The batch and its rows are untouched.</summary>
    /// <param name="batchId">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(Guid batchId, CancellationToken cancellationToken = default);
}
