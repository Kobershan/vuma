using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Imports;

/// <summary>Something imports-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>import batch</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class ImportNotFoundException(string what, Guid id)
    : DomainException("IMPORTS_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something imports-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class ImportConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A mapping template code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static ImportConflictException TemplateCode(string code)
        => new(
            "IMPORTS_TEMPLATE_CODE_TAKEN",
            $"Mapping template code '{code}' is already used in this tenant.");

    /// <summary>The same file has already been committed by another batch.</summary>
    /// <param name="fileName">The file that was uploaded.</param>
    /// <param name="batchNumber">The batch that already committed those exact bytes.</param>
    /// <remarks>
    /// Matched on the SHA-256 of the content, not on the name — the same sheet saved twice under two
    /// names is the same sheet, and re-importing it doubles somebody's opening stock.
    /// </remarks>
    public static ImportConflictException FileAlreadyCommitted(string fileName, string batchNumber)
        => new(
            "IMPORTS_FILE_ALREADY_COMMITTED",
            $"These exact bytes were already committed by batch {batchNumber} (uploaded as "
            + $"'{fileName}'). Re-importing an identical file is nearly always an accident; roll that "
            + "batch back first, or change the file if it genuinely is new data.");
}

/// <summary>The uploaded bytes could not be read as the format they were declared to be.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What went wrong with the file.</param>
/// <remarks>
/// <see cref="DomainProblemKind.Malformed"/> rather than <see cref="DomainProblemKind.Rule"/>: the
/// caller can fix this from the response by uploading a different file, which is the distinction
/// Stage 03's handler maps to a 400 rather than a 422.
/// </remarks>
public sealed class ImportSourceException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Malformed)
{
    /// <summary>The file had no rows under its header, or no content at all.</summary>
    public static ImportSourceException NoRows()
        => new("IMPORTS_SOURCE_HAS_NO_ROWS", "The file contains a header but no data rows.");

    /// <summary>Two columns in the source carry the same header.</summary>
    /// <param name="header">The header that appears more than once.</param>
    public static ImportSourceException DuplicateHeader(string header)
        => new(
            "IMPORTS_DUPLICATE_HEADER",
            $"The header '{header}' appears in more than one column. A mapping binds a target field "
            + "to one column, and two columns with one name make that ambiguous.");

    /// <summary>The PDF carries no extractable text — it is a scan, and OCR is deferred.</summary>
    public static ImportSourceException PdfHasNoTextLayer()
        => new(
            "IMPORTS_PDF_HAS_NO_TEXT_LAYER",
            "This PDF has no text layer, which means it is a scan or an image. Optical character "
            + "recognition is not available in this build. Export the document as CSV or Excel, or "
            + "supply a PDF produced by the supplier's system rather than by a scanner.");

    /// <summary>The bytes are not the format the batch declared.</summary>
    /// <param name="declared">The format the caller said it was.</param>
    /// <param name="detail">What actually failed, in words.</param>
    public static ImportSourceException Unreadable(ImportSourceFormat declared, string detail)
        => new(
            "IMPORTS_SOURCE_UNREADABLE",
            $"The upload could not be read as {declared}: {detail}");

    /// <summary>The named worksheet is not in the workbook.</summary>
    /// <param name="worksheet">The worksheet that was asked for.</param>
    public static ImportSourceException WorksheetNotFound(string worksheet)
        => new(
            "IMPORTS_WORKSHEET_NOT_FOUND",
            $"The workbook has no worksheet named '{worksheet}'.");
}

/// <summary>An imports business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class ImportRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>Something was done to a batch that its current status does not allow.</summary>
    /// <param name="status">The status the batch is actually in.</param>
    /// <param name="attempted">What was attempted, in words — <c>commit</c>, <c>map</c>.</param>
    public static ImportRuleException UnexpectedBatchStatus(ImportBatchStatus status, string attempted)
        => new(
            "IMPORTS_UNEXPECTED_BATCH_STATUS",
            $"A batch cannot be {attempted} while it is {status}.");

    /// <summary>A required target field was left unbound when the mapping was set.</summary>
    /// <param name="fields">The fields with no source column and no default.</param>
    public static ImportRuleException RequiredFieldsUnmapped(IReadOnlyCollection<string> fields)
        => new(
            "IMPORTS_REQUIRED_FIELDS_UNMAPPED",
            $"These target fields are required and are bound to nothing: {string.Join(", ", fields)}. "
            + "Bind each to a source column, or give it a constant default if the file genuinely does "
            + "not carry it.");

    /// <summary>A mapping named a source column the file does not have.</summary>
    /// <param name="column">The column that was named.</param>
    public static ImportRuleException UnknownSourceColumn(string column)
        => new(
            "IMPORTS_UNKNOWN_SOURCE_COLUMN",
            $"The file has no column called '{column}'.");

    /// <summary>A mapping named a target field this target kind does not have.</summary>
    /// <param name="field">The field that was named.</param>
    /// <param name="target">The target kind.</param>
    public static ImportRuleException UnknownTargetField(string field, ImportTargetKind target)
        => new(
            "IMPORTS_UNKNOWN_TARGET_FIELD",
            $"'{field}' is not a field of the {target} target. Ask "
            + "GET /api/v1/imports/targets for the catalogue.");

    /// <summary>Two mappings bound the same target field.</summary>
    /// <param name="field">The field bound twice.</param>
    public static ImportRuleException TargetFieldBoundTwice(string field)
        => new(
            "IMPORTS_TARGET_FIELD_BOUND_TWICE",
            $"The target field '{field}' is bound to more than one source column.");

    /// <summary>A commit was attempted on a batch with nothing valid on it.</summary>
    public static ImportRuleException NothingToCommit()
        => new(
            "IMPORTS_NOTHING_TO_COMMIT",
            "Every row on this batch is invalid, so a commit would write nothing. Fix the file, or "
            + "discard the batch.");

    /// <summary>
    /// A rollback could not be completed because something else now depends on what was imported.
    /// </summary>
    /// <param name="rowNumbers">The source rows that are blocked.</param>
    /// <param name="reason">Why they are blocked, in words.</param>
    /// <remarks>
    /// ADR-076: the whole rollback is refused rather than half-applied. A partial rollback leaves a
    /// posted document pointing at a soft-deleted entity, which is a worse state than not rolling
    /// back at all — and unlike this refusal, it is not one a person can undo.
    /// </remarks>
    public static ImportRuleException RollbackBlocked(IReadOnlyCollection<int> rowNumbers, string reason)
        => new(
            "IMPORTS_ROLLBACK_BLOCKED",
            $"This import cannot be rolled back: {reason}. Blocked source rows: "
            + $"{string.Join(", ", rowNumbers)}. Nothing has been changed — a rollback is all or "
            + "nothing, because a partial one would leave posted documents pointing at deleted "
            + "records.");

    /// <summary>A file was uploaded with no content.</summary>
    public static ImportRuleException EmptyUpload()
        => new("IMPORTS_EMPTY_UPLOAD", "The uploaded file is empty.");

    /// <summary>A file was uploaded that is larger than the configured ceiling.</summary>
    /// <param name="sizeBytes">How big it is.</param>
    /// <param name="limitBytes">The ceiling.</param>
    public static ImportRuleException UploadTooLarge(long sizeBytes, long limitBytes)
        => new(
            "IMPORTS_UPLOAD_TOO_LARGE",
            $"The upload is {sizeBytes:N0} bytes and the limit is {limitBytes:N0}. Split the file, or "
            + "raise Vuma:Imports:MaxUploadBytes if the store server has the memory for it.");

    /// <summary>A file has more rows than one batch may carry.</summary>
    /// <param name="rows">How many rows it has.</param>
    /// <param name="limit">The ceiling.</param>
    public static ImportRuleException TooManyRows(int rows, int limit)
        => new(
            "IMPORTS_TOO_MANY_ROWS",
            $"The file has {rows:N0} rows and one batch may carry {limit:N0}. Split it — a commit is "
            + "one transaction, and a transaction that large will hold locks a trading till needs.");
}
