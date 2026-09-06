using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>EF Core implementation of <see cref="IStagingQuantityReader"/> (Stage 08c).</summary>
/// <param name="context">The company database context to read bins from.</param>
/// <remarks>
/// ADR-114: a staging area is a bin of type <c>Staging</c>; stock in one is on hand and not
/// available. This sums the on-hand quantity sitting in staging bins at the location for the
/// stock-keeping unit. A bin row in an unexpected unit of measure fails loudly rather than being
/// skipped: silently dropping staged stock overstates available, which is exactly the defect
/// this stage exists to prevent.
/// </remarks>
public sealed class EfStagingQuantityReader(VumaRetailDbContext context) : IStagingQuantityReader
{
    /// <inheritdoc />
    public async Task<Quantity> ReadStagingAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitOfMeasure);

        List<Guid> stagingBinIds = await context.Bins
            .AsNoTracking()
            .Where(bin => bin.LocationId == locationId && bin.Type == Domain.Warehouse.BinType.Staging)
            .Select(bin => bin.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stagingBinIds.Count == 0)
        {
            return Quantity.Zero(unitOfMeasure);
        }

        List<(decimal Value, string Unit)> staged = await context.BinStocks
            .AsNoTracking()
            .Where(stock => stagingBinIds.Contains(stock.BinId)
                && stock.ItemId == itemId
                && stock.ItemVariantId == itemVariantId)
            .Select(stock => new ValueTuple<decimal, string>(stock.QuantityOnHand.Value, stock.QuantityOnHand.UnitOfMeasure))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Quantity total = Quantity.Zero(unitOfMeasure);
        foreach ((decimal value, string unit) in staged)
        {
            if (!string.Equals(unit, unitOfMeasure, StringComparison.Ordinal))
            {
                throw InventoryRuleException.UnitOfMeasureMismatch(unitOfMeasure, unit);
            }

            total += new Quantity(value, unit);
        }

        return total;
    }
}
