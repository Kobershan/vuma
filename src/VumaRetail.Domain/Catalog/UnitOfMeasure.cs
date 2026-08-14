using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Catalog;

/// <summary>
/// A unit an item can be counted, weighed or measured in — <c>EA</c>, <c>KG</c>, <c>BOX12</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every item names a base unit of measure by its <see cref="Code"/> (<c>Quantity.UnitOfMeasure</c> is
/// a code string, not a foreign key — <c>CONVENTIONS.md</c> §2 forbids a cross-schema reference and a
/// quantity travels with a sale line into modules this schema knows nothing about). This entity is the
/// tenant's definition of what a code means and how it converts, which a quantity on its own cannot
/// carry.
/// </para>
/// <para>
/// A unit either <em>is</em> a base unit — <see cref="BaseUnitOfMeasureId"/> is <c>null</c> and
/// <see cref="ConversionFactorToBase"/> is <c>1</c> — or converts to one by multiplication: three
/// <c>BOX12</c> is 36 <c>EA</c>. One level of conversion rather than a graph, because a graph needs a
/// path-finding query on every stock movement and no retailer's packaging needs more than one.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class UnitOfMeasure : Entity
{
    private UnitOfMeasure(Guid tenantId, string code, string name, UnitOfMeasureType type)
        : base(tenantId)
    {
        Code = code;
        Name = name;
        Type = type;
        ConversionFactorToBase = 1m;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private UnitOfMeasure()
    {
    }

    /// <summary>The short code items and quantities reference, for example <c>EA</c> or <c>BOX12</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The unit's name, for example "Each" or "Box of 12".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of measure this is. Conversion only ever happens within one kind.</summary>
    public UnitOfMeasureType Type { get; private set; }

    /// <summary>The base unit this converts to, or <c>null</c> if this unit is itself a base unit.</summary>
    public Guid? BaseUnitOfMeasureId { get; private set; }

    /// <summary>
    /// How many base units one of this unit is worth. <c>1</c> for a base unit, and never anything else.
    /// </summary>
    public decimal ConversionFactorToBase { get; private set; }

    /// <summary>True while the unit may be used on new items. Existing quantities keep their unit.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Defines a base unit — one with nothing to convert to.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The short code, upper-cased.</param>
    /// <param name="name">The unit's name.</param>
    /// <param name="type">What kind of measure this is.</param>
    public static UnitOfMeasure CreateBase(Guid tenantId, string code, string name, UnitOfMeasureType type)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A unit of measure must belong to a tenant.", nameof(tenantId));
        }

        return new UnitOfMeasure(tenantId, Require(code, nameof(code)).ToUpperInvariant(), Require(name, nameof(name)), type);
    }

    /// <summary>
    /// Defines a unit that converts to another, for example a box of a dozen each. Takes its
    /// <see cref="Type"/> from <paramref name="baseUnit"/> — a unit converts within one kind of
    /// measure or it is not a conversion.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The short code, upper-cased.</param>
    /// <param name="name">The unit's name.</param>
    /// <param name="baseUnit">The unit this one converts to. Must itself be a base unit.</param>
    /// <param name="conversionFactorToBase">How many <paramref name="baseUnit"/> one of this is worth.</param>
    /// <exception cref="CatalogRuleException">
    /// <paramref name="baseUnit"/> is not itself a base unit, or the factor is not positive.
    /// </exception>
    public static UnitOfMeasure CreateDerived(
        Guid tenantId,
        string code,
        string name,
        UnitOfMeasure baseUnit,
        decimal conversionFactorToBase)
    {
        ArgumentNullException.ThrowIfNull(baseUnit);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A unit of measure must belong to a tenant.", nameof(tenantId));
        }

        if (!baseUnit.IsBaseUnit)
        {
            throw CatalogRuleException.UnitOfMeasureMustConvertToABaseUnit(baseUnit.Code);
        }

        if (conversionFactorToBase <= 0m)
        {
            throw CatalogRuleException.ConversionFactorMustBePositive();
        }

        return new UnitOfMeasure(
            tenantId,
            Require(code, nameof(code)).ToUpperInvariant(),
            Require(name, nameof(name)),
            baseUnit.Type)
        {
            BaseUnitOfMeasureId = baseUnit.Id,
            ConversionFactorToBase = conversionFactorToBase,
        };
    }

    /// <summary>True when this unit is a base unit — nothing to convert to.</summary>
    public bool IsBaseUnit => BaseUnitOfMeasureId is null;

    /// <summary>Renames the unit.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name) => Name = Require(name, nameof(name));

    /// <summary>Retires the unit from new items. Items already using it are unaffected.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired unit back for use on new items.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>What kind of measure a <see cref="UnitOfMeasure"/> is. Conversion never crosses kinds.</summary>
public enum UnitOfMeasureType
{
    /// <summary>Discrete items — each, dozen, box of twelve.</summary>
    Count = 0,

    /// <summary>Mass — grams, kilograms.</summary>
    Weight = 1,

    /// <summary>Fluid or spatial volume — millilitres, litres.</summary>
    Volume = 2,

    /// <summary>Length — metres, centimetres.</summary>
    Length = 3,
}
