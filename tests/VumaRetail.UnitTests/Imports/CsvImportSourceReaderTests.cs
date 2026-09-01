using System.Text;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;
using VumaRetail.Imports.Readers;

namespace VumaRetail.UnitTests.Imports;

/// <summary>
/// RFC 4180, one corner case at a time (ADR-077).
/// </summary>
/// <remarks>
/// Every case here is one a real supplier file has produced: a company name with a comma in it, a
/// multi-line address, a 55" television, a sheet saved by Excel with a byte-order mark, a file edited
/// on two machines with mixed line endings, and a European export separated by semicolons because the
/// comma is that locale's decimal point.
/// </remarks>
public sealed class CsvImportSourceReaderTests
{
    private static readonly CsvImportSourceReader Reader = new();

    [Fact]
    public async Task Reads_a_plain_file()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,Acme Wholesalers\nBETA,Beta Supplies\n");

        sheet.Headers.Should().Equal("code", "name");
        sheet.Rows.Should().HaveCount(2);
        sheet.Rows[0].RowNumber.Should().Be(2, "the header is row 1, which is what the person's spreadsheet shows");
        sheet.Rows[0].Values["name"].Should().Be("Acme Wholesalers");
        sheet.Rows[1].Values["code"].Should().Be("BETA");
    }

    [Fact]
    public async Task Keeps_a_comma_inside_a_quoted_field()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,\"Smith, Jones & Son\"\n");

        sheet.Rows[0].Values["name"].Should().Be("Smith, Jones & Son");
    }

    [Fact]
    public async Task Keeps_a_newline_inside_a_quoted_field()
    {
        ImportSheet sheet = await ReadAsync("code,address\nACME,\"14 Long Street\nCape Town\"\nBETA,Elsewhere\n");

        sheet.Rows.Should().HaveCount(2, "a newline inside quotes is data, not a row break");
        sheet.Rows[0].Values["address"].Should().Be("14 Long Street\nCape Town");
        sheet.Rows[1].Values["code"].Should().Be("BETA");
    }

    [Fact]
    public async Task Reads_a_doubled_quote_as_one_literal_quote()
    {
        ImportSheet sheet = await ReadAsync("code,name\nTV55,\"55\"\" Television\"\n");

        sheet.Rows[0].Values["name"].Should().Be("55\" Television");
    }

    [Fact]
    public async Task Strips_a_byte_order_mark_from_the_first_header()
    {
        // Without this the first column is called "﻿code", matches no alias, and the import
        // fails as "no code column" on a file that plainly has one.
        ImportSheet sheet = await ReadAsync("﻿code,name\nACME,Acme\n");

        sheet.Headers[0].Should().Be("code");
    }

    [Fact]
    public async Task Reads_crlf_and_lf_in_the_same_file()
    {
        ImportSheet sheet = await ReadAsync("code,name\r\nACME,Acme\nBETA,Beta\r\n");

        sheet.Rows.Should().HaveCount(3 - 1);
        sheet.Rows.Select(row => row.Values["code"]).Should().Equal("ACME", "BETA");
    }

    [Fact]
    public async Task Treats_missing_trailing_cells_as_empty_rather_than_an_error()
    {
        ImportSheet sheet = await ReadAsync("code,name,email\nACME,Acme\n");

        sheet.Rows[0].Values["email"].Should().BeNull();
    }

    [Fact]
    public async Task Ignores_a_trailing_empty_line()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,Acme\n\n");

        sheet.Rows.Should().HaveCount(1, "every spreadsheet writes a trailing newline");
    }

    [Fact]
    public async Task Ignores_a_blank_line_in_the_middle()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,Acme\n,\nBETA,Beta\n");

        sheet.Rows.Select(row => row.Values["code"]).Should().Equal("ACME", "BETA");
    }

    [Fact]
    public async Task Keeps_the_source_row_number_across_a_skipped_blank_line()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,Acme\n,\nBETA,Beta\n");

        // BETA is on line 4 of the file the person is looking at, not line 3 of the rows we kept.
        sheet.Rows[1].RowNumber.Should().Be(4);
    }

    [Fact]
    public async Task Detects_a_semicolon_delimiter_from_the_header()
    {
        ImportSheet sheet = await ReadAsync("code;name;price\nACME;Acme;1.234,56\n");

        sheet.Headers.Should().Equal("code", "name", "price");
        sheet.Rows[0].Values["price"].Should().Be("1.234,56", "the reader reports the cell; parsing is validation's job");
    }

    [Fact]
    public async Task Detects_a_tab_delimiter()
    {
        ImportSheet sheet = await ReadAsync("code\tname\nACME\tAcme\n");

        sheet.Headers.Should().Equal("code", "name");
    }

    [Fact]
    public async Task Honours_an_explicit_delimiter_over_detection()
    {
        ImportSheet sheet = await ReadAsync("code|name\nACME|Acme\n", new ImportReadOptions(Delimiter: '|'));

        sheet.Headers.Should().Equal("code", "name");
    }

    [Fact]
    public async Task Does_not_let_a_quoted_comma_in_the_header_win_delimiter_detection()
    {
        ImportSheet sheet = await ReadAsync("code;\"name, trading\"\nACME;Acme\n");

        sheet.Headers.Should().Equal("code", "name, trading");
    }

    [Fact]
    public async Task Names_an_unnamed_column_rather_than_dropping_it()
    {
        // Dropping it would shift every header after it one column left, and the whole file would
        // import with every value in the wrong field.
        ImportSheet sheet = await ReadAsync("code,,name\nACME,x,Acme\n");

        sheet.Headers.Should().Equal("code", "column 2", "name");
        sheet.Rows[0].Values["name"].Should().Be("Acme");
    }

    [Fact]
    public async Task Drops_trailing_unnamed_columns()
    {
        ImportSheet sheet = await ReadAsync("code,name,,\nACME,Acme,,\n");

        sheet.Headers.Should().Equal("code", "name");
    }

    [Fact]
    public async Task Refuses_an_empty_file()
    {
        Func<Task> read = () => ReadAsync(string.Empty);

        await read.Should().ThrowAsync<ImportSourceException>()
            .Where(exception => exception.Code == "IMPORTS_SOURCE_UNREADABLE");
    }

    [Fact]
    public async Task Refuses_a_file_with_more_rows_than_one_batch_may_carry()
    {
        StringBuilder builder = new("code\n");

        for (int row = 0; row < 5; row++)
        {
            builder.Append("ACME").Append(row).Append('\n');
        }

        Func<Task> read = () => ReadAsync(builder.ToString(), new ImportReadOptions(MaxRows: 3));

        await read.Should().ThrowAsync<ImportRuleException>()
            .Where(exception => exception.Code == "IMPORTS_TOO_MANY_ROWS");
    }

    [Fact]
    public async Task Reads_a_file_with_no_trailing_newline()
    {
        ImportSheet sheet = await ReadAsync("code,name\nACME,Acme");

        sheet.Rows.Should().HaveCount(1);
        sheet.Rows[0].Values["name"].Should().Be("Acme");
    }

    [Fact]
    public async Task Looks_a_cell_up_case_insensitively()
    {
        ImportSheet sheet = await ReadAsync("Code,Name\nACME,Acme\n");

        sheet.Rows[0].Values["code"].Should().Be("ACME");
    }

    private static async Task<ImportSheet> ReadAsync(string content, ImportReadOptions? options = null)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));

        return await Reader.ReadAsync(stream, options ?? new ImportReadOptions());
    }
}
