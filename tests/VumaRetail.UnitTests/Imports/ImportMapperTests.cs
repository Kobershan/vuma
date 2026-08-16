using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.UnitTests.Imports;

/// <summary>
/// Auto-mapping: what it binds, and — more importantly — what it refuses to guess.
/// </summary>
public sealed class ImportMapperTests
{
    private static readonly ImportTargetDescriptor Target = new(
        ImportTargetKind.Suppliers,
        "Suppliers",
        "Test target.",
        RequiresStore: false,
        ["code"],
        [
            new("code", ImportFieldType.Text, true, "The code.", "ACME", ["supplier code", "account no"]),
            new("name", ImportFieldType.Text, true, "The name.", "Acme", ["supplier name", "company"]),
            new("email", ImportFieldType.Text, false, "The email.", "a@b.c", ["e-mail"]),
        ]);

    [Fact]
    public void Binds_a_header_that_matches_the_field_name()
    {
        IReadOnlyList<ImportTemplateBinding> bindings = ImportMapper.AutoMap(Target, ["code", "name"]);

        bindings.Select(binding => binding.TargetField).Should().Equal("code", "name");
        bindings[0].SourceColumn.Should().Be("code");
    }

    [Theory]
    [InlineData("Supplier Code")]
    [InlineData("supplier_code")]
    [InlineData("SUPPLIER-CODE")]
    [InlineData("suppliercode")]
    public void Binds_an_alias_regardless_of_case_spacing_and_punctuation(string header)
    {
        // Everything that is not a letter or a digit is stripped on both sides, which is why the four
        // ways a supplier writes one heading are one heading.
        IReadOnlyList<ImportTemplateBinding> bindings = ImportMapper.AutoMap(Target, [header, "name"]);

        bindings.Should().Contain(binding => binding.TargetField == "code" && binding.SourceColumn == header);
    }

    [Fact]
    public void Refuses_to_pick_between_two_columns_that_both_match_one_field()
    {
        // Binding the first would be a coin toss dressed up as a decision. Leaving it unbound sends
        // the person to the mapping screen, which is where the choice belongs.
        IReadOnlyList<ImportTemplateBinding> bindings =
            ImportMapper.AutoMap(Target, ["code", "supplier code", "name"]);

        bindings.Should().NotContain(binding => binding.TargetField == "code");
        ImportMapper.UnmappedRequiredFields(Target, bindings).Should().Equal("code");
    }

    [Fact]
    public void Gives_a_shared_column_to_the_required_field_first()
    {
        ImportTargetDescriptor target = Target with
        {
            Fields =
            [
                new("optional", ImportFieldType.Text, false, "x", "x", ["account no"]),
                .. Target.Fields,
            ],
        };

        IReadOnlyList<ImportTemplateBinding> bindings = ImportMapper.AutoMap(target, ["account no", "name"]);

        bindings.Should().Contain(binding => binding.TargetField == "code" && binding.SourceColumn == "account no");
        bindings.Should().NotContain(binding => binding.TargetField == "optional");
    }

    [Fact]
    public void Leaves_a_field_no_header_matches_unbound()
    {
        IReadOnlyList<ImportTemplateBinding> bindings = ImportMapper.AutoMap(Target, ["code", "name"]);

        bindings.Should().NotContain(binding => binding.TargetField == "email");
    }

    [Fact]
    public void Reports_the_required_fields_a_mapping_leaves_bound_to_nothing()
    {
        ImportMapper.UnmappedRequiredFields(Target, [new ImportTemplateBinding("code", "code", null)])
            .Should().Equal("name");
    }

    [Fact]
    public void Counts_a_constant_only_binding_as_bound()
    {
        // The case that makes real files work: the field is required and the file simply does not
        // have it, because that supplier quotes in one currency and everybody knows it.
        ImportMapper.UnmappedRequiredFields(
                Target,
                [
                    new ImportTemplateBinding("code", "code", null),
                    new ImportTemplateBinding("name", null, "Unknown supplier"),
                ])
            .Should().BeEmpty();
    }

    [Fact]
    public void Reports_a_binding_naming_a_field_the_target_does_not_have()
    {
        // Caught before the batch is touched. Left to validation, `unitprice` for `unitPrice` would
        // fail four thousand rows for a missing price and send somebody to stare at a spreadsheet
        // that is perfectly fine.
        ImportMapper.UnknownTargetFields(Target, [new ImportTemplateBinding("cod", "code", null)])
            .Should().Equal("cod");
    }

    [Fact]
    public void Finds_a_field_by_name_case_insensitively()
    {
        ImportMapper.FieldNamed(Target, "CODE")!.Name.Should().Be("code");
        ImportMapper.FieldNamed(Target, "nothing").Should().BeNull();
    }
}
