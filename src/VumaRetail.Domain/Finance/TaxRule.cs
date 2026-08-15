using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One tenant-configured tax rate, effective over a date range (CLAUDE.md §9 — tax is a rules
/// engine, never a constant).
/// </summary>
/// <remarks>
/// The `en-ZA` default seeds exactly one row — <c>STANDARD</c> at 15%, inclusive — and nothing else.
/// A second jurisdiction, a rate change, or an exempt/zero-rated code is a new row, never a code
/// change; a rate change is a new <see cref="TaxRule"/> with its own effective dates, not an edit to
/// this one, so a historical invoice's tax always recalculates to what it actually charged.
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class TaxRule : Entity
{
    private TaxRule(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private TaxRule()
    {
    }

    /// <summary>The code documents reference, for example <c>STANDARD</c>, <c>ZERO</c> or <c>EXEMPT</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>A description for the person configuring it.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The rate, as a fraction — <c>0.15</c> for 15%.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Whether a stated amount already includes this tax, or excludes it.</summary>
    public TaxTreatment Treatment { get; private set; }

    /// <summary>The first date this rule applies from, inclusive.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last date this rule applies to, inclusive — or <c>null</c> if still current.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>False once superseded or retired.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Defines a new tax rule.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The code documents reference.</param>
    /// <param name="name">A description.</param>
    /// <param name="rate">The rate, as a fraction.</param>
    /// <param name="treatment">Whether stated amounts are inclusive or exclusive of this tax.</param>
    /// <param name="effectiveFrom">The first date this applies from.</param>
    /// <param name="effectiveTo">The last date this applies to, or <c>null</c> if open-ended.</param>
    public static TaxRule Define(
        Guid tenantId,
        string code,
        string name,
        decimal rate,
        TaxTreatment treatment,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (rate < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A tax rate cannot be negative.");
        }

        if (effectiveTo is { } end && end < effectiveFrom)
        {
            throw new ArgumentException("A rule cannot end before it starts.", nameof(effectiveTo));
        }

        return new TaxRule(tenantId)
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Rate = rate,
            Treatment = treatment,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true,
        };
    }

    /// <summary>Whether this rule applies on the given date.</summary>
    /// <param name="date">The date to test.</param>
    public bool IsEffectiveOn(DateOnly date) => IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>Retires the rule. Existing documents that used it are unaffected.</summary>
    public void Deactivate() => IsActive = false;
}
