namespace ZenithRetail.Domain.Primitives;

/// <summary>
/// An amount of stock paired with the unit of measure it is counted in. Stored as
/// <c>decimal(18,6)</c>.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md §7 rule 5. Six decimal places because retail weighs things: 0.001 kg matters at a deli
/// counter, and recipe/BOM explosion multiplies small fractions several levels deep before anything is
/// rounded.
/// </para>
/// <para>
/// Combining quantities in different units throws. Converting between units needs a conversion factor
/// from the unit-of-measure catalogue, which is Stage 06's job — this type will not guess that a case
/// is twelve eaches.
/// </para>
/// <para>
/// Negative quantities are legitimate and expected: returns, issues and reversals all produce them.
/// The stock ledger is append-only (ADR-005), so a correction is a negative entry rather than an edit.
/// </para>
/// </remarks>
public readonly record struct Quantity : IComparable<Quantity>
{
    /// <summary>The scale every stored quantity is held at — <c>decimal(18,6)</c>.</summary>
    public const int Scale = 6;

    /// <summary>Creates a quantity in the given unit of measure.</summary>
    /// <param name="value">The amount. Rounded to <see cref="Scale"/> decimal places.</param>
    /// <param name="unitOfMeasure">Unit-of-measure code, for example <c>EA</c>, <c>KG</c> or <c>CASE</c>.</param>
    /// <exception cref="ArgumentException">The unit of measure is missing.</exception>
    public Quantity(decimal value, string unitOfMeasure)
    {
        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw new ArgumentException(
                "A unit of measure is required — a bare quantity is ambiguous.", nameof(unitOfMeasure));
        }

        UnitOfMeasure = unitOfMeasure.Trim().ToUpperInvariant();
        Value = decimal.Round(value, Scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>The quantity, at <see cref="Scale"/> decimal places.</summary>
    public decimal Value { get; }

    /// <summary>The unit-of-measure code, upper case.</summary>
    public string UnitOfMeasure { get; }

    /// <summary>Zero in the given unit of measure.</summary>
    /// <param name="unitOfMeasure">Unit-of-measure code.</param>
    public static Quantity Zero(string unitOfMeasure) => new(0m, unitOfMeasure);

    /// <summary>True when the quantity is exactly zero.</summary>
    public bool IsZero => Value == 0m;

    /// <summary>True when the quantity is below zero — an issue, return or reversal.</summary>
    public bool IsNegative => Value < 0m;

    /// <summary>Adds two quantities in the same unit of measure.</summary>
    /// <exception cref="InvalidOperationException">The units of measure differ.</exception>
    public static Quantity operator +(Quantity left, Quantity right)
        => new(left.Value + right.EnsureSameUnitAs(left), left.UnitOfMeasure);

    /// <summary>Subtracts two quantities in the same unit of measure.</summary>
    /// <exception cref="InvalidOperationException">The units of measure differ.</exception>
    public static Quantity operator -(Quantity left, Quantity right)
        => new(left.Value - right.EnsureSameUnitAs(left), left.UnitOfMeasure);

    /// <summary>Negates a quantity — an issue, return or reversal.</summary>
    public static Quantity operator -(Quantity value) => new(-value.Value, value.UnitOfMeasure);

    /// <summary>Scales a quantity — by a BOM component factor or a pack size.</summary>
    public static Quantity operator *(Quantity left, decimal factor) => new(left.Value * factor, left.UnitOfMeasure);

    /// <summary>Scales a quantity — by a BOM component factor or a pack size.</summary>
    public static Quantity operator *(decimal factor, Quantity right) => right * factor;

    /// <summary>True when the left quantity exceeds the right. Comparing across units throws.</summary>
    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    /// <summary>True when the left quantity is below the right. Comparing across units throws.</summary>
    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    /// <summary>True when the left quantity is at least the right. Comparing across units throws.</summary>
    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    /// <summary>True when the left quantity is at most the right. Comparing across units throws.</summary>
    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    /// <summary>Orders two quantities. Comparing across units of measure throws.</summary>
    /// <exception cref="InvalidOperationException">The units of measure differ.</exception>
    public int CompareTo(Quantity other) => Value.CompareTo(other.EnsureSameUnitAs(this));

    /// <summary>
    /// Multiplies a quantity by a unit price to give a line value.
    /// </summary>
    /// <param name="unitPrice">The price of one unit, in the line's currency.</param>
    public Money Extend(Money unitPrice) => unitPrice * Value;

    /// <summary>Returns the quantity and its unit, for example <c>2.500000 KG</c>.</summary>
    public override string ToString() => $"{Value.ToString($"F{Scale}")} {UnitOfMeasure}";

    private decimal EnsureSameUnitAs(Quantity other)
    {
        if (!string.Equals(UnitOfMeasure, other.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine quantities in {other.UnitOfMeasure} and {UnitOfMeasure}. Convert one of " +
                "them using the unit-of-measure catalogue's conversion factor first.");
        }

        return Value;
    }
}
