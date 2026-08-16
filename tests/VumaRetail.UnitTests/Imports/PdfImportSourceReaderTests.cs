using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;
using VumaRetail.Imports.Readers;

namespace VumaRetail.UnitTests.Imports;

/// <summary>
/// Reconstructing a table from word geometry, asserted against a PDF built in-test.
/// </summary>
/// <remarks>
/// The fixtures are written by PdfPig's own document builder at known coordinates, which is what makes
/// the geometry assertions meaningful: the test states where each word sits on the page, so a failure
/// says something about the clustering rather than about a binary nobody can inspect.
/// </remarks>
public sealed class PdfImportSourceReaderTests
{
    private static readonly PdfImportSourceReader Reader = new(new UnavailableOcrTextExtractor());

    [Fact]
    public async Task Reads_a_machine_generated_price_list()
    {
        using MemoryStream file = Pdf(
            [
                (60, 700, "SKU"), (200, 700, "Description"), (400, 700, "Price"),
                (60, 680, "MILK-1L"), (200, 680, "Full"), (240, 680, "Cream"), (400, 680, "18.99"),
                (60, 660, "BRD-700"), (200, 660, "Brown"), (245, 660, "Bread"), (400, 660, "15.50"),
            ]);

        ImportSheet sheet = await Reader.ReadAsync(file, new ImportReadOptions());

        sheet.Headers.Should().Equal("SKU", "Description", "Price");
        sheet.Rows.Should().HaveCount(2);
        sheet.Rows[0].Values["SKU"].Should().Be("MILK-1L");
        sheet.Rows[0].Values["Description"].Should().Be("Full Cream", "words inside one cell join");
        sheet.Rows[0].Values["Price"].Should().Be("18.99");
        sheet.Rows[1].Values["SKU"].Should().Be("BRD-700");
    }

    [Fact]
    public async Task Reads_rows_top_down()
    {
        // A PDF's origin is the bottom-left corner, so the top of the page is the largest Y. Sorting
        // the other way silently reverses somebody's price list.
        using MemoryStream file = Pdf(
            [
                (60, 700, "SKU"),
                (60, 680, "FIRST"),
                (60, 660, "SECOND"),
                (60, 640, "THIRD"),
            ]);

        ImportSheet sheet = await Reader.ReadAsync(file, new ImportReadOptions());

        sheet.Rows.Select(row => row.Values["SKU"]).Should().Equal("FIRST", "SECOND", "THIRD");
    }

    [Fact]
    public async Task Joins_a_two_word_heading_into_one_column()
    {
        // "Unit Price" is one column, not two. Splitting it would split the data beneath it down the
        // middle.
        using MemoryStream file = Pdf(
            [
                (60, 700, "SKU"), (200, 700, "Unit"), (228, 700, "Price"),
                (60, 680, "MILK-1L"), (200, 680, "18.99"),
            ]);

        ImportSheet sheet = await Reader.ReadAsync(file, new ImportReadOptions());

        sheet.Headers.Should().Equal("SKU", "Unit Price");
        sheet.Rows[0].Values["Unit Price"].Should().Be("18.99");
    }

    [Fact]
    public async Task Refuses_a_pdf_with_no_text_layer()
    {
        // A scan. Refused with a message a person can act on, rather than imported as zero rows —
        // "your file contained nothing" is a lie they cannot do anything about. OCR is deferred.
        using MemoryStream file = Pdf([]);

        Func<Task> read = () => Reader.ReadAsync(file, new ImportReadOptions());

        await read.Should().ThrowAsync<ImportSourceException>()
            .Where(exception => exception.Code == "IMPORTS_PDF_HAS_NO_TEXT_LAYER");
    }

    [Fact]
    public async Task Refuses_bytes_that_are_not_a_pdf()
    {
        using MemoryStream file = new("code,name\nACME,Acme\n"u8.ToArray());

        Func<Task> read = () => Reader.ReadAsync(file, new ImportReadOptions());

        await read.Should().ThrowAsync<ImportSourceException>()
            .Where(exception => exception.Code == "IMPORTS_SOURCE_UNREADABLE");
    }

    [Fact]
    public void The_deferred_ocr_extractor_reports_itself_unavailable()
    {
        // The PDF reader branches on this, and the honest answer is what makes the refusal above say
        // "OCR is not available in this build" rather than blaming the file (CLAUDE.md §1).
        new UnavailableOcrTextExtractor().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task The_deferred_ocr_extractor_refuses_to_be_called()
    {
        Func<Task> extract = () => new UnavailableOcrTextExtractor().ExtractAsync(ReadOnlyMemory<byte>.Empty);

        await extract.Should().ThrowAsync<NotSupportedException>();
    }

    /// <summary>Writes a one-page PDF with each word at a stated point on the page.</summary>
    /// <param name="words">The words, as (x, y from the bottom, text).</param>
    private static MemoryStream Pdf(IReadOnlyList<(double X, double Y, string Text)> words)
    {
        PdfDocumentBuilder builder = new();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(595, 842);

        foreach ((double x, double y, string text) in words)
        {
            page.AddText(text, 10, new PdfPoint(x, y), font);
        }

        return new MemoryStream(builder.Build());
    }
}
