using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>Reads availability — company-local (authoritative) and group (planning only).</summary>
/// <param name="locations">Location lookup on the acting company's database.</param>
/// <param name="balances">On-hand projection on the acting company's database.</param>
/// <param name="positions">Reservation positions on the acting company's database.</param>
/// <param name="skus">Resolves an item/variant to the unit of measure figures are read in.</param>
/// <param name="staging">Reads staging bins on the acting company's database.</param>
/// <param name="registry">Reads the registry projection for group views.</param>
/// <param name="options">Freshness threshold for stale contributors.</param>
/// <param name="clock">The only source of time.</param>
/// <remarks>
/// A query-side service: it opens no transaction and takes no locks. A point-in-time read is
/// exactly what planning needs; committing from it is what <c>IReservationService</c> exists to
/// prevent. Local and group reads return different types so the two can never be confused at a
/// call site.
/// </remarks>
public sealed class AvailabilityService(
    IStockLocationRepository locations,
    IStockBalanceRepository balances,
    IAvailableBalanceRepository positions,
    IStockKeepingUnitResolver skus,
    IStagingQuantityReader staging,
    IRegistryAvailabilityReader registry,
    IOptions<GroupAvailabilityOptions> options,
    IClock clock) : IAvailabilityService
{
    /// <inheritdoc />
    public async Task<LocalAvailability> GetLocalAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
    {
        StockLocation location = await locations.FindAsync(locationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", locationId);

        string unitOfMeasure = await skus
            .ResolveUnitOfMeasureCodeAsync(itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        StockBalance? balance = await balances
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        Quantity onHand = balance?.QuantityOnHand ?? Quantity.Zero(unitOfMeasure);
        if (balance is not null && !string.Equals(balance.QuantityOnHand.UnitOfMeasure, unitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(balance.QuantityOnHand.UnitOfMeasure, unitOfMeasure);
        }

        AvailableBalance? position = await positions
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        Quantity reserved = position?.Reserved ?? Quantity.Zero(unitOfMeasure);
        Quantity inStaging = await staging.ReadStagingAsync(locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
            .ConfigureAwait(false);

        return new LocalAvailability(
            locationId,
            itemId,
            itemVariantId,
            new AvailableToPromise(onHand, reserved, inStaging, Quantity.Zero(unitOfMeasure), clock.UtcNow));
    }

    /// <inheritdoc />
    public Task<GroupAvailabilityView> GetGroupAsync(
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => registry.ReadAsync(itemId, itemVariantId, options.Value.StaleAfter, cancellationToken);
}
