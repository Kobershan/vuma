using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IZoneRepository"/>.</summary>
public sealed class ZoneRepository(VumaRetailDbContext context) : IZoneRepository
{
    /// <inheritdoc />
    public Task<Zone?> FindAsync(Guid zoneId, CancellationToken cancellationToken = default)
        => context.Zones.FirstOrDefaultAsync(zone => zone.Id == zoneId, cancellationToken);

    /// <inheritdoc />
    public Task<Zone?> FindByCodeAsync(Guid locationId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Zones.FirstOrDefaultAsync(
            zone => zone.LocationId == locationId && zone.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Zone>> ListForLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
        => await context.Zones
            .AsNoTracking()
            .Where(zone => zone.LocationId == locationId)
            .OrderBy(zone => zone.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Zone zone) => context.Zones.Add(zone);
}

/// <summary>EF Core implementation of <see cref="IBinRepository"/>.</summary>
public sealed class BinRepository(VumaRetailDbContext context) : IBinRepository
{
    /// <inheritdoc />
    public Task<Bin?> FindAsync(Guid binId, CancellationToken cancellationToken = default)
        => context.Bins.FirstOrDefaultAsync(bin => bin.Id == binId, cancellationToken);

    /// <inheritdoc />
    public Task<Bin?> FindByCodeAsync(Guid locationId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Bins.FirstOrDefaultAsync(
            bin => bin.LocationId == locationId && bin.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bin>> ListForZoneAsync(Guid zoneId, CancellationToken cancellationToken = default)
        => await context.Bins
            .AsNoTracking()
            .Where(bin => bin.ZoneId == zoneId)
            .OrderBy(bin => bin.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bin>> ListActiveForLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
        => await context.Bins
            .AsNoTracking()
            .Where(bin => bin.LocationId == locationId && bin.IsActive)
            .OrderBy(bin => bin.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Bin bin) => context.Bins.Add(bin);
}

/// <summary>EF Core implementation of <see cref="IBinStockRepository"/>.</summary>
public sealed class BinStockRepository(VumaRetailDbContext context) : IBinStockRepository
{
    /// <inheritdoc />
    /// <remarks>Tracked: every caller is about to mutate the balance it gets back.</remarks>
    public Task<BinStock?> FindAsync(Guid binId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
        => context.BinStocks.FirstOrDefaultAsync(
            stock => stock.BinId == binId && stock.ItemId == itemId && stock.ItemVariantId == itemVariantId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BinStock>> ListCandidatesAsync(
        Guid locationId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
        => await (
            from stock in context.BinStocks.AsNoTracking()
            join bin in context.Bins.AsNoTracking() on stock.BinId equals bin.Id
            where bin.LocationId == locationId && bin.IsActive
                && stock.ItemId == itemId && stock.ItemVariantId == itemVariantId
            orderby stock.QuantityOnHand.Value descending
            select stock)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BinStock>> ListForBinAsync(Guid binId, CancellationToken cancellationToken = default)
        => await context.BinStocks
            .AsNoTracking()
            .Where(stock => stock.BinId == binId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(BinStock balance) => context.BinStocks.Add(balance);
}

/// <summary>EF Core implementation of <see cref="IBinStockMovementRepository"/>.</summary>
/// <remarks>Append-only, the same shape <c>StockLedgerRepository</c> uses — no update or remove surface.</remarks>
public sealed class BinStockMovementRepository(VumaRetailDbContext context) : IBinStockMovementRepository
{
    /// <inheritdoc />
    public void Add(BinStockMovement movement) => context.BinStockMovements.Add(movement);
}

/// <summary>EF Core implementation of <see cref="IPutawayTaskRepository"/>.</summary>
public sealed class PutawayTaskRepository(VumaRetailDbContext context) : IPutawayTaskRepository
{
    /// <inheritdoc />
    public Task<PutawayTask?> FindAsync(Guid putawayTaskId, CancellationToken cancellationToken = default)
        => context.PutawayTasks.FirstOrDefaultAsync(task => task.Id == putawayTaskId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PutawayTask>> ListPendingForLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
        => await context.PutawayTasks
            .AsNoTracking()
            .Where(task => task.LocationId == locationId && task.Status == PutawayStatus.Pending)
            .OrderBy(task => task.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(PutawayTask task) => context.PutawayTasks.Add(task);
}

/// <summary>EF Core implementation of <see cref="IPickWaveRepository"/>.</summary>
public sealed class PickWaveRepository(VumaRetailDbContext context) : IPickWaveRepository
{
    /// <inheritdoc />
    public Task<PickWave?> FindAsync(Guid pickWaveId, CancellationToken cancellationToken = default)
        => context.PickWaves.FirstOrDefaultAsync(wave => wave.Id == pickWaveId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickTask>> ListTasksAsync(Guid pickWaveId, CancellationToken cancellationToken = default)
        => await context.PickTasks
            .Where(task => task.PickWaveId == pickWaveId)
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<PickTask?> FindTaskAsync(Guid pickTaskId, CancellationToken cancellationToken = default)
        => context.PickTasks.FirstOrDefaultAsync(task => task.Id == pickTaskId, cancellationToken);

    /// <inheritdoc />
    public void AddWave(PickWave wave) => context.PickWaves.Add(wave);

    /// <inheritdoc />
    public void AddTask(PickTask task) => context.PickTasks.Add(task);
}

/// <summary>EF Core implementation of <see cref="IPackTaskRepository"/>.</summary>
public sealed class PackTaskRepository(VumaRetailDbContext context) : IPackTaskRepository
{
    /// <inheritdoc />
    public Task<PackTask?> FindForWaveAsync(Guid pickWaveId, CancellationToken cancellationToken = default)
        => context.PackTasks.FirstOrDefaultAsync(task => task.PickWaveId == pickWaveId, cancellationToken);

    /// <inheritdoc />
    public void Add(PackTask task) => context.PackTasks.Add(task);
}

/// <summary>EF Core implementation of <see cref="IShipmentConfirmationRepository"/>.</summary>
public sealed class ShipmentConfirmationRepository(VumaRetailDbContext context) : IShipmentConfirmationRepository
{
    /// <inheritdoc />
    public Task<ShipmentConfirmation?> FindForWaveAsync(Guid pickWaveId, CancellationToken cancellationToken = default)
        => context.ShipmentConfirmations.FirstOrDefaultAsync(shipment => shipment.PickWaveId == pickWaveId, cancellationToken);

    /// <inheritdoc />
    public void Add(ShipmentConfirmation shipment) => context.ShipmentConfirmations.Add(shipment);
}

/// <summary>EF Core implementation of <see cref="ICycleCountRepository"/>.</summary>
public sealed class CycleCountRepository(VumaRetailDbContext context) : ICycleCountRepository
{
    /// <inheritdoc />
    public Task<CycleCount?> FindAsync(Guid cycleCountId, CancellationToken cancellationToken = default)
        => context.CycleCounts.FirstOrDefaultAsync(count => count.Id == cycleCountId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CycleCountLine>> ListLinesAsync(Guid cycleCountId, CancellationToken cancellationToken = default)
        => await context.CycleCountLines
            .Where(line => line.CycleCountId == cycleCountId)
            .OrderBy(line => line.CreatedAt)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<CycleCountLine?> FindLineAsync(
        Guid cycleCountId, Guid binId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
        => context.CycleCountLines.FirstOrDefaultAsync(
            line => line.CycleCountId == cycleCountId && line.BinId == binId
                && line.ItemId == itemId && line.ItemVariantId == itemVariantId,
            cancellationToken);

    /// <inheritdoc />
    public void AddCount(CycleCount count) => context.CycleCounts.Add(count);

    /// <inheritdoc />
    public void AddLine(CycleCountLine line) => context.CycleCountLines.Add(line);
}
