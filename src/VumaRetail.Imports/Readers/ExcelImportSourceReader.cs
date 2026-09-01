using System.Globalization;
using ClosedXML.Excel;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Imports.Readers;

/// <summary>
/// Reads an <c>.xlsx</c> workbook with ClosedXML, the <c>CLAUDE.md</c> §4 locked choice.
/// </summary>
/// <remarks>
/// <para>
/// <b>A spreadsheet is not a table, and the three ways it is not are all handled here.</b>
/// </para>
/// <para>
/// <b>Formulas.</b> A supplier's price sheet computes the sell price from cost and margin, so the cell
/// holds <c>=B2*1.35</c> and not a number. The formula's <em>cached value</em> — what Excel last
/// showed the person who saved it — is what the file is telling us, so that is what is read. A
/// workbook saved by a tool that did not cache its values gives an empty cell, which becomes a row
/// error naming the field rather than a silent zero.
/// </para>
/// <para>
/// <b>Dates.</b> Excel stores a date as a serial number with a display format, and a cell that
/// somebody formatted as <c>General</c> hands back <c>45678</c>. Where ClosedXML knows the cell is a
/// date this reader emits an ISO date, so the parser never has to guess; where it does not, the serial
/// number reaches <c>ImportValueParser</c>, which recognises it (including the sheet's own 1900
/// leap-year bug) and says so.
/// </para>
/// <para>
/// <b>Numbers stored as text.</b> A barcode column is text far more often than not, because a leading
/// zero is meaningful and Excel eats it otherwise. Reading every cell as its string value rather than
/// coercing to a number is what keeps <c>0060012345678</c> intact.
/// </para>
/// <para>
/// The used range is trimmed rather than trusted: a sheet somebody has scrolled through and deleted
/// rows from reports a used range hundreds of rows past its last value, and importing four hundred
/// empty rows as four hundred required-field errors is a support call.
/// </para>
/// </remarks>
public sealed class ExcelImportSourceReader : IImportSourceReader
{
    /// <inheritdoc />
    public ImportSourceFormat Format => ImportSourceFormat.Excel;

    /// <inheritdoc />
    public Task<ImportSheet> ReadAsync(
        Stream content, ImportReadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        using XLWorkbook workbook = OpenWorkbook(content);

        IXLWorksheet worksheet = SelectWorksheet(workbook, options.Worksheet);
        IXLRange? used = worksheet.RangeUsed();

        if (used is null)
        {
            throw ImportSourceException.Unreadable(
                ImportSourceFormat.Excel, $"worksheet '{worksheet.Name}' is empty.");
        }

        int firstRow = used.FirstRow().RowNumber();
        int lastRow = used.LastRow().RowNumber();
        int firstColumn = used.FirstColumn().ColumnNumber();
        int lastColumn = used.LastColumn().ColumnNumber();

        IReadOnlyList<string> headers = ReadHeaders(worksheet, firstRow, firstColumn, lastColumn);

        if (headers.Count == 0)
        {
            throw ImportSourceException.Unreadable(
                ImportSourceFormat.Excel, $"worksheet '{worksheet.Name}' has no header row.");
        }

        if (lastRow - firstRow > options.MaxRows)
        {
            throw ImportRuleException.TooManyRows(lastRow - firstRow, options.MaxRows);
        }

        List<ImportSourceRow> rows = [];

        for (int row = firstRow + 1; row <= lastRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
            bool anyValue = false;

            for (int column = 0; column < headers.Count; column++)
            {
                string? cell = ReadCell(worksheet.Cell(row, firstColumn + column));
                values[headers[column]] = cell;
                anyValue |= cell is not null;
            }

            if (!anyValue)
            {
                continue;
            }

            // The worksheet's own row number, not a count of the rows kept — it is the number the
            // person sees down the left-hand side of the sheet they are looking at.
            rows.Add(new ImportSourceRow(row, values));
        }

        return Task.FromResult(new ImportSheet(headers, rows));
    }

    /// <summary>Opens the workbook, turning a non-OOXML upload into a caller-fixable refusal.</summary>
    /// <param name="content">The uploaded bytes.</param>
    private static XLWorkbook OpenWorkbook(Stream content)
    {
        try
        {
            return new XLWorkbook(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately broad: ClosedXML and the OOXML SDK beneath it throw half a dozen unrelated
            // types for "these bytes are not a workbook" — an .xls renamed to .xlsx, a CSV uploaded
            // under the wrong format, a truncated download. All of them are the same 400 to the person
            // holding the file, and none of them is a fault in this server.
            throw ImportSourceException.Unreadable(ImportSourceFormat.Excel, exception.Message);
        }
    }

    /// <summary>Picks the worksheet to read.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="name">The requested worksheet, or <c>null</c> for the first.</param>
    /// <exception cref="ImportSourceException">The named worksheet is not in the workbook.</exception>
    private static IXLWorksheet SelectWorksheet(XLWorkbook workbook, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return workbook.Worksheets.FirstOrDefault()
                ?? throw ImportSourceException.Unreadable(
                    ImportSourceFormat.Excel, "the workbook has no worksheets.");
        }

        return workbook.Worksheets.FirstOrDefault(sheet
                => string.Equals(sheet.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw ImportSourceException.WorksheetNotFound(name.Trim());
    }

    /// <summary>Reads the header row, naming unnamed columns and dropping trailing empty ones.</summary>
    /// <param name="worksheet">The worksheet.</param>
    /// <param name="row">The header row number.</param>
    /// <param name="firstColumn">The first used column.</param>
    /// <param name="lastColumn">The last used column.</param>
    private static IReadOnlyList<string> ReadHeaders(
        IXLWorksheet worksheet, int row, int firstColumn, int lastColumn)
    {
        List<string?> raw = [];

        for (int column = firstColumn; column <= lastColumn; column++)
        {
            raw.Add(ReadCell(worksheet.Cell(row, column)));
        }

        int lastNamed = raw.Count - 1;

        while (lastNamed >= 0 && raw[lastNamed] is null)
        {
            lastNamed--;
        }

        List<string> headers = [];

        for (int index = 0; index <= lastNamed; index++)
        {
            headers.Add(raw[index]
                ?? $"column {(index + 1).ToString(CultureInfo.InvariantCulture)}");
        }

        return headers;
    }

    /// <summary>
    /// Reads one cell as the text it means, or <c>null</c> when it is empty.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <remarks>
    /// A date is emitted in ISO form so no locale guessing is needed downstream. A number is emitted
    /// invariantly for the same reason — <c>ToString()</c> on a machine configured for a comma decimal
    /// point would hand the parser a string it then has to un-guess. Everything else is its string
    /// value, which for a formula cell is the cached result rather than the formula text.
    /// </remarks>
    private static string? ReadCell(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        XLDataType type = cell.CachedValue.Type;

        string text = type switch
        {
            XLDataType.DateTime => cell.CachedValue.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => FormatNumber(cell.CachedValue.GetNumber()),
            XLDataType.Boolean => cell.CachedValue.GetBoolean() ? "true" : "false",
            XLDataType.Error => string.Empty,
            _ => cell.GetString(),
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Writes a workbook number as the text the person who typed it would recognise.</summary>
    /// <param name="value">The cell's numeric value.</param>
    /// <remarks>
    /// Via <see cref="decimal"/> rather than straight off the <see cref="double"/>. A sheet holds
    /// 19.99 as a binary double that round-trips to <c>19.989999999999998</c> at full precision, and
    /// a price list imported at that value is a price list that fails every equality assertion a
    /// person makes about it. The decimal conversion rounds to the 15 significant digits a
    /// spreadsheet actually carries, which is the number that was typed. Values outside decimal's
    /// range are not prices or quantities and fall back to a round-trippable form.
    /// </remarks>
    private static string FormatNumber(double value)
    {
        try
        {
            return ((decimal)value).ToString(CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
