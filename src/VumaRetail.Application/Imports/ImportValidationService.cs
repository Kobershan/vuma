using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports;

/// <summary>
/// Turns a mapped batch into a preview: one verdict per row, with everything wrong with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A row is the unit of judgement (business rule 2).</b> One bad barcode on line 400 must not cost
/// the other 399 rows, and it must say it was line 400. Every row is walked, every error on it is
/// collected — not just the first — and the batch's counters are the report a person reads before
/// deciding to commit.
/// </para>
/// <para>
/// <b>Three passes, in this order, and the order matters.</b> Structural first (does this cell parse
/// as the type its field declares), then within-file (do two rows claim the same natural key), then
/// semantic (does this unit of measure exist in this tenant). Structural first because a cell that
/// does not parse has nothing to compare; within-file before semantic because a duplicate inside the
/// file is decidable without touching the database, and asking the database once per row for the
/// 4,000 rows of a price list is what makes an import take four minutes instead of four seconds.
/// </para>
/// <para>
/// <b>The within-file duplicate check is not an optional nicety.</b> Two rows for one record cannot
/// both be right, and applying both would silently keep whichever came last — a shop would see a
/// successful import and the wrong price. The second occurrence is the error, and it names the row
/// that had it first so a person can go and look at both.
/// </para>
/// </remarks>
/// <param name="handlers">Where the field catalogue and the semantic checks come from.</param>
public sealed class ImportValidationService(IImportTargetHandlerFactory handlers) : IImportValidator
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, ImportRowVerdict>> ValidateAsync(
        ImportBatch batch, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);

        IImportTargetHandler handler = handlers.For(batch.TargetKind);
        ImportTargetDescriptor target = handler.Descriptor;

        Dictionary<int, ImportRowVerdict> verdicts = [];

        // Natural key -> the first row that claimed it. Ordinal-insensitive because a code is a code
        // whether the file wrote it in capitals or not, which is also how every repository looks one up.
        Dictionary<string, int> seenKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (ImportRow row in batch.Rows.OrderBy(row => row.RowNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();

            (Dictionary<string, string?> parsed, List<ImportRowError> errors) = ParseRow(batch, target, row);

            if (errors.Count == 0 && NaturalKeyOf(target, parsed) is { } key)
            {
                if (seenKeys.TryGetValue(key, out int firstRow))
                {
                    errors.Add(ImportRowError.DuplicateWithinFile(
                        target.NaturalKeyFields[0], DescribeKey(target, parsed), firstRow));
                }
                else
                {
                    seenKeys[key] = row.RowNumber;
                }
            }

            if (errors.Count > 0)
            {
                // The semantic pass is skipped for a row that already failed structurally. It would
                // ask the database about values that did not parse, and the answers would be noise on
                // top of an error the person has to fix first anyway.
                verdicts[row.RowNumber] = new ImportRowVerdict(parsed, errors);
                continue;
            }

            ImportSemanticVerdict semantic = await handler
                .ValidateAsync(parsed, context, cancellationToken)
                .ConfigureAwait(false);

            verdicts[row.RowNumber] = semantic switch
            {
                { Errors.Count: > 0 } => new ImportRowVerdict(parsed, semantic.Errors),

                // The handler's skip verdict is carried through rather than flattened into "valid".
                // Every target computes it and the preview is the only place it is any use.
                { WouldSkip: true } => ImportRowVerdict.Skip(parsed),

                _ => ImportRowVerdict.Valid(parsed),
            };
        }

        return verdicts;
    }

    /// <summary>
    /// Maps and parses one row: source columns in, canonical values keyed by target field out.
    /// </summary>
    /// <param name="batch">The batch, for its mappings.</param>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="row">The row.</param>
    private static (Dictionary<string, string?> Values, List<ImportRowError> Errors) ParseRow(
        ImportBatch batch, ImportTargetDescriptor target, ImportRow row)
    {
        Dictionary<string, string?> parsed = new(StringComparer.OrdinalIgnoreCase);
        List<ImportRowError> errors = [];

        foreach (ImportColumnMapping mapping in batch.Mappings)
        {
            ImportFieldDescriptor? field = ImportMapper.FieldNamed(target, mapping.TargetField);

            if (field is null)
            {
                // The mapping was checked when it was set; a field that has vanished since means the
                // catalogue changed under a batch mid-flight. Reported on the row rather than thrown,
                // so the person sees which field and can re-map instead of getting a 500.
                errors.Add(new ImportRowError(
                    "IMPORTS_UNKNOWN_TARGET_FIELD",
                    mapping.TargetField,
                    $"'{mapping.TargetField}' is no longer a field of the {target.Kind} target. "
                    + "Set the mapping again."));

                continue;
            }

            ImportParsedValue value = ImportValueParser.Parse(field, mapping.ValueFrom(row.RawValues));

            if (value.Error is not null)
            {
                errors.Add(value.Error);
                continue;
            }

            parsed[field.Name] = value.Value;
        }

        // A required field with no mapping at all cannot happen — ImportBatch.SetMapping refuses it —
        // but a required field mapped to a column that is empty on this row can, and it is the single
        // commonest thing wrong with a real file.
        foreach (ImportFieldDescriptor field in target.Fields.Where(field => field.IsRequired))
        {
            if (!parsed.TryGetValue(field.Name, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                if (!errors.Any(error => string.Equals(error.Field, field.Name, StringComparison.Ordinal)))
                {
                    errors.Add(ImportRowError.Required(field.Name));
                }
            }
        }

        return (parsed, errors);
    }

    /// <summary>The key two rows in one file must not share.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="values">The row's parsed values.</param>
    /// <returns>The comparable key, or <c>null</c> when the target declares none.</returns>
    /// <remarks>
    /// Joined on an ASCII unit separator rather than on a character a value could contain: with a
    /// comma, a partner coded <c>A,B</c> at location <c>C</c> and one coded <c>A</c> at <c>B,C</c>
    /// would collide, and one of two perfectly good rows would be refused as a duplicate of the other.
    /// </remarks>
    private static string? NaturalKeyOf(
        ImportTargetDescriptor target, IReadOnlyDictionary<string, string?> values)
        => target.NaturalKeyFields.Count == 0
            ? null
            : string.Join(
                '\u001f',
                target.NaturalKeyFields.Select(field => values.TryGetValue(field, out string? value)
                    ? value ?? string.Empty
                    : string.Empty));

    /// <summary>The natural key as a person reads it back in the error message.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="values">The row's parsed values.</param>
    private static string DescribeKey(
        ImportTargetDescriptor target, IReadOnlyDictionary<string, string?> values)
        => string.Join(
            " / ",
            target.NaturalKeyFields.Select(field => values.TryGetValue(field, out string? value)
                ? value ?? string.Empty
                : string.Empty));
}

/// <summary>Produces one verdict per row on a mapped batch — the preview.</summary>
/// <remarks>
/// A port so that the commit path and the validate path share exactly one implementation. Two
/// validators, however similar, are two chances for a commit to apply something a preview never
/// showed anybody.
/// </remarks>
public interface IImportValidator
{
    /// <summary>Validates every row on a mapped batch.</summary>
    /// <param name="batch">The batch, with its rows and mappings loaded.</param>
    /// <param name="context">The batch's store, duplicate strategy and currency.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One verdict per row, keyed by <see cref="ImportRow.RowNumber"/>.</returns>
    Task<IReadOnlyDictionary<int, ImportRowVerdict>> ValidateAsync(
        ImportBatch batch, ImportContext context, CancellationToken cancellationToken = default);
}
