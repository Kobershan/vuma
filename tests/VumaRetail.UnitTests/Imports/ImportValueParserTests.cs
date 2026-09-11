using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.UnitTests.Imports;

/// <summary>Meaningful format and validation cases for the import cell parser.</summary>
public sealed class ImportValueParserTests
{
    [Fact]
    public void Empty_required_cell_is_reported_but_empty_optional_cell_is_allowed()
    {
        ImportValueParser.Parse(Field(ImportFieldType.Text, required: true), " ").Error!.Code
            .Should().Be("IMPORTS_FIELD_REQUIRED");

        ImportValueParser.Parse(Field(ImportFieldType.Text), null).Should().Be(ImportParsedValue.Empty);
    }

    [Fact]
    public void Text_is_trimmed_and_numbers_accept_currency_grouping_and_parentheses()
    {
        ImportValueParser.Parse(Field(ImportFieldType.Text), "  ACME  ").Value.Should().Be("ACME");
        ImportValueParser.Parse(Field(ImportFieldType.Money), "R 1 234,56").Value.Should().Be("1234.56");
        ImportValueParser.Parse(Field(ImportFieldType.Money), "(£1,234.56)").Value.Should().Be("-1234.56");
        ImportValueParser.Parse(Field(ImportFieldType.Quantity), "1.234.567").Value.Should().Be("1234567");
    }

    [Fact]
    public void Number_parser_rejects_bad_grouping_and_integer_fraction()
    {
        ImportValueParser.Parse(Field(ImportFieldType.Decimal), "1,23,456").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
        ImportValueParser.Parse(Field(ImportFieldType.Integer), "12.5").Error!.Code
            .Should().Be("IMPORTS_FIELD_OUT_OF_RANGE");
        ImportValueParser.Parse(Field(ImportFieldType.Decimal), "R").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
    }

    [Theory]
    [InlineData("2026-04-03", "2026-04-03")]
    [InlineData("03/04/2026", "2026-04-03")]
    [InlineData("03 Apr 2026", "2026-04-03")]
    [InlineData("46115", "2026-04-03")]
    public void Dates_accept_supplier_formats_and_excel_serials(string raw, string expected)
    {
        ImportValueParser.Parse(Field(ImportFieldType.Date), raw).Value.Should().Be(expected);
    }

    [Fact]
    public void Invalid_date_and_out_of_range_excel_serial_are_rejected()
    {
        ImportValueParser.Parse(Field(ImportFieldType.Date), "31/02/2026").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
        ImportValueParser.Parse(Field(ImportFieldType.Date), "60").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
    }

    [Theory]
    [InlineData("YES", "true")]
    [InlineData("on", "true")]
    [InlineData("0", "false")]
    [InlineData("OFF", "false")]
    public void Booleans_accept_documented_supplier_spellings(string raw, string expected)
    {
        ImportParsedValue parsed = ImportValueParser.Parse(Field(ImportFieldType.Boolean), raw);

        parsed.Value.Should().Be(expected);
        ImportValueParser.ToBoolean(parsed.Value!).Should().Be(expected == "true");
    }

    [Fact]
    public void Unknown_boolean_and_unknown_type_are_rejected()
    {
        ImportValueParser.Parse(Field(ImportFieldType.Boolean), "maybe").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
        ImportValueParser.Parse(Field((ImportFieldType)999), "value").Error!.Code
            .Should().Be("IMPORTS_FIELD_NOT_PARSEABLE");
    }

    [Fact]
    public void Canonical_values_round_trip_through_the_public_converters()
    {
        ImportValueParser.ToDecimal("1234.50").Should().Be(1234.50m);
        ImportValueParser.ToDate("2026-04-03").Should().Be(new DateOnly(2026, 4, 3));
        ImportValueParser.ToBoolean("false").Should().BeFalse();
    }

    private static ImportFieldDescriptor Field(ImportFieldType type, bool required = false)
        => new("value", type, required, "Test field", "example", []);
}
