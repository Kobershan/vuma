using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog.Queries;

/// <summary>A unit of measure as read back out of the catalog. Application's own shape (§7 rule 1).</summary>
/// <param name="Id">The unit's id.</param>
/// <param name="Code">The short code.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="Type">What kind of measure this is.</param>
/// <param name="BaseUnitOfMeasureId">The base unit this converts to, or <c>null</c> if this is itself a base unit.</param>
/// <param name="ConversionFactorToBase">How many base units one of this is worth.</param>
/// <param name="IsActive">Whether the unit may still be used on new items.</param>
public sealed record UnitOfMeasureResult(
    Guid Id,
    string Code,
    string Name,
    UnitOfMeasureType Type,
    Guid? BaseUnitOfMeasureId,
    decimal ConversionFactorToBase,
    bool IsActive);

/// <summary>
/// Lists every unit of measure in the tenant. Not paginated — a tenant's set of units of measure is a
/// handful of configuration rows, not a catalogue.
/// </summary>
public sealed record ListUnitsOfMeasureQuery : IQuery<IReadOnlyList<UnitOfMeasureResult>>;

/// <summary>Lists units of measure.</summary>
/// <param name="units">Unit-of-measure lookup.</param>
public sealed class ListUnitsOfMeasureQueryHandler(IUnitOfMeasureRepository units)
    : IQueryHandler<ListUnitsOfMeasureQuery, IReadOnlyList<UnitOfMeasureResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitOfMeasureResult>> HandleAsync(
        ListUnitsOfMeasureQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<UnitOfMeasure> found = await units.ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. found.Select(unit => new UnitOfMeasureResult(
                unit.Id,
                unit.Code,
                unit.Name,
                unit.Type,
                unit.BaseUnitOfMeasureId,
                unit.ConversionFactorToBase,
                unit.IsActive)),
        ];
    }
}
