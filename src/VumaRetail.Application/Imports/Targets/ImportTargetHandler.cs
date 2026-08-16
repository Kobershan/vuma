using System.Globalization;
using System.Text.Json;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// What every target handler shares: reading a normalised value, and writing and reading a
/// before-image.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately thin. It holds the two mechanisms that must behave identically across all five
/// targets — because a before-image written one way and read another is a rollback that restores
/// nothing, and it fails at the worst possible moment — and nothing else. The business of each target
/// stays in its own handler where a person can read it in one sitting.
/// </para>
/// <para>
/// The value accessors take the <em>normalised</em> dictionary, never the raw one. Parsing happened
/// once, at validation (<c>CONVENTIONS.md</c> §6); a handler that reached for a raw cell would be
/// re-parsing, and a preview and a commit that parse separately are a preview and a commit that can
/// disagree.
/// </para>
/// </remarks>
public abstract class ImportTargetHandler : IImportTargetHandler
{
    /// <summary>
    /// How a before-image is written and read back. Never indented and never camel-cased: the JSON is
    /// a machine's record of a prior state, not a document anybody reads, and it goes in a database
    /// column where every byte is multiplied by the row count of somebody's stock file.
    /// </summary>
    private static readonly JsonSerializerOptions BeforeImageJson = new()
    {
        WriteIndented = false,
    };

    /// <inheritdoc />
    public abstract ImportTargetKind Kind { get; }

    /// <inheritdoc />
    public abstract ImportTargetDescriptor Descriptor { get; }

    /// <inheritdoc />
    public abstract Task<ImportSemanticVerdict> ValidateAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<ImportApplyResult> ApplyAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default);

    /// <summary>Serialises a before-image.</summary>
    /// <typeparam name="TImage">The image's shape, private to the handler that writes it.</typeparam>
    /// <param name="image">The prior state.</param>
    /// <returns>The JSON to store on the row.</returns>
    protected static string WriteBeforeImage<TImage>(TImage image)
        => JsonSerializer.Serialize(image, BeforeImageJson);

    /// <summary>Reads a before-image back.</summary>
    /// <typeparam name="TImage">The image's shape.</typeparam>
    /// <param name="row">The committed row.</param>
    /// <returns>The prior state.</returns>
    /// <exception cref="ImportRuleException">
    /// The row has no before-image, or one this handler cannot read. Both mean the row cannot be
    /// restored, and a rollback that silently skipped it would report success having changed nothing —
    /// which is the one outcome ADR-076 is written to prevent.
    /// </exception>
    protected static TImage ReadBeforeImage<TImage>(ImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.BeforeImage))
        {
            throw new ImportRuleException(
                "IMPORTS_BEFORE_IMAGE_MISSING",
                $"Row {row.RowNumber.ToString(CultureInfo.InvariantCulture)} updated an existing "
                + "record and carries no before-image, so there is nothing to restore it to.");
        }

        try
        {
            return JsonSerializer.Deserialize<TImage>(row.BeforeImage, BeforeImageJson)
                ?? throw new ImportRuleException(
                    "IMPORTS_BEFORE_IMAGE_UNREADABLE",
                    $"Row {row.RowNumber.ToString(CultureInfo.InvariantCulture)}'s before-image is "
                    + "empty.");
        }
        catch (JsonException exception)
        {
            throw new ImportRuleException(
                "IMPORTS_BEFORE_IMAGE_UNREADABLE",
                $"Row {row.RowNumber.ToString(CultureInfo.InvariantCulture)}'s before-image cannot be "
                + $"read back: {exception.Message}");
        }
    }

    /// <summary>Reads a text value, or <c>null</c> when the row does not carry one.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    protected static string? Text(IReadOnlyDictionary<string, string?> values, string field)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.TryGetValue(field, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    /// <summary>Reads a text value that validation has already proved is present.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    /// <exception cref="InvalidOperationException">
    /// The field is empty, which means a required field reached apply unvalidated — a bug in the
    /// pipeline rather than in the file, and one that must not be papered over with an empty string.
    /// </exception>
    protected static string RequiredText(IReadOnlyDictionary<string, string?> values, string field)
        => Text(values, field)
            ?? throw new InvalidOperationException(
                $"'{field}' is required and reached the target handler empty. Validation should have "
                + "refused this row.");

    /// <summary>Reads a decimal value, or <c>null</c>.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    protected static decimal? Number(IReadOnlyDictionary<string, string?> values, string field)
    {
        string? text = Text(values, field);

        return text is null ? null : ImportValueParser.ToDecimal(text);
    }

    /// <summary>Reads a money value in the batch's currency, or <c>null</c>.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    /// <param name="context">The batch's context, which is where the currency comes from.</param>
    /// <remarks>
    /// The currency is the batch's, never the file's. A cell reading <c>USD 4.99</c> in a
    /// ZAR-denominated shop is a mistake in the file, and importing it as dollars is how §4.13 bricked
    /// a terminal — the currency belongs to the store, and the parser has already stripped the symbol.
    /// </remarks>
    protected static Money? Amount(
        IReadOnlyDictionary<string, string?> values, string field, ImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        decimal? number = Number(values, field);

        return number is null ? null : new Money(number.Value, context.Currency);
    }

    /// <summary>Reads a boolean value, falling back when the row does not carry one.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    /// <param name="fallback">What an absent cell means.</param>
    protected static bool Flag(
        IReadOnlyDictionary<string, string?> values, string field, bool fallback)
    {
        string? text = Text(values, field);

        return text is null ? fallback : ImportValueParser.ToBoolean(text);
    }

    /// <summary>Reads a date value, or <c>null</c>.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    protected static DateOnly? Date(IReadOnlyDictionary<string, string?> values, string field)
    {
        string? text = Text(values, field);

        return text is null ? null : ImportValueParser.ToDate(text);
    }

    /// <summary>
    /// Reads a cell as one of an enum's members, matched on the name, case-insensitively.
    /// </summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="values">The row's normalised values.</param>
    /// <param name="field">The target field.</param>
    /// <param name="fallback">What an absent cell means.</param>
    /// <param name="parsed">The member the cell named.</param>
    /// <returns><c>true</c> when the cell was absent or named a member; <c>false</c> when it named nothing.</returns>
    protected static bool TryEnum<TEnum>(
        IReadOnlyDictionary<string, string?> values, string field, TEnum fallback, out TEnum parsed)
        where TEnum : struct, Enum
    {
        string? text = Text(values, field);

        if (text is null)
        {
            parsed = fallback;
            return true;
        }

        // Numeric parsing is refused deliberately: Enum.TryParse accepts "7" for an enum with three
        // members and returns a value that names nothing. A spreadsheet full of type codes would then
        // import as garbage the preview called valid.
        if (!Enum.TryParse(text, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed))
        {
            parsed = fallback;
            return false;
        }

        return true;
    }

    /// <summary>The permitted names of an enum, for an error message that says what to write instead.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    protected static string NamesOf<TEnum>()
        where TEnum : struct, Enum
        => string.Join(", ", Enum.GetNames<TEnum>());
}
