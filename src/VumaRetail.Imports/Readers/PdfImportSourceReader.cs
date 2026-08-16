using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Imports.Readers;

/// <summary>
/// Reads a machine-generated PDF price list by reconstructing its table from word geometry.
/// </summary>
/// <remarks>
/// <para>
/// <b>A PDF has no table.</b> It has glyphs at coordinates. The grid a person sees is an illusion
/// their eye assembles, and this class assembles the same illusion the same way: words that share a
/// baseline are a row, and words that share a horizontal position are a column. Nothing in the file
/// says so — PdfPig gives words with bounding boxes, and the clustering below is the whole reader.
/// </para>
/// <para>
/// <b>Rows, by baseline.</b> Words are grouped by the vertical centre of their box, with a tolerance
/// scaled to the text height rather than a fixed number of points, because a supplier's 7pt price
/// list and a 14pt one are the same document at different sizes. Rows come out top-down, which is
/// descending Y — a PDF's origin is the bottom-left corner, and getting that backwards silently
/// reverses everybody's price list.
/// </para>
/// <para>
/// <b>Columns, from the header row.</b> The first row is the header, and each header word's left edge
/// opens a column that runs to the next header's left edge. Assigning a data word to the column its
/// own left edge falls in handles the ordinary case (left-aligned text) and the awkward one
/// (right-aligned prices, whose left edge still lands inside their own column) without needing to
/// know which alignment a column uses.
/// </para>
/// <para>
/// <b>What this deliberately does not attempt.</b> Multi-line cells, spanned headers, nested tables
/// and rotated text. A supplier's price list is a flat grid, that is the document R5 names, and a
/// reader that guessed at the rest would produce plausible-looking wrong rows — which is worse than a
/// refusal, because the preview would look fine.
/// </para>
/// <para>
/// A PDF with no text layer is a scan. It is refused with
/// <c>IMPORTS_PDF_HAS_NO_TEXT_LAYER</c> rather than imported as zero rows, because "your file
/// contained nothing" is a lie a person cannot act on. OCR is deferred behind
/// <see cref="IOcrTextExtractor"/> — see <c>PROGRESS.md</c>, "Deferred".
/// </para>
/// </remarks>
/// <param name="ocr">
/// The OCR seam. Consulted for a scan so the refusal can say whether OCR would have helped, and so
/// registering a real extractor later is a container change rather than a change to this class.
/// </param>
public sealed class PdfImportSourceReader(IOcrTextExtractor ocr) : IImportSourceReader
{
    /// <summary>
    /// How much of a word's own height two words may differ in baseline and still be one row.
    /// </summary>
    /// <remarks>
    /// A fraction of the text height rather than a constant, so the same value works for a 7pt price
    /// list and a 14pt one. Six tenths is wide enough to hold a row whose cells use slightly different
    /// fonts, and narrow enough not to swallow the row above at ordinary line spacing.
    /// </remarks>
    private const double RowToleranceFactor = 0.6;

    /// <summary>
    /// How far apart two words on the header line may be and still be one heading, as a multiple of
    /// the text height.
    /// </summary>
    /// <remarks>
    /// <c>Unit</c> and <c>Price</c> are one column and must merge; <c>SKU</c> and <c>Description</c>
    /// are two and must not. The two cases are separated by an order of magnitude in a real table —
    /// an inter-word space is around a quarter of the text height, while a column gap is several
    /// characters wide — so anything in that gap works, and one-and-a-half heights sits in the middle
    /// of it rather than on the edge of the word-spacing a particular PDF writer happened to emit.
    /// </remarks>
    private const double HeadingMergeFactor = 1.5;

    /// <summary>
    /// How far left of its column's boundary a data word may start and still belong to that column,
    /// as a multiple of the text height.
    /// </summary>
    /// <remarks>
    /// Catches a value set slightly wider than its heading — a long SKU under a short header — which
    /// would otherwise land in the column before it and shift the whole row. Deliberately much
    /// tighter than <see cref="HeadingMergeFactor"/>: this one is a nudge, not a merge.
    /// </remarks>
    private const double ColumnToleranceFactor = 0.5;

    /// <inheritdoc />
    public ImportSourceFormat Format => ImportSourceFormat.Pdf;

    /// <inheritdoc />
    public Task<ImportSheet> ReadAsync(
        Stream content, ImportReadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        List<IReadOnlyList<Word>> lines = [];

        using (PdfDocument document = OpenDocument(content))
        {
            foreach (Page page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.AddRange(GroupIntoLines(page.GetWords()));
            }
        }

        if (lines.Count == 0)
        {
            // Distinguishing a scan from a genuinely blank PDF is not possible from the text layer
            // alone — both have no words — so both get the message that tells a person what to do,
            // and the message is honest about OCR being unavailable rather than blaming their file.
            throw ocr.IsAvailable
                ? ImportSourceException.Unreadable(
                    ImportSourceFormat.Pdf, "no text could be extracted from any page.")
                : ImportSourceException.PdfHasNoTextLayer();
        }

        IReadOnlyList<Word> headerLine = lines[0];
        IReadOnlyList<double> boundaries = ColumnBoundaries(headerLine);
        IReadOnlyList<string> headers = NameHeaders(headerLine);

        if (headers.Count == 0)
        {
            throw ImportSourceException.Unreadable(ImportSourceFormat.Pdf, "the first line is empty.");
        }

        if (lines.Count - 1 > options.MaxRows)
        {
            throw ImportRuleException.TooManyRows(lines.Count - 1, options.MaxRows);
        }

        List<ImportSourceRow> rows = [];

        for (int index = 1; index < lines.Count; index++)
        {
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
            string?[] cells = new string?[headers.Count];

            foreach (Word word in lines[index])
            {
                int column = ColumnFor(word, boundaries);

                // Two words in one cell — "SMITH AND SON" under a Name header — join with a space,
                // which is what the reader is looking at on the page.
                cells[column] = cells[column] is null
                    ? word.Text
                    : $"{cells[column]} {word.Text}";
            }

            bool anyValue = false;

            for (int column = 0; column < headers.Count; column++)
            {
                string? cell = string.IsNullOrWhiteSpace(cells[column]) ? null : cells[column]!.Trim();
                values[headers[column]] = cell;
                anyValue |= cell is not null;
            }

            if (!anyValue)
            {
                continue;
            }

            rows.Add(new ImportSourceRow(index + 1, values));
        }

        return Task.FromResult(new ImportSheet(headers, rows));
    }

    /// <summary>Opens the document, turning bytes that are not a PDF into a caller-fixable refusal.</summary>
    /// <param name="content">The uploaded bytes.</param>
    private static PdfDocument OpenDocument(Stream content)
    {
        try
        {
            return PdfDocument.Open(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Broad for the same reason as the workbook reader: an encrypted PDF, a truncated
            // download and a JPEG renamed to .pdf all throw differently and all mean one thing to the
            // person holding the file.
            throw ImportSourceException.Unreadable(ImportSourceFormat.Pdf, exception.Message);
        }
    }

    /// <summary>Groups a page's words into visual lines, top-down and then left-to-right.</summary>
    /// <param name="words">Every word on the page, in whatever order the content stream had them.</param>
    private static IReadOnlyList<IReadOnlyList<Word>> GroupIntoLines(IEnumerable<Word> words)
    {
        List<Word> ordered = words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        // Descending Y: a PDF's origin is the bottom-left corner, so the top of the page is the
        // largest Y. Sorting ascending here is the bug that silently reverses a price list.
        ordered.Sort((left, right) => Centre(right).CompareTo(Centre(left)));

        List<List<Word>> lines = [];
        List<Word> current = [ordered[0]];
        double currentCentre = Centre(ordered[0]);

        for (int index = 1; index < ordered.Count; index++)
        {
            Word word = ordered[index];
            double tolerance = Math.Max(word.BoundingBox.Height, 1d) * RowToleranceFactor;

            if (Math.Abs(currentCentre - Centre(word)) <= tolerance)
            {
                current.Add(word);
                continue;
            }

            lines.Add(current);
            current = [word];
            currentCentre = Centre(word);
        }

        lines.Add(current);

        foreach (List<Word> line in lines)
        {
            line.Sort((left, right) => left.BoundingBox.Left.CompareTo(right.BoundingBox.Left));
        }

        return lines;
    }

    /// <summary>The vertical centre of a word's box — its baseline, near enough to cluster on.</summary>
    /// <param name="word">The word.</param>
    private static double Centre(Word word) => (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2d;

    /// <summary>
    /// The left edge of each column, taken from the header row.
    /// </summary>
    /// <param name="headerLine">The header row's words, already left-to-right.</param>
    /// <remarks>
    /// Header words closer together than a space are one heading — <c>Unit</c> <c>Price</c> is one
    /// column, not two — and merging them here is what stops a two-word heading splitting the data
    /// beneath it down the middle.
    /// </remarks>
    private static IReadOnlyList<double> ColumnBoundaries(IReadOnlyList<Word> headerLine)
    {
        List<double> boundaries = [];
        double previousRight = double.MinValue;

        foreach (Word word in headerLine)
        {
            double gap = word.BoundingBox.Left - previousRight;

            if (boundaries.Count == 0 || gap > MergeGap(word))
            {
                boundaries.Add(word.BoundingBox.Left);
            }

            previousRight = word.BoundingBox.Right;
        }

        return boundaries;
    }

    /// <summary>The header texts, with the words of a multi-word heading joined.</summary>
    /// <param name="headerLine">The header row's words, already left-to-right.</param>
    private static IReadOnlyList<string> NameHeaders(IReadOnlyList<Word> headerLine)
    {
        List<string> headers = [];
        double previousRight = double.MinValue;

        foreach (Word word in headerLine)
        {
            double gap = word.BoundingBox.Left - previousRight;

            if (headers.Count == 0 || gap > MergeGap(word))
            {
                headers.Add(word.Text.Trim());
            }
            else
            {
                headers[^1] = $"{headers[^1]} {word.Text.Trim()}";
            }

            previousRight = word.BoundingBox.Right;
        }

        for (int index = 0; index < headers.Count; index++)
        {
            if (headers[index].Length == 0)
            {
                headers[index] = $"column {(index + 1).ToString(CultureInfo.InvariantCulture)}";
            }
        }

        return headers;
    }

    /// <summary>
    /// The largest gap that still counts as a space inside one heading, for a word of this size.
    /// </summary>
    /// <param name="word">The word, whose height stands in for the text size.</param>
    private static double MergeGap(Word word)
        => Math.Max(word.BoundingBox.Height, 1d) * HeadingMergeFactor;

    /// <summary>The column a word belongs to, by which boundary its left edge falls past.</summary>
    /// <param name="word">The word.</param>
    /// <param name="boundaries">The column left edges, ascending.</param>
    /// <remarks>
    /// A small tolerance to the left of each boundary catches a data value set slightly wider than its
    /// heading — a long SKU under a short header — which would otherwise land in the column before it
    /// and shift the whole row.
    /// </remarks>
    private static int ColumnFor(Word word, IReadOnlyList<double> boundaries)
    {
        double left = word.BoundingBox.Left;
        double tolerance = Math.Max(word.BoundingBox.Height, 1d) * ColumnToleranceFactor;
        int column = 0;

        for (int index = 0; index < boundaries.Count; index++)
        {
            if (left >= boundaries[index] - tolerance)
            {
                column = index;
            }
        }

        return Math.Min(column, boundaries.Count - 1);
    }
}

/// <summary>
/// The OCR seam with nothing behind it — the honest state of scanned-PDF support in this build.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §4 names Tesseract as the fallback for scans. Tesseract needs a native library and
/// a trained data file, neither of which this build can acquire, and §1 says the answer to that is a
/// stub behind an interface plus an entry in <c>PROGRESS.md</c> — not a silent gap. This class is
/// that stub. It reports itself unavailable, which is what makes the PDF reader refuse a scan with a
/// message a person can act on rather than returning zero rows.
/// </remarks>
public sealed class UnavailableOcrTextExtractor : IOcrTextExtractor
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string> ExtractAsync(
        ReadOnlyMemory<byte> pageImage, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Optical character recognition is not available in this build. Callers must check "
            + "IsAvailable first; the PDF reader does, and refuses a scan with "
            + "IMPORTS_PDF_HAS_NO_TEXT_LAYER rather than reaching this method.");
}
