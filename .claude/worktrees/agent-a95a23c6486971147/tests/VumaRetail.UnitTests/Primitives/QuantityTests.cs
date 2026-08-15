using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Primitives;

/// <summary>CLAUDE.md §7 rule 5 — quantities carry their unit of measure.</summary>
public sealed class QuantityTests
{
    [Fact]
    public void Holds_a_value_at_six_decimal_places()
    {
        var weight = new Quantity(2.5m, "kg");

        weight.Value.Should().Be(2.5m);
        weight.UnitOfMeasure.Should().Be("KG");
        Quantity.Scale.Should().Be(6);
    }

    [Fact]
    public void Keeps_precision_a_deli_counter_needs()
    {
        new Quantity(0.001234m, "KG").Value.Should().Be(0.001234m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_unit_of_measure(string unit)
    {
        Action creating = () => _ = new Quantity(1m, unit);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adds_and_subtracts_within_one_unit()
    {
        var a = new Quantity(10m, "EA");
        var b = new Quantity(3m, "EA");

        (a + b).Should().Be(new Quantity(13m, "EA"));
        (a - b).Should().Be(new Quantity(7m, "EA"));
    }

    [Fact]
    public void Refuses_to_combine_two_units_of_measure()
    {
        // A case is not twelve eaches until the unit-of-measure catalogue says so (Stage 06). This type
        // will not guess.
        var eaches = new Quantity(12m, "EA");
        var cases = new Quantity(1m, "CASE");

        Action adding = () => _ = eaches + cases;

        string message = adding.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("EA").And.Contain("CASE");
    }

    [Fact]
    public void Allows_negative_quantities_for_issues_and_returns()
    {
        // The stock ledger is append-only (ADR-005) — a correction is a negative entry, never an edit.
        var issue = new Quantity(-5m, "EA");

        issue.IsNegative.Should().BeTrue();
        (new Quantity(10m, "EA") + issue).Should().Be(new Quantity(5m, "EA"));
    }

    [Fact]
    public void Extends_a_unit_price_into_a_line_value()
    {
        var weight = new Quantity(2.5m, "KG");

        weight.Extend(new Money(89.99m, "ZAR")).Should().Be(new Money(224.975m, "ZAR"));
    }

    [Fact]
    public void Orders_quantities_in_the_same_unit()
    {
        (new Quantity(5m, "EA") > new Quantity(3m, "EA")).Should().BeTrue();
        (new Quantity(3m, "EA") <= new Quantity(3m, "EA")).Should().BeTrue();
    }

    [Fact]
    public void Renders_the_value_with_its_unit()
        => new Quantity(2.5m, "KG").ToString().Should().Be("2.500000 KG");
}
