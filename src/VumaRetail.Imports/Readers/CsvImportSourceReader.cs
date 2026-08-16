using System.Globalization;
using System.Text;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Imports.Readers;

/// <summary>
/// Reads delimiter-separated text to RFC 4180, by hand (ADR-077).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why hand-written.</b> A CSV parser is four rules — a quote opens a field, a doubled quote inside
/// one is a literal quote, a delimiter or newline inside quotes is data, and anything else is a
/// character — and every one of them is exercised by the first real supplier file that contains
/// <c>SMITH &amp; SON, PTY</c> in a company name. Taking a dependency for four rules costs a
/// third-party licence review and a supply-chain surface on the one code path that eats untrusted
/// bytes from outside the building, and buys nothing this file does not do in eighty lines.
/// </para>
/// <para>
/// <b>What the corner cases actually are, in the order they bite:</b> a BOM in front of the first
/// header (Excel writes one, and without stripping it the first column is called
/// <c>"﻿SKU"</c> and matches no alias); CRLF versus LF, often mixed inside one file that has been
/// through two machines; a quoted field containing the delimiter; a quoted field containing a
/// newline, which is what a multi-line address in a customer list is; a doubled quote for a literal
/// one; a ragged row shorter or longer than the header; and a trailing empty line, which every
/// spreadsheet writes and which must not become a row of nulls.
/// </para>
/// <para>
/// <b>The delimiter is detected, not assumed.</b> A European export is semicolon-separated because the
/// comma is that locale's decimal point, and a person who has just been handed such a file has no idea
/// that is why their import produced one column. Detection reads only the header line, and only
/// outside quotes.
/// </para>
/// </remarks>
public sealed class CsvImportSourceReader : IImportSourceReader
{
    /// <summary>The delimiters detection will consider, in preference order.</summary>
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    /// <inheritdoc />
    public ImportSourceFormat Format => ImportSourceFormat.Csv;

    /// <inheritdoc />
    public async Task<ImportSheet> ReadAsync(
        Stream content, ImportReadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        // detectEncodingFromByteOrderMarks strips a UTF-8, UTF-16 or UTF-32 BOM and picks the right
        // encoding for the last two. A UTF-16 export from an older till system is otherwise read as
        // interleaved nulls, which fails as "no columns" rather than as "wrong encoding".
        using StreamReader reader = new(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw ImportSourceException.Unreadable(ImportSourceFormat.Csv, "the file is empty.");
        }

        // A stream that has already been read past its BOM, or one handed over as a byte array, keeps
        // the character; strip it rather than trusting the reader to have done it.
        text = text.TrimStart('﻿');

        char delimiter = options.Delimiter ?? DetectDelimiter(text);
        IReadOnlyList<IReadOnlyList<string>> records = Split(text, delimiter);

        if (records.Count == 0)
        {
            throw ImportSourceException.Unreadable(ImportSourceFormat.Csv, "the file has no header row.");
        }

        IReadOnlyList<string> headers = NameHeaders(records[0]);

        if (records.Count - 1 > options.MaxRows)
        {
            throw ImportRuleException.TooManyRows(records.Count - 1, options.MaxRows);
        }

        List<ImportSourceRow> rows = [];

        for (int index = 1; index < records.Count; index++)
        {
            IReadOnlyList<string> record = records[index];

            // A row of nothing but empty cells is a blank line in the middle of a sheet, not a record
            // somebody meant to import. Keeping it would put a required-field error on a line the
            // person cannot see anything wrong with.
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

            for (int column = 0; column < headers.Count; column++)
            {
                // Short row: the missing cells are empty, not an error. A supplier who omits the
                // trailing optional columns on rows that have no value for them is being reasonable.
                string? cell = column < record.Count ? record[column] : null;
                values[headers[column]] = string.IsNullOrWhiteSpace(cell) ? null : cell;
            }

            // The row number counts the header as 1 and is the number the person's spreadsheet shows,
            // so it is the record's position in the file — not its position among the rows we kept.
            rows.Add(new ImportSourceRow(index + 1, values));
        }

        return new ImportSheet(headers, rows);
    }

    /// <summary>
    /// Picks the delimiter by counting candidates outside quotes on the header line.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <returns>The delimiter, defaulting to a comma when nothing separates anything.</returns>
    /// <remarks>
    /// Only the header line is considered, because it is the one line guaranteed not to contain free
    /// text a person typed. Counting over the whole file lets one address field full of commas
    /// outvote forty semicolons.
    /// </remarks>
    private static char DetectDelimiter(string text)
    {
        string header = FirstLineOutsideQuotes(text);

        char best = ',';
        int bestCount = 0;

        foreach (char candidate in CandidateDelimiters)
        {
            int count = CountOutsideQuotes(header, candidate);

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>The first physical line, ignoring newlines that are inside a quoted field.</summary>
    /// <param name="text">The whole file.</param>
    private static string FirstLineOutsideQuotes(string text)
    {
        bool inQuotes = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character is '\n' or '\r')
            {
                return text[..index];
            }
        }

        return text;
    }

    /// <summary>Counts a character in a line, skipping anything inside quotes.</summary>
    /// <param name="line">The line.</param>
    /// <param name="candidate">The character to count.</param>
    private static int CountOutsideQuotes(string line, char candidate)
    {
        bool inQuotes = false;
        int count = 0;

        foreach (char character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == candidate)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Splits the whole file into records of fields — the RFC 4180 state machine.
    /// </summary>
    /// <param name="text">The file.</param>
    /// <param name="delimiter">The field separator.</param>
    /// <returns>Every record, in file order, with its trailing empty line dropped.</returns>
    private static IReadOnlyList<IReadOnlyList<string>> Split(string text, char delimiter)
    {
        List<IReadOnlyList<string>> records = [];
        List<string> fields = [];
        StringBuilder field = new();
        bool inQuotes = false;
        bool sawAnyCharacter = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (inQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }

                // A doubled quote inside a quoted field is one literal quote — 55" TV is written
                // "55"" TV". Anything else closes the field.
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    sawAnyCharacter = true;
                    break;

                case '\r':
                    // Swallow the CR of a CRLF and let the LF end the record, so a file with mixed
                    // line endings — one machine's export edited on another — reads as one file.
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields);
                    fields = [];
                    sawAnyCharacter = false;
                    break;

                default:
                    if (character == delimiter)
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        sawAnyCharacter = true;
                    }
                    else
                    {
                        field.Append(character);
                        sawAnyCharacter = true;
                    }

                    break;
            }
        }

        // The last record only exists if the file did not end on a newline, or if something was typed
        // after the last one. Without this check every file that ends properly gains a phantom row.
        if (sawAnyCharacter || field.Length > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }

    /// <summary>
    /// Trims the headers and names the unnamed ones, so every column can be bound to.
    /// </summary>
    /// <param name="raw">The header record as it was split.</param>
    /// <returns>The headers, in file order.</returns>
    /// <remarks>
    /// An empty header becomes <c>column 4</c> rather than being dropped: dropping it would shift every
    /// header after it one column to the left, and the file would import with every value in the wrong
    /// field. A duplicated header is left alone here and refused by <c>ImportBatch.RecordParse</c>,
    /// which is where the rule that a target field binds to one column lives.
    /// </remarks>
    private static IReadOnlyList<string> NameHeaders(IReadOnlyList<string> raw)
    {
        // Trailing unnamed columns are the artefact of a sheet saved with columns to spare, and binding
        // to them is never what somebody wants. Trailing ones only: an unnamed column in the middle
        // still holds data, under a header the supplier forgot to write.
        int lastNamed = raw.Count - 1;

        while (lastNamed > 0 && raw[lastNamed].Trim().Length == 0)
        {
            lastNamed--;
        }

        List<string> headers = [];

        for (int index = 0; index <= lastNamed; index++)
        {
            string header = raw[index].Trim().TrimStart('﻿');

            headers.Add(header.Length > 0
                ? header
                : $"column {(index + 1).ToString(CultureInfo.InvariantCulture)}");
        }

        return headers;
    }
}
