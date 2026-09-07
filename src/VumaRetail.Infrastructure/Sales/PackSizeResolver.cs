using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Catalog;

namespace VumaRetail.Infrastructure.Sales;

/// <summary>
/// Resolves a sold quantity's pack snapshot from the tenant's unit-of-measure catalogue (ADR-112).
/// </summary>
/// <remarks>
/// A base unit reads bare (<c>Each</c>); a converting unit reads counted (<c>6 x Box of 12</c>).
/// Per-barcode pack definitions (a case code that is its own SKU alias) are a catalogue follow-up;
/// the snapshot column and this port already carry whatever that work resolves, so no document code
/// changes when it lands. An unknown code never refuses — it reads back verbatim, because a till
/// with a new unit must still trade.
/// </remarks>
/// <param name="units">The tenant's unit definitions.</param>
public sealed class PackSizeResolver(IUnitOfMeasureRepository units) : IPackSizeResolver
{
    /// <inheritdoc />
    public async Task<PackSizeSnapshot> ResolveAsync(
        Guid? itemId,
        Guid? itemVariantId,
        string uom,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uom);

        Domain.Catalog.UnitOfMeasure? unit = await units
            .FindByCodeAsync(uom.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (unit is null)
        {
            return new PackSizeSnapshot(uom.Trim());
        }

        if (unit.BaseUnitOfMeasureId is null || unit.ConversionFactorToBase == 1m)
        {
            return new PackSizeSnapshot(unit.Name);
        }

        string count = quantity % 1m == 0m
            ? ((long)quantity).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new PackSizeSnapshot(
            $"{count} x {unit.Name}",
            unit.ConversionFactorToBase,
            unit.Name);
    }
}
