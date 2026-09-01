using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Imports;

/// <summary>
/// One uploaded file, and the whole life of what it became — the aggregate root of the import
/// pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The batch is a state machine, and the transitions are the safety model rather than bookkeeping.
/// Nothing outside the <c>imports</c> schema is written before <see cref="Commit"/>: upload, parse,
/// map and validate all read the file and write only here. That is what makes the preview an honest
/// preview and not a report on damage already done (R5).
/// </para>
/// <para>
/// <b>The counters are derived, not maintained.</b> <see cref="ValidRows"/> and its neighbours are
/// recomputed from the rows on every transition rather than incremented as rows change, because a
/// counter that is incremented is a counter that drifts, and this one is what a person reads before
/// deciding to commit.
/// </para>
/// <para>
/// <see cref="ContentHash"/> is the SHA-256 of the uploaded bytes, and it exists to catch the single
/// most expensive import mistake there is: importing the same file twice and doubling somebody's
/// opening stock. Matching on content rather than on file name is the point — the same sheet saved
/// under two names is the same sheet.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ImportBatch : Entity
{
    private readonly List<ImportRow> _rows = [];
    private readonly List<ImportColumnMapping> _mappings = [];
    private readonly List<string> _sourceColumns = [];

    private ImportBatch(
        Guid tenantId,
        Guid? storeId,
        string batchNumber,
        ImportTargetKind targetKind,
        ImportSourceFormat sourceFormat,
        string fileName,
        string contentHash,
        long sizeBytes,
        ImportDuplicateStrategy duplicateStrategy)
        : base(tenantId, storeId)
    {
        BatchNumber = batchNumber;
        TargetKind = targetKind;
        SourceFormat = sourceFormat;
        FileName = fileName;
        ContentHash = contentHash;
        SizeBytes = sizeBytes;
        DuplicateStrategy = duplicateStrategy;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ImportBatch()
    {
    }

    /// <summary>The batch's human-facing number, from ADR-065's sequence, series <c>IMP</c>.</summary>
    public string BatchNumber { get; private set; } = string.Empty;

    /// <summary>What the rows are aimed at.</summary>
    public ImportTargetKind TargetKind { get; private set; }

    /// <summary>The format the file was uploaded as.</summary>
    public ImportSourceFormat SourceFormat { get; private set; }

    /// <summary>The name the file was uploaded under, for the person's benefit only.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the uploaded bytes.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>How big the upload was.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>What to do about rows that match something that already exists.</summary>
    public ImportDuplicateStrategy DuplicateStrategy { get; private set; }

    /// <summary>Where the batch stands.</summary>
    public ImportBatchStatus Status { get; private set; } = ImportBatchStatus.Uploaded;

    /// <summary>The optional worksheet name, for an Excel upload with more than one sheet.</summary>
    public string? Worksheet { get; private set; }

    /// <summary>When the batch was committed, UTC, or <c>null</c>.</summary>
    public DateTimeOffset? CommittedAt { get; private set; }

    /// <summary>Who committed it.</summary>
    public string? CommittedBy { get; private set; }

    /// <summary>When the batch was rolled back, UTC, or <c>null</c>.</summary>
    public DateTimeOffset? RolledBackAt { get; private set; }

    /// <summary>Who rolled it back.</summary>
    public string? RolledBackBy { get; private set; }

    /// <summary>Why it was rolled back — required, because a rollback undoes somebody else's work.</summary>
    public string? RollbackReason { get; private set; }

    /// <summary>How many data rows the file had.</summary>
    public int TotalRows { get; private set; }

    /// <summary>How many rows would apply cleanly.</summary>
    public int ValidRows { get; private set; }

    /// <summary>How many rows would not.</summary>
    public int InvalidRows { get; private set; }

    /// <summary>How many rows created something.</summary>
    public int CreatedRows { get; private set; }

    /// <summary>How many rows changed something that already existed.</summary>
    public int UpdatedRows { get; private set; }

    /// <summary>How many rows were deliberately left alone as duplicates.</summary>
    public int SkippedRows { get; private set; }

    /// <summary>The headers the reader found, in file order.</summary>
    public IReadOnlyList<string> SourceColumns => _sourceColumns;

    /// <summary>The rows.</summary>
    public IReadOnlyList<ImportRow> Rows => _rows;

    /// <summary>The field bindings.</summary>
    public IReadOnlyList<ImportColumnMapping> Mappings => _mappings;

    /// <summary>True once the batch has written outside the <c>imports</c> schema.</summary>
    public bool HasTouchedBusinessData
        => Status is ImportBatchStatus.Committed or ImportBatchStatus.RolledBack;

    /// <summary>Starts a batch from an upload that has been hashed but not yet read.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the import is for, where the target is store-scoped.</param>
    /// <param name="batchNumber">The number from ADR-065's sequence.</param>
    /// <param name="targetKind">What the rows are aimed at.</param>
    /// <param name="sourceFormat">The upload's format.</param>
    /// <param name="fileName">The name it was uploaded under.</param>
    /// <param name="contentHash">Lower-case hex SHA-256 of the bytes.</param>
    /// <param name="sizeBytes">How big the upload is.</param>
    /// <param name="duplicateStrategy">What to do about rows that already exist.</param>
    /// <param name="worksheet">The worksheet to read, for a multi-sheet workbook.</param>
    /// <returns>The new batch, <see cref="ImportBatchStatus.Uploaded"/>.</returns>
    /// <exception cref="ImportRuleException">The upload is empty.</exception>
    public static ImportBatch Create(
        Guid tenantId,
        Guid? storeId,
        string batchNumber,
        ImportTargetKind targetKind,
        ImportSourceFormat sourceFormat,
        string fileName,
        string contentHash,
        long sizeBytes,
        ImportDuplicateStrategy duplicateStrategy,
        string? worksheet = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An import batch must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(batchNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        if (sizeBytes <= 0)
        {
            throw ImportRuleException.EmptyUpload();
        }

        return new ImportBatch(
            tenantId,
            storeId,
            batchNumber.Trim(),
            targetKind,
            sourceFormat,
            fileName.Trim(),
            contentHash.Trim().ToLowerInvariant(),
            sizeBytes,
            duplicateStrategy)
        {
            Worksheet = string.IsNullOrWhiteSpace(worksheet) ? null : worksheet.Trim(),
        };
    }

    /// <summary>Records what the reader found, moving the batch to <see cref="ImportBatchStatus.Parsed"/>.</summary>
    /// <param name="sourceColumns">The headers, in file order.</param>
    /// <param name="rows">The data rows, in file order.</param>
    /// <exception cref="ImportRuleException">The batch has already moved past upload.</exception>
    /// <exception cref="ImportSourceException">The file has no rows, or a duplicated header.</exception>
    public void RecordParse(IReadOnlyList<string> sourceColumns, IReadOnlyList<ImportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(sourceColumns);
        ArgumentNullException.ThrowIfNull(rows);
        Require(ImportBatchStatus.Uploaded, "parsed");

        if (rows.Count == 0)
        {
            throw ImportSourceException.NoRows();
        }

        string? duplicate = sourceColumns
            .GroupBy(column => column, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (duplicate is not null)
        {
            throw ImportSourceException.DuplicateHeader(duplicate);
        }

        _sourceColumns.Clear();
        _sourceColumns.AddRange(sourceColumns);
        _rows.Clear();
        _rows.AddRange(rows);

        TotalRows = rows.Count;
        Status = ImportBatchStatus.Parsed;
        RecountRows();
    }

    /// <summary>
    /// Replaces the field bindings, moving the batch to <see cref="ImportBatchStatus.Mapped"/>.
    /// </summary>
    /// <param name="mappings">The bindings.</param>
    /// <param name="requiredFields">
    /// The target's required fields, supplied by the caller because the domain does not know the
    /// field catalogue — that lives with the target handler, which is Application's business.
    /// </param>
    /// <exception cref="ImportRuleException">
    /// The batch is past mapping, a target field is bound twice, a mapping names a column the file
    /// does not have, or a required field is bound to nothing.
    /// </exception>
    public void SetMapping(
        IReadOnlyList<ImportColumnMapping> mappings, IReadOnlyCollection<string> requiredFields)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(requiredFields);

        if (Status is not (ImportBatchStatus.Parsed or ImportBatchStatus.Mapped or ImportBatchStatus.Validated))
        {
            throw ImportRuleException.UnexpectedBatchStatus(Status, "mapped");
        }

        string? boundTwice = mappings
            .GroupBy(mapping => mapping.TargetField, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (boundTwice is not null)
        {
            throw ImportRuleException.TargetFieldBoundTwice(boundTwice);
        }

        foreach (ImportColumnMapping mapping in mappings)
        {
            if (mapping.SourceColumn is not null
                && !_sourceColumns.Contains(mapping.SourceColumn, StringComparer.OrdinalIgnoreCase))
            {
                throw ImportRuleException.UnknownSourceColumn(mapping.SourceColumn);
            }
        }

        string[] unmapped = requiredFields
            .Where(field => !mappings.Any(mapping
                => string.Equals(mapping.TargetField, field, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (unmapped.Length > 0)
        {
            throw ImportRuleException.RequiredFieldsUnmapped(unmapped);
        }

        _mappings.Clear();
        _mappings.AddRange(mappings);

        // Re-mapping invalidates every verdict: the rows were judged against the old bindings, and
        // leaving them Valid would let a commit apply a preview nobody ever saw.
        foreach (ImportRow row in _rows)
        {
            row.MarkInvalid([new ImportRowError(
                "IMPORTS_NOT_VALIDATED",
                null,
                "The mapping changed; this row has not been validated against it yet.")]);
        }

        Status = ImportBatchStatus.Mapped;
        RecountRows();
    }

    /// <summary>
    /// Records a validation pass, moving the batch to <see cref="ImportBatchStatus.Validated"/>.
    /// </summary>
    /// <param name="verdicts">
    /// One verdict per row, keyed by <see cref="ImportRow.RowNumber"/>. A row with no verdict is
    /// left invalid rather than assumed good.
    /// </param>
    /// <exception cref="ImportRuleException">The batch has not been mapped, or is past validation.</exception>
    public void RecordValidation(IReadOnlyDictionary<int, ImportRowVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        if (Status is not (ImportBatchStatus.Mapped or ImportBatchStatus.Validated))
        {
            throw ImportRuleException.UnexpectedBatchStatus(Status, "validated");
        }

        foreach (ImportRow row in _rows)
        {
            if (!verdicts.TryGetValue(row.RowNumber, out ImportRowVerdict? verdict))
            {
                row.MarkInvalid([new ImportRowError(
                    "IMPORTS_NOT_VALIDATED",
                    null,
                    "The validator returned no verdict for this row.")]);
                continue;
            }

            if (verdict.Errors.Count > 0)
            {
                row.MarkInvalid(verdict.Errors, verdict.NormalisedValues);
            }
            else if (verdict.WouldSkip)
            {
                // Marked here, at preview, and not left for the commit to discover. A duplicate row
                // counted as Valid tells somebody their 4,000-row supplier sheet will make 4,000
                // changes when 3,900 of them already exist and it will make a hundred — which is
                // precisely the preview being dishonest that business rule 1 exists to prevent.
                row.MarkSkipped(verdict.NormalisedValues);
            }
            else
            {
                row.MarkValid(verdict.NormalisedValues);
            }
        }

        Status = ImportBatchStatus.Validated;
        RecountRows();
    }

    /// <summary>
    /// Opens the commit, checking inside the caller's transaction that this batch has not already run.
    /// </summary>
    /// <exception cref="ImportRuleException">
    /// The batch is not validated, or every row on it is invalid.
    /// </exception>
    /// <remarks>
    /// Separate from <see cref="Commit"/> so the status check happens <em>before</em> the target
    /// handler writes anything, in the same transaction as the write. Two concurrent commits of one
    /// batch would otherwise both pass a check taken at the start and both apply their rows.
    /// </remarks>
    public void BeginCommit()
    {
        Require(ImportBatchStatus.Validated, "committed");

        // Refused only when the file is entirely broken, which is what the refusal has always said in
        // words. A file whose rows all already exist is not broken — it is a shop re-uploading last
        // month's supplier list, and the honest answer to that is a commit that applies nothing and
        // counters that say so, not a 422 reading as though the import failed.
        if (ValidRows == 0 && SkippedRows == 0)
        {
            throw ImportRuleException.NothingToCommit();
        }
    }

    /// <summary>Closes the commit once every valid row has been applied.</summary>
    /// <param name="at">When, UTC.</param>
    /// <param name="by">Who.</param>
    /// <exception cref="ImportRuleException">The batch is not validated.</exception>
    public void Commit(DateTimeOffset at, string by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        Require(ImportBatchStatus.Validated, "committed");

        CommittedAt = at;
        CommittedBy = by;
        Status = ImportBatchStatus.Committed;
        RecountRows();
    }

    /// <summary>Closes a rollback once every committed row has been compensated.</summary>
    /// <param name="at">When, UTC.</param>
    /// <param name="by">Who.</param>
    /// <param name="reason">Why — required; a rollback undoes work somebody else may be relying on.</param>
    /// <exception cref="ImportRuleException">The batch was never committed.</exception>
    public void RollBack(DateTimeOffset at, string by, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Require(ImportBatchStatus.Committed, "rolled back");

        RolledBackAt = at;
        RolledBackBy = by;
        RollbackReason = reason.Trim();
        Status = ImportBatchStatus.RolledBack;
        RecountRows();
    }

    /// <summary>Abandons a batch that has not committed. Nothing outside <c>imports</c> is touched.</summary>
    /// <exception cref="ImportRuleException">The batch has already written business data.</exception>
    public void Discard()
    {
        if (HasTouchedBusinessData)
        {
            throw ImportRuleException.UnexpectedBatchStatus(Status, "discarded");
        }

        Status = ImportBatchStatus.Discarded;
    }

    /// <summary>Finds a row by its source line number.</summary>
    /// <param name="rowNumber">The line number.</param>
    /// <returns>The row, or <c>null</c>.</returns>
    public ImportRow? FindRow(int rowNumber) => _rows.Find(row => row.RowNumber == rowNumber);

    /// <summary>Records what a commit did to one row.</summary>
    /// <param name="rowNumber">The source line number.</param>
    /// <param name="outcome">What it did.</param>
    /// <param name="targetEntityId">The entity it did it to.</param>
    /// <param name="beforeImage">The prior state, for an update.</param>
    /// <exception cref="ImportNotFoundException">No such row on this batch.</exception>
    public void RecordRowCommitted(
        int rowNumber, ImportRowOutcome outcome, Guid targetEntityId, string? beforeImage)
    {
        ImportRow row = FindRow(rowNumber)
            ?? throw new ImportNotFoundException($"import row {rowNumber} on this batch", Id);

        row.MarkCommitted(outcome, targetEntityId, beforeImage);
    }

    /// <summary>Records that a commit deliberately left one row alone.</summary>
    /// <param name="rowNumber">The source line number.</param>
    /// <exception cref="ImportNotFoundException">No such row on this batch.</exception>
    public void RecordRowSkipped(int rowNumber)
    {
        ImportRow row = FindRow(rowNumber)
            ?? throw new ImportNotFoundException($"import row {rowNumber} on this batch", Id);

        row.MarkSkipped();
    }

    /// <summary>Records that a rollback compensated one row.</summary>
    /// <param name="rowNumber">The source line number.</param>
    /// <param name="compensationEntityId">The reversing entry, for a movement.</param>
    /// <exception cref="ImportNotFoundException">No such row on this batch.</exception>
    public void RecordRowRolledBack(int rowNumber, Guid? compensationEntityId = null)
    {
        ImportRow row = FindRow(rowNumber)
            ?? throw new ImportNotFoundException($"import row {rowNumber} on this batch", Id);

        row.MarkRolledBack(compensationEntityId);
    }

    /// <summary>The rows a commit would apply, in file order.</summary>
    public IReadOnlyList<ImportRow> CommittableRows
        => _rows.Where(row => row.Status is ImportRowStatus.Valid).OrderBy(row => row.RowNumber).ToArray();

    /// <summary>The rows a rollback must compensate, in reverse file order.</summary>
    /// <remarks>
    /// Reverse order because a later row may depend on an earlier one — an item created on row 12 and
    /// priced on row 40 must lose its price before it loses its existence, or the compensation for
    /// row 40 runs against something already soft-deleted.
    /// </remarks>
    public IReadOnlyList<ImportRow> CompensatableRows
        => _rows
            .Where(row => row.Status is ImportRowStatus.Committed
                && row.Outcome is not ImportRowOutcome.None)
            .OrderByDescending(row => row.RowNumber)
            .ToArray();

    private void RecountRows()
    {
        TotalRows = _rows.Count;
        ValidRows = _rows.Count(row => row.Status is ImportRowStatus.Valid);
        InvalidRows = _rows.Count(row => row.Status is ImportRowStatus.Invalid);
        CreatedRows = _rows.Count(row => row.Status is ImportRowStatus.Committed
            && row.Outcome is ImportRowOutcome.Created or ImportRowOutcome.Movement);
        UpdatedRows = _rows.Count(row => row.Status is ImportRowStatus.Committed
            && row.Outcome is ImportRowOutcome.Updated);
        SkippedRows = _rows.Count(row => row.Status is ImportRowStatus.Skipped);
    }

    private void Require(ImportBatchStatus expected, string attempted)
    {
        if (Status != expected)
        {
            throw ImportRuleException.UnexpectedBatchStatus(Status, attempted);
        }
    }
}

/// <summary>What a validator concluded about one row.</summary>
/// <param name="NormalisedValues">The mapped, parsed values, keyed by target field.</param>
/// <param name="Errors">Every reason the row will not import; empty means it will.</param>
/// <param name="WouldSkip">
/// True when the row matches something that already exists and the batch's duplicate strategy is
/// <see cref="ImportDuplicateStrategy.Skip"/>. Not an error, and not valid either — carried as its own
/// answer because those are the only three things a preview can honestly say about a row.
/// </param>
public sealed record ImportRowVerdict(
    IReadOnlyDictionary<string, string?> NormalisedValues,
    IReadOnlyList<ImportRowError> Errors,
    bool WouldSkip = false)
{
    /// <summary>A row that will import cleanly.</summary>
    /// <param name="normalisedValues">The parsed values.</param>
    public static ImportRowVerdict Valid(IReadOnlyDictionary<string, string?> normalisedValues)
        => new(normalisedValues, []);

    /// <summary>A row that already exists and will be left alone.</summary>
    /// <param name="normalisedValues">The parsed values.</param>
    public static ImportRowVerdict Skip(IReadOnlyDictionary<string, string?> normalisedValues)
        => new(normalisedValues, [], WouldSkip: true);
}
