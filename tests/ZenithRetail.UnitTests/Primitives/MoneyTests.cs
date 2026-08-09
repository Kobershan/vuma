using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.UnitTests.Primitives;

/// <summary>
/// CLAUDE.md §7 rule 4 and docs/TESTING.md §3. Money bugs are the most expensive kind in this product,
/// so the type is held to the strictest bar in the solution.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Holds_an_amount_at_four_decimal_places()
    {
        var price = new Money(19.99m, "ZAR");

        price.Amount.Should().Be(19.99m);
        price.Currency.Should().Be("ZAR");
        Money.Scale.Should().Be(4);
    }

    [Theory]
    [InlineData("zar", "ZAR")]
    [InlineData(" usd ", "USD")]
    [InlineData("Eur", "EUR")]
    public void Normalises_the_currency_code(string input, string expected)
        => new Money(1m, input).Currency.Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("R")]
    [InlineData("RAND")]
    [InlineData("Z4R")]
    public void Rejects_anything_that_is_not_an_ISO_4217_code(string currency)
    {
        Action creating = () => _ = new Money(1m, currency);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adds_and_subtracts_within_one_currency()
    {
        var a = new Money(10.50m, "ZAR");
        var b = new Money(4.25m, "ZAR");

        (a + b).Should().Be(new Money(14.75m, "ZAR"));
        (a - b).Should().Be(new Money(6.25m, "ZAR"));
        (-a).Should().Be(new Money(-10.50m, "ZAR"));
    }

    [Fact]
    public void Refuses_to_combine_two_currencies()
    {
        // The failure this prevents: a multi-currency tenant adding rands to dollars and producing a
        // total that is a number but not an amount.
        var rands = new Money(100m, "ZAR");
        var dollars = new Money(100m, "USD");

        Action adding = () => _ = rands + dollars;
        Action comparing = () => _ = rands > dollars;

        // Name both currencies, so whoever hits this knows which two collided without opening a debugger.
        string message = adding.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("ZAR").And.Contain("USD");

        comparing.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(2.005, 2.01)]
    [InlineData(2.015, 2.02)]
    [InlineData(-2.005, -2.01)]
    [InlineData(2.004, 2.00)]
    public void Rounds_midpoints_away_from_zero_at_currency_scale(decimal input, decimal expected)
    {
        // ADR-033. Banker's rounding would give 2.00 for 2.005, which is defensible statistically and
        // indefensible at a till when the customer checks the receipt.
        new Money(input, "ZAR").RoundToCurrencyScale().Amount.Should().Be(expected);
    }

    [Fact]
    public void Line_totals_reconcile_to_the_document_total_after_rounding()
    {
        // docs/TESTING.md §3: the invoice total must equal the sum of the rounded lines. Assert it
        // rather than assume it — this is the reconciliation that goes wrong quietly.
        Money[] lines =
        [
            new Money(3.335m, "ZAR").RoundToCurrencyScale(),
            new Money(3.335m, "ZAR").RoundToCurrencyScale(),
            new Money(3.330m, "ZAR").RoundToCurrencyScale(),
        ];

        Money total = lines.Aggregate(Money.Zero("ZAR"), (running, line) => running + line);

        total.Should().Be(new Money(10.01m, "ZAR"));
        total.Amount.Should().Be(lines.Sum(line => line.Amount));
    }

    [Fact]
    public void Multiplies_by_a_quantity_or_a_rate()
    {
        var unitPrice = new Money(12.50m, "ZAR");

        (unitPrice * 3m).Should().Be(new Money(37.50m, "ZAR"));
        (3m * unitPrice).Should().Be(new Money(37.50m, "ZAR"));
    }

    [Fact]
    public void Applies_a_VAT_inclusive_split_without_losing_a_cent()
    {
        // 15% inclusive is seed data, not code (ADR-014) — but the arithmetic still has to be right.
        var inclusive = new Money(114.99m, "ZAR");

        Money exclusive = (inclusive / 1.15m).RoundToCurrencyScale();
        Money vat = inclusive - exclusive;

        exclusive.Should().Be(new Money(99.99m, "ZAR"));
        vat.Should().Be(new Money(15.00m, "ZAR"));
        (exclusive + vat).Should().Be(inclusive);
    }

    [Fact]
    public void Refuses_division_by_zero()
    {
        Action dividing = () => _ = new Money(10m, "ZAR") / 0m;

        dividing.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void Reports_zero_and_negative_amounts()
    {
        Money.Zero("ZAR").IsZero.Should().BeTrue();
        new Money(-1m, "ZAR").IsNegative.Should().BeTrue("returns and reversals are negative amounts");
        new Money(1m, "ZAR").IsNegative.Should().BeFalse();
    }

    [Fact]
    public void Two_amounts_are_equal_only_when_the_currency_matches_too()
    {
        new Money(10m, "ZAR").Should().Be(new Money(10m, "ZAR"));
        new Money(10m, "ZAR").Should().NotBe(new Money(10m, "USD"));
    }

    [Fact]
    public void Renders_the_amount_with_its_currency()
        => new Money(1234.5m, "ZAR").ToString().Should().Be("1234.5000 ZAR");
}
