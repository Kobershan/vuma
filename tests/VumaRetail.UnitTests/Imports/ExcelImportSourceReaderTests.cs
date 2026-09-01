using ClosedXML.Excel;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;
using VumaRetail.Imports.Readers;

namespace VumaRetail.UnitTests.Imports;

/// <summary>
/// The three ways a spreadsheet is not a table, asserted against a real workbook.
/// </summary>
/// <remarks>
/// The fixtures are built in-test by ClosedXML and read straight back, rather than checked in as
/// binary files. A checked-in <c>.xlsx</c> nobody can regenerate is a fixture that slowly stops
/// describing the case it was made for; a workbook built four lines above the assertion is one a
/// person can change while they are reading the failure.
/// </remarks>
public sealed class ExcelImportSourceReaderTests
{
    private static readonly ExcelImportSourceReader Reader = new();

    [Fact]
    public async Task Reads_headers_and_rows()
    {
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "code";
            sheet.Cell(1, 2).Value = "name";
            sheet.Cell(2, 1).Value = "ACME";
            sheet.Cell(2, 2).Value = "Acme Wholesalers";
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Headers.Should().Equal("code", "name");
        read.Rows.Should().HaveCount(1);
        read.Rows[0].RowNumber.Should().Be(2);
        read.Rows[0].Values["name"].Should().Be("Acme Wholesalers");
    }

    [Fact]
    public async Task Reads_a_formula_as_its_cached_value()
    {
        // A supplier's sheet computes the sell price from cost and margin, so the cell holds a
        // formula and not a number. What the file is telling us is what Excel last showed the person
        // who saved it.
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "cost";
            sheet.Cell(1, 2).Value = "price";
            sheet.Cell(2, 1).Value = 10m;
            sheet.Cell(2, 2).FormulaA1 = "A2*1.35";
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Rows[0].Values["price"].Should().Be("13.5");
    }

    [Fact]
    public async Task Reads_a_date_cell_as_an_iso_date_rather_than_a_serial_number()
    {
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "code";
            sheet.Cell(1, 2).Value = "effectiveFrom";
            sheet.Cell(2, 1).Value = "ACME";
            sheet.Cell(2, 2).Value = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Rows[0].Values["effectiveFrom"].Should().Be("2026-03-04");
    }

    [Fact]
    public async Task Keeps_a_leading_zero_on_a_barcode_stored_as_text()
    {
        // A barcode column is text far more often than not, precisely because a leading zero is
        // meaningful and coercing the cell to a number eats it.
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "barcode";
            sheet.Cell(2, 1).SetValue("0060012345678");
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Rows[0].Values["barcode"].Should().Be("0060012345678");
    }

    [Fact]
    public async Task Reads_a_price_as_the_number_that_was_typed()
    {
        // 19.99 is a binary double that round-trips to 19.989999999999998 at full precision, and a
        // price list imported at that value fails every equality assertion a person makes about it.
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "price";
            sheet.Cell(2, 1).Value = 19.99;
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Rows[0].Values["price"].Should().Be("19.99");
    }

    [Fact]
    public async Task Reads_the_named_worksheet()
    {
        using MemoryStream file = Workbook(
            first =>
            {
                first.Cell(1, 1).Value = "wrong";
                first.Cell(2, 1).Value = "no";
            },
            ("Prices", second =>
            {
                second.Cell(1, 1).Value = "code";
                second.Cell(2, 1).Value = "ACME";
            }));

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions(Worksheet: "Prices"));

        read.Headers.Should().Equal("code");
        read.Rows[0].Values["code"].Should().Be("ACME");
    }

    [Fact]
    public async Task Refuses_a_worksheet_the_workbook_does_not_have()
    {
        using MemoryStream file = Workbook(sheet => sheet.Cell(1, 1).Value = "code");

        Func<Task> read = () => Reader.ReadAsync(file, new ImportReadOptions(Worksheet: "Nope"));

        await read.Should().ThrowAsync<ImportSourceException>()
            .Where(exception => exception.Code == "IMPORTS_WORKSHEET_NOT_FOUND");
    }

    [Fact]
    public async Task Skips_rows_a_deleted_range_left_behind()
    {
        // A sheet somebody has scrolled through and deleted rows from reports a used range well past
        // its last value. Importing those as required-field errors is a support call.
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "code";
            sheet.Cell(2, 1).Value = "ACME";
            sheet.Cell(9, 1).Value = "BETA";
        });

        ImportSheet read = await Reader.ReadAsync(file, new ImportReadOptions());

        read.Rows.Select(row => row.Values["code"]).Should().Equal("ACME", "BETA");
        read.Rows[1].RowNumber.Should().Be(9, "the row number is the one down the side of the sheet");
    }

    [Fact]
    public async Task Refuses_bytes_that_are_not_a_workbook()
    {
        using MemoryStream file = new("code,name\nACME,Acme\n"u8.ToArray());

        Func<Task> read = () => Reader.ReadAsync(file, new ImportReadOptions());

        await read.Should().ThrowAsync<ImportSourceException>()
            .Where(exception => exception.Code == "IMPORTS_SOURCE_UNREADABLE");
    }

    [Fact]
    public async Task Refuses_a_file_with_more_rows_than_one_batch_may_carry()
    {
        using MemoryStream file = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "code";

            for (int row = 2; row <= 8; row++)
            {
                sheet.Cell(row, 1).Value = $"ACME{row}";
            }
        });

        Func<Task> read = () => Reader.ReadAsync(file, new ImportReadOptions(MaxRows: 3));

        await read.Should().ThrowAsync<ImportRuleException>()
            .Where(exception => exception.Code == "IMPORTS_TOO_MANY_ROWS");
    }

    /// <summary>Builds a one-sheet workbook in memory.</summary>
    /// <param name="build">Fills the first sheet.</param>
    /// <param name="extra">Any further named sheets.</param>
    private static MemoryStream Workbook(
        Action<IXLWorksheet> build, params (string Name, Action<IXLWorksheet> Build)[] extra)
    {
        using XLWorkbook workbook = new();

        build(workbook.AddWorksheet("Sheet1"));

        foreach ((string name, Action<IXLWorksheet> fill) in extra)
        {
            fill(workbook.AddWorksheet(name));
        }

        MemoryStream stream = new();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }
}
