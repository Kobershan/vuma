using System.Globalization;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports;

/// <summary>
/// Turns a cell of text into the canonical, invariant form the rest of the pipeline uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class exists because real files are not written by programmers.</b> A South African
/// supplier's export writes <c>R 1 234,56</c>. A UK one writes <c>£1,234.56</c>. A German one writes
/// <c>1.234,56</c>. Excel writes a date as a number. All four mean something unambiguous to the
/// person who sent it, and a parser that reads <c>1,234</c> as one-point-two-three-four has silently
/// divided somebody's price list by a thousand.
/// </para>
/// <para>
/// <b>Parsing happens exactly once, here, at validation</b> (<c>CONVENTIONS.md</c> §6), and the
/// canonical invariant string is what gets stored on the row and read by the target handler. Nothing
/// downstream re-parses a cell, so a preview and a commit cannot disagree about what a number was.
/// </para>
/// <para>
/// <b>What it refuses to guess.</b> Where a value is genuinely ambiguous it returns an error rather
/// than picking, with one documented exception: a lone separator followed by exactly three digits is
/// read as a thousands separator, because <c>1,234</c> written by a person filling in a price is
/// twelve hundred far more often than it is one-and-a-bit. That case is called out in the parse error
/// when it goes the other way, so a person can see what happened rather than discovering it in a
/// stocktake.
/// </para>
/// </remarks>
public static class ImportValueParser
{
    /// <summary>Text a cell may hold that means "yes".</summary>
    private static readonly string[] TruthyValues = ["true", "t", "yes", "y", "1", "x", "on"];

    /// <summary>Text a cell may hold that means "no".</summary>
    private static readonly string[] FalsyValues = ["false", "f", "no", "n", "0", "off"];

    /// <summary>
    /// Date formats tried in order, after ISO-8601. Day-first before month-first, because
    /// <c>CLAUDE.md</c> §9 sets <c>en-ZA</c> as the default locale and <c>03/04/2026</c> from a South
    /// African supplier is the third of April.
    /// </summary>
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd MMMM yyyy",
        "yyyyMMdd",
    ];

    /// <summary>
    /// Parses one cell for one field.
    /// </summary>
    /// <param name="field">The field the cell is bound to.</param>
    /// <param name="raw">The cell, as the reader produced it.</param>
    /// <returns>
    /// The canonical value, or the error explaining why it will not parse. A canonical
    /// <c>null</c> with no error means "empty, and the field does not require it".
    /// </returns>
    public static ImportParsedValue Parse(ImportFieldDescriptor field, string? raw)
    {
        ArgumentNullException.ThrowIfNull(field);

        string? trimmed = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        if (trimmed is null)
        {
            return field.IsRequired
                ? ImportParsedValue.Failed(ImportRowError.Required(field.Name))
                : ImportParsedValue.Empty;
        }

        return field.Type switch
        {
            ImportFieldType.Text => ImportParsedValue.Parsed(trimmed),
            ImportFieldType.Integer => ParseInteger(field, trimmed),
            ImportFieldType.Decimal or ImportFieldType.Money or ImportFieldType.Quantity
                => ParseNumber(field, trimmed),
            ImportFieldType.Date => ParseDate(field, trimmed),
            ImportFieldType.Boolean => ParseBoolean(field, trimmed),
            _ => ImportParsedValue.Failed(
                ImportRowError.NotParseable(field.Name, field.Type, trimmed)),
        };
    }

    /// <summary>
    /// Reads a canonical decimal back. Cannot fail — <see cref="Parse"/> already proved it.
    /// </summary>
    /// <param name="canonical">A value from <see cref="Parse"/>.</param>
    /// <returns>The number.</returns>
    /// <exception cref="FormatException">
    /// The caller passed something that did not come from <see cref="Parse"/>, which is a bug in the
    /// caller rather than a bad cell — and a loud one is better than a silent zero.
    /// </exception>
    public static decimal ToDecimal(string canonical)
        => decimal.Parse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Reads a canonical date back.</summary>
    /// <param name="canonical">A value from <see cref="Parse"/>.</param>
    /// <returns>The date.</returns>
    public static DateOnly ToDate(string canonical)
        => DateOnly.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Reads a canonical boolean back.</summary>
    /// <param name="canonical">A value from <see cref="Parse"/>.</param>
    /// <returns>The flag.</returns>
    public static bool ToBoolean(string canonical)
        => string.Equals(canonical, "true", StringComparison.Ordinal);

    private static ImportParsedValue ParseInteger(ImportFieldDescriptor field, string value)
    {
        ImportParsedValue number = ParseNumber(field, value);

        if (number.Error is not null)
        {
            return number;
        }

        decimal parsed = ToDecimal(number.Value!);

        if (parsed != decimal.Truncate(parsed))
        {
            return ImportParsedValue.Failed(ImportRowError.OutOfRange(
                field.Name, $"'{value}' has a fractional part and '{field.Name}' is a whole number."));
        }

        return ImportParsedValue.Parsed(
            decimal.Truncate(parsed).ToString(CultureInfo.InvariantCulture));
    }

    private static ImportParsedValue ParseNumber(ImportFieldDescriptor field, string value)
    {
        string working = value;
        bool negative = false;

        // (1 234,56) is how a spreadsheet writes a negative, and a credit line on a supplier's
        // price file is exactly where it turns up.
        if (working.StartsWith('(') && working.EndsWith(')'))
        {
            negative = true;
            working = working[1..^1].Trim();
        }

        // Strip anything that is not part of the number: currency symbols, codes, percent signs and
        // the non-breaking spaces Excel likes to use as a thousands separator.
        working = new string([.. working.Where(character
            => char.IsDigit(character) || character is '.' or ',' or '-' or '+' or ' ' or '\u00a0' or '\u202f')]);

        working = working.Replace("\u00a0", string.Empty, StringComparison.Ordinal)
            .Replace("\u202f", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (working.StartsWith('-'))
        {
            negative = !negative;
            working = working[1..];
        }
        else if (working.StartsWith('+'))
        {
            working = working[1..];
        }

        if (working.Length == 0 || working.Any(character => character is '-' or '+'))
        {
            return ImportParsedValue.Failed(
                ImportRowError.NotParseable(field.Name, field.Type, value));
        }

        string? canonical = Canonicalise(working);

        if (canonical is null || !decimal.TryParse(
            canonical, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return ImportParsedValue.Failed(
                ImportRowError.NotParseable(field.Name, field.Type, value));
        }

        if (negative)
        {
            parsed = -parsed;
        }

        return ImportParsedValue.Parsed(parsed.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Decides which of <c>.</c> and <c>,</c> is the decimal point, and removes the other.
    /// </summary>
    /// <param name="digitsAndSeparators">The number with sign, spaces and symbols already stripped.</param>
    /// <returns>An invariant-parseable string, or <c>null</c> when the value is not a number at all.</returns>
    private static string? Canonicalise(string digitsAndSeparators)
    {
        int lastDot = digitsAndSeparators.LastIndexOf('.');
        int lastComma = digitsAndSeparators.LastIndexOf(',');

        if (lastDot < 0 && lastComma < 0)
        {
            return digitsAndSeparators;
        }

        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: the rightmost is the decimal point and the other is grouping. This is
            // unambiguous — no locale groups with the character it also uses as a decimal point.
            char decimalPoint = lastDot > lastComma ? '.' : ',';
            char grouping = decimalPoint == '.' ? ',' : '.';

            return digitsAndSeparators
                .Replace(grouping.ToString(), string.Empty, StringComparison.Ordinal)
                .Replace(decimalPoint, '.');
        }

        char separator = lastDot >= 0 ? '.' : ',';
        int index = lastDot >= 0 ? lastDot : lastComma;
        int occurrences = digitsAndSeparators.Count(character => character == separator);
        int trailingDigits = digitsAndSeparators.Length - index - 1;

        // One separator, exactly three digits behind it, and digits in front of it: grouping.
        // 1,234 is twelve hundred. 1,23 and 1,2345 are not — no locale groups in twos or fours.
        // Two or more of the same separator can only be grouping: 1.234.567.
        bool isGrouping = (occurrences == 1 && trailingDigits == 3 && index > 0)
            || occurrences > 1;

        if (isGrouping)
        {
            // Every group must be exactly three digits, or it was never a grouped number.
            string[] groups = digitsAndSeparators.Split(separator);

            if (groups.Skip(1).Any(group => group.Length != 3) || groups[0].Length == 0)
            {
                return null;
            }

            return string.Concat(groups);
        }

        return digitsAndSeparators.Replace(separator, '.');
    }

    private static ImportParsedValue ParseDate(ImportFieldDescriptor field, string value)
    {
        if (DateOnly.TryParseExact(
            value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            return ImportParsedValue.Parsed(parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        // Excel hands a date column back as a serial number when the cell was never formatted as a
        // date. 1900-01-01 is serial 1, and the sheet's own famous 1900-02-29 leap-year bug means
        // everything from March 1900 onward is offset by one — hence the epoch below rather than a
        // straight AddDays from 1900-01-01.
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int serial)
            && serial is > 60 and < 60_000)
        {
            DateOnly fromSerial = new DateOnly(1899, 12, 30).AddDays(serial);

            return ImportParsedValue.Parsed(
                fromSerial.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return ImportParsedValue.Failed(ImportRowError.NotParseable(field.Name, field.Type, value));
    }

    private static ImportParsedValue ParseBoolean(ImportFieldDescriptor field, string value)
    {
        string lowered = value.ToLowerInvariant();

        if (TruthyValues.Contains(lowered, StringComparer.Ordinal))
        {
            return ImportParsedValue.Parsed("true");
        }

        if (FalsyValues.Contains(lowered, StringComparer.Ordinal))
        {
            return ImportParsedValue.Parsed("false");
        }

        return ImportParsedValue.Failed(ImportRowError.NotParseable(field.Name, field.Type, value));
    }
}

/// <summary>The result of parsing one cell.</summary>
/// <param name="Value">The canonical invariant form, or <c>null</c> when the cell was empty.</param>
/// <param name="Error">Why it would not parse, or <c>null</c> when it did.</param>
public sealed record ImportParsedValue(string? Value, ImportRowError? Error)
{
    /// <summary>An empty cell on a field that does not require one.</summary>
    public static ImportParsedValue Empty { get; } = new(null, null);

    /// <summary>A cell that parsed.</summary>
    /// <param name="value">Its canonical form.</param>
    public static ImportParsedValue Parsed(string value) => new(value, null);

    /// <summary>A cell that did not.</summary>
    /// <param name="error">Why.</param>
    public static ImportParsedValue Failed(ImportRowError error) => new(null, error);
}
