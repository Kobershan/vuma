namespace ZenithRetail.Domain.Primitives;

/// <summary>
/// An amount paired with the currency it is denominated in. Stored as <c>decimal(18,4)</c>.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md §7 rule 4: money is never a <see cref="double"/> and never a bare <see cref="decimal"/>
/// without a currency. A decimal with no currency attached is how a multi-currency system quietly adds
/// rands to dollars; making the currency part of the type means the compiler stops that instead of a
/// reviewer noticing it.
/// </para>
/// <para>
/// Arithmetic between two different currencies throws. There is deliberately no implicit conversion —
/// converting requires an explicit rate and a rate date, which is Stage 07's job, not this type's.
/// </para>
/// <para>
/// Scale is 4, not 2, so that unit prices, tax fractions and landed-cost apportionment keep their
/// precision through a calculation. Rounding to the currency's own scale happens once, at the point a
/// document line is finalised. See ADR-033 for the midpoint rule.
/// </para>
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>The scale every stored amount is held at — <c>decimal(18,4)</c>.</summary>
    public const int Scale = 4;

    /// <summary>
    /// ADR-033: midpoints round away from zero, matching what a till receipt and a customer's
    /// arithmetic do. Banker's rounding is correct for statistics and wrong for a shop counter.
    /// </summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    /// <summary>Creates an amount in the given currency.</summary>
    /// <param name="amount">The amount. Rounded to <see cref="Scale"/> decimal places.</param>
    /// <param name="currency">ISO 4217 alphabetic code, for example <c>ZAR</c>.</param>
    /// <exception cref="ArgumentException">The currency code is not three letters.</exception>
    public Money(decimal amount, string currency)
    {
        Currency = NormaliseCurrency(currency);
        Amount = decimal.Round(amount, Scale, Rounding);
    }

    /// <summary>The amount, at <see cref="Scale"/> decimal places.</summary>
    public decimal Amount { get; }

    /// <summary>The ISO 4217 alphabetic currency code, upper case.</summary>
    public string Currency { get; }

    /// <summary>Zero in the given currency.</summary>
    /// <param name="currency">ISO 4217 alphabetic code.</param>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is below zero — a refund, credit or reversal.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Adds two amounts in the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right)
        => new(left.Amount + right.EnsureSameCurrencyAs(left), left.Currency);

    /// <summary>Subtracts two amounts in the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right)
        => new(left.Amount - right.EnsureSameCurrencyAs(left), left.Currency);

    /// <summary>Negates an amount — a reversal, credit or refund.</summary>
    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    /// <summary>Scales an amount — by a quantity, a tax rate or a discount factor.</summary>
    public static Money operator *(Money left, decimal factor) => new(left.Amount * factor, left.Currency);

    /// <summary>Scales an amount — by a quantity, a tax rate or a discount factor.</summary>
    public static Money operator *(decimal factor, Money right) => right * factor;

    /// <summary>Divides an amount, for unit pricing and apportionment.</summary>
    /// <exception cref="DivideByZeroException">The divisor is zero.</exception>
    public static Money operator /(Money left, decimal divisor)
        => divisor == 0m
            ? throw new DivideByZeroException("Cannot divide a monetary amount by zero.")
            : new Money(left.Amount / divisor, left.Currency);

    /// <summary>True when the left amount exceeds the right. Comparing across currencies throws.</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>True when the left amount is below the right. Comparing across currencies throws.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>True when the left amount is at least the right. Comparing across currencies throws.</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>True when the left amount is at most the right. Comparing across currencies throws.</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Orders two amounts. Comparing across currencies throws.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public int CompareTo(Money other) => Amount.CompareTo(other.EnsureSameCurrencyAs(this));

    /// <summary>
    /// Rounds to a currency's own presentation scale — 2 for most currencies, 0 for the likes of JPY.
    /// Call this once when a line or document is finalised, never in the middle of a calculation.
    /// </summary>
    /// <param name="decimals">The number of decimal places the currency presents.</param>
    public Money RoundToCurrencyScale(int decimals = 2)
        => new(decimal.Round(Amount, decimals, Rounding), Currency);

    /// <summary>Returns the amount and its currency, for example <c>1234.5000 ZAR</c>.</summary>
    public override string ToString() => $"{Amount.ToString($"F{Scale}")} {Currency}";

    private decimal EnsureSameCurrencyAs(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine {other.Currency} and {Currency} amounts. Convert one of them with an " +
                "explicit exchange rate and rate date first.");
        }

        return Amount;
    }

    private static string NormaliseCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("A currency code is required — money without a currency is a bug.", nameof(currency));
        }

        string trimmed = currency.Trim().ToUpperInvariant();

        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                $"'{currency}' is not an ISO 4217 alphabetic currency code (three letters, for example ZAR).",
                nameof(currency));
        }

        return trimmed;
    }
}
