using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Imports;

/// <summary>
/// One row read out of the uploaded file, and everything that later happened to it.
/// </summary>
/// <remarks>
/// <para>
/// This is the table the module is really built around. It is the preview (its status and errors),
/// it is the audit trail (what it produced), and it is the rollback (<see cref="BeforeImage"/>) —
/// and it is one row per source row, so every answer the module gives can name a line number in a
/// file somebody is looking at.
/// </para>
/// <para>
/// <b>Two value dictionaries, deliberately.</b> <see cref="RawValues"/> is exactly what came out of
/// the file, keyed by source column, never touched again — so a support call can ask "what did the
/// file actually say" a year later. <see cref="NormalisedValues"/> is keyed by <em>target field</em>
/// and holds the value after mapping and parsing, in invariant canonical form. The split is what
/// stops a locale bug being invisible: <c>"1 234,56"</c> stays in <see cref="RawValues"/> as it was
/// written and becomes <c>"1234.56"</c> in <see cref="NormalisedValues"/>, and the conversion happens
/// once, at validation, where it can be reported as a row error (<c>CONVENTIONS.md</c> §6).
/// </para>
/// <para>
/// Rows are never mutated by anything outside their batch: <see cref="ImportBatch"/> owns them and
/// every state change here is called from there. That is what keeps the batch's counters and the
/// rows' statuses from disagreeing.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ImportRow : Entity
{
    private readonly List<ImportRowError> _errors = [];
    private readonly Dictionary<string, string?> _rawValues = [];
    private readonly Dictionary<string, string?> _normalisedValues = [];

    private ImportRow(
        Guid tenantId,
        Guid? storeId,
        Guid importBatchId,
        int rowNumber,
        IReadOnlyDictionary<string, string?> rawValues)
        : base(tenantId, storeId)
    {
        ImportBatchId = importBatchId;
        RowNumber = rowNumber;

        foreach ((string column, string? value) in rawValues)
        {
            _rawValues[column] = value;
        }
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ImportRow()
    {
    }

    /// <summary>The batch this row belongs to.</summary>
    public Guid ImportBatchId { get; private set; }

    /// <summary>
    /// The row's line number in the source, 1-based and counting the header as row 1.
    /// </summary>
    /// <remarks>
    /// Counting the header means this number is the one the person's spreadsheet shows them, which is
    /// the entire point of reporting a row number. An off-by-one here costs somebody twenty minutes
    /// looking at the wrong line.
    /// </remarks>
    public int RowNumber { get; private set; }

    /// <summary>Where this row stands.</summary>
    public ImportRowStatus Status { get; private set; } = ImportRowStatus.Pending;

    /// <summary>What the commit did to the target module.</summary>
    public ImportRowOutcome Outcome { get; private set; } = ImportRowOutcome.None;

    /// <summary>The entity this row created, updated or moved, once it has been committed.</summary>
    public Guid? TargetEntityId { get; private set; }

    /// <summary>
    /// The compensating entity a rollback wrote — the reversing stock ledger entry, for a
    /// <see cref="ImportRowOutcome.Movement"/> row. <c>null</c> for every other outcome.
    /// </summary>
    public Guid? CompensationEntityId { get; private set; }

    /// <summary>
    /// The state of the target entity before this row changed it, as JSON, or <c>null</c> when the row
    /// created rather than updated.
    /// </summary>
    /// <remarks>
    /// Written by the target handler, read only by the same target handler on rollback — the shape is
    /// the handler's business and is deliberately opaque here. Storing it as text rather than as a
    /// typed graph is what lets a later stage change an entity's shape without invalidating the
    /// before-images of imports already on disk; the handler is responsible for reading its own old
    /// shapes back.
    /// </remarks>
    public string? BeforeImage { get; private set; }

    /// <summary>Exactly what the file said, keyed by source column.</summary>
    public IReadOnlyDictionary<string, string?> RawValues => _rawValues;

    /// <summary>The mapped, parsed, invariant-form values, keyed by target field.</summary>
    public IReadOnlyDictionary<string, string?> NormalisedValues => _normalisedValues;

    /// <summary>Why this row is invalid. Empty for every other status.</summary>
    public IReadOnlyList<ImportRowError> Errors => _errors;

    /// <summary>Creates a pending row from what the reader produced.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the batch has one.</param>
    /// <param name="importBatchId">The batch.</param>
    /// <param name="rowNumber">The source line number, header counted as 1.</param>
    /// <param name="rawValues">The cells, keyed by source column header.</param>
    /// <returns>The new row.</returns>
    public static ImportRow Create(
        Guid tenantId,
        Guid? storeId,
        Guid importBatchId,
        int rowNumber,
        IReadOnlyDictionary<string, string?> rawValues)
    {
        ArgumentNullException.ThrowIfNull(rawValues);

        if (rowNumber < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowNumber),
                rowNumber,
                "Row 1 is the header; data rows start at 2.");
        }

        return new ImportRow(tenantId, storeId, importBatchId, rowNumber, rawValues);
    }

    /// <summary>Records that this row passed validation, with the values it parsed to.</summary>
    /// <param name="normalisedValues">The mapped, parsed values, keyed by target field.</param>
    internal void MarkValid(IReadOnlyDictionary<string, string?> normalisedValues)
    {
        ArgumentNullException.ThrowIfNull(normalisedValues);

        ReplaceNormalised(normalisedValues);
        _errors.Clear();
        Status = ImportRowStatus.Valid;
    }

    /// <summary>Records that this row failed validation, and why.</summary>
    /// <param name="errors">Every reason, not just the first — one round trip should fix the row.</param>
    /// <param name="normalisedValues">
    /// Whatever did parse, so the preview can still show the person the fields that were fine.
    /// </param>
    internal void MarkInvalid(
        IReadOnlyCollection<ImportRowError> errors,
        IReadOnlyDictionary<string, string?>? normalisedValues = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("An invalid row must say why.", nameof(errors));
        }

        if (normalisedValues is not null)
        {
            ReplaceNormalised(normalisedValues);
        }

        _errors.Clear();
        _errors.AddRange(errors);
        Status = ImportRowStatus.Invalid;
    }

    /// <summary>Records that the commit applied this row.</summary>
    /// <param name="outcome">What it did.</param>
    /// <param name="targetEntityId">The entity it did it to.</param>
    /// <param name="beforeImage">
    /// The entity's prior state as JSON, required for <see cref="ImportRowOutcome.Updated"/> and
    /// meaningless for the others.
    /// </param>
    /// <exception cref="ArgumentException">An update was recorded with no before-image.</exception>
    internal void MarkCommitted(ImportRowOutcome outcome, Guid targetEntityId, string? beforeImage)
    {
        if (outcome is ImportRowOutcome.Updated && string.IsNullOrWhiteSpace(beforeImage))
        {
            // Without this the row is committed and unrollbackable, and nothing would notice until
            // somebody tried to roll back — by which time the prior state is gone for good.
            throw new ArgumentException(
                "An updated row must carry the before-image its rollback restores from.",
                nameof(beforeImage));
        }

        Outcome = outcome;
        TargetEntityId = targetEntityId;
        BeforeImage = beforeImage;
        Status = ImportRowStatus.Committed;
    }

    /// <summary>Records that the commit deliberately left this row alone.</summary>
    internal void MarkSkipped()
    {
        Outcome = ImportRowOutcome.None;
        Status = ImportRowStatus.Skipped;
    }

    /// <summary>Records that validation found this row already exists and it will be left alone.</summary>
    /// <param name="normalisedValues">The mapped, parsed values, so the preview can still show them.</param>
    /// <remarks>
    /// The same status the commit sets, reached one step earlier. It carries the parsed values because
    /// a skipped row is still shown in the preview and a person looking at it wants to see what the
    /// file said, not an empty line.
    /// </remarks>
    internal void MarkSkipped(IReadOnlyDictionary<string, string?> normalisedValues)
    {
        ArgumentNullException.ThrowIfNull(normalisedValues);

        ReplaceNormalised(normalisedValues);
        _errors.Clear();
        MarkSkipped();
    }

    /// <summary>Records that a rollback has compensated this row.</summary>
    /// <param name="compensationEntityId">
    /// The reversing entry, for a <see cref="ImportRowOutcome.Movement"/>; <c>null</c> otherwise.
    /// </param>
    internal void MarkRolledBack(Guid? compensationEntityId = null)
    {
        CompensationEntityId = compensationEntityId;
        Status = ImportRowStatus.RolledBack;
    }

    private void ReplaceNormalised(IReadOnlyDictionary<string, string?> values)
    {
        _normalisedValues.Clear();

        foreach ((string field, string? value) in values)
        {
            _normalisedValues[field] = value;
        }
    }
}

/// <summary>
/// One reason a row will not import.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is the stable contract (<c>CONVENTIONS.md</c> §5) and <see cref="Field"/> is
/// what lets a screen put the message next to the cell rather than at the top of the page.
/// </remarks>
/// <param name="Code">The stable machine-readable code.</param>
/// <param name="Field">The target field at fault, or <c>null</c> when the whole row is.</param>
/// <param name="Message">The human-readable explanation.</param>
public sealed record ImportRowError(string Code, string? Field, string Message)
{
    /// <summary>A required field's cell was empty.</summary>
    /// <param name="field">The field.</param>
    public static ImportRowError Required(string field)
        => new("IMPORTS_FIELD_REQUIRED", field, $"'{field}' is required and this row's cell is empty.");

    /// <summary>A cell would not parse as the type its field declares.</summary>
    /// <param name="field">The field.</param>
    /// <param name="type">The type it should have been.</param>
    /// <param name="value">What was actually in the cell.</param>
    public static ImportRowError NotParseable(string field, ImportFieldType type, string? value)
        => new(
            "IMPORTS_FIELD_NOT_PARSEABLE",
            field,
            $"'{value}' is not a valid {type} for '{field}'.");

    /// <summary>A cell held a value the field does not permit.</summary>
    /// <param name="field">The field.</param>
    /// <param name="message">What is wrong with it.</param>
    public static ImportRowError OutOfRange(string field, string message)
        => new("IMPORTS_FIELD_OUT_OF_RANGE", field, message);

    /// <summary>A cell referenced something that does not exist in this tenant.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The reference that resolved to nothing.</param>
    /// <param name="what">What it should have been, for example <c>unit of measure</c>.</param>
    public static ImportRowError UnknownReference(string field, string? value, string what)
        => new(
            "IMPORTS_UNKNOWN_REFERENCE",
            field,
            $"No {what} matches '{value}'. Import or create it first.");

    /// <summary>The row duplicates something that exists, under the <c>Fail</c> strategy.</summary>
    /// <param name="field">The natural-key field that collided.</param>
    /// <param name="value">The value that already exists.</param>
    public static ImportRowError Duplicate(string field, string? value)
        => new(
            "IMPORTS_DUPLICATE",
            field,
            $"'{value}' already exists. Choose the Skip or Update duplicate strategy if that is what "
            + "you meant.");

    /// <summary>The row duplicates another row inside the same file.</summary>
    /// <param name="field">The natural-key field that collided.</param>
    /// <param name="value">The repeated value.</param>
    /// <param name="firstRowNumber">The row that had it first.</param>
    public static ImportRowError DuplicateWithinFile(string field, string? value, int firstRowNumber)
        => new(
            "IMPORTS_DUPLICATE_WITHIN_FILE",
            field,
            $"'{value}' also appears on row {firstRowNumber} of this same file. Two rows for one "
            + "record cannot both be right, and applying both would silently keep the last one.");
}
