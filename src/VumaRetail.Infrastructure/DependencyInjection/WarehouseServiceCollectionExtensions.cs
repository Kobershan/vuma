using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Warehouse;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 13 <c>warehouse</c> module — zones, bins, bin stock, putaway, pick/pack/ship
/// and cycle counts.
/// </summary>
public static class WarehouseServiceCollectionExtensions
{
    /// <summary>
    /// Registers the warehouse repositories, the bin-stock mover, the pick allocation strategy, the
    /// module's permission declaration and its manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddVumaInventory</c>: every command handler here resolves Stage 08's
    /// <c>IStockLocationRepository</c> and <c>IStockLedgerPoster</c> to validate locations and post the
    /// movements that change what a location holds (business rules 2–4). No new financial event
    /// publisher is registered — a cycle count variance reaches the general ledger through the same
    /// <c>IInventoryValuationEventPublisher</c> binding Stage 08 already wired.
    /// </remarks>
    public static IServiceCollection AddVumaWarehouse(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IBinRepository, BinRepository>();
        services.AddScoped<IBinStockRepository, BinStockRepository>();
        services.AddScoped<IBinStockMovementRepository, BinStockMovementRepository>();
        services.AddScoped<IPutawayTaskRepository, PutawayTaskRepository>();
        services.AddScoped<IPickWaveRepository, PickWaveRepository>();
        services.AddScoped<IPackTaskRepository, PackTaskRepository>();
        services.AddScoped<IShipmentConfirmationRepository, ShipmentConfirmationRepository>();
        services.AddScoped<ICycleCountRepository, CycleCountRepository>();

        services.AddScoped<IBinStockMover, BinStockMover>();
        services.AddScoped<IPickAllocationStrategy, LargestBinFirstAllocationStrategy>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, WarehousePermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, WarehouseModuleManifest>());

        return services;
    }
}
