using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Queries;

/// <summary>A zone, as read back out.</summary>
public sealed record ZoneResult(Guid Id, Guid LocationId, string Code, string Name, ZoneType Type, bool IsActive);

/// <summary>Lists every zone at a location.</summary>
public sealed record ListZonesQuery(Guid LocationId) : IQuery<IReadOnlyList<ZoneResult>>;

/// <summary>Lists zones.</summary>
public sealed class ListZonesQueryHandler(IZoneRepository zones) : IQueryHandler<ListZonesQuery, IReadOnlyList<ZoneResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneResult>> HandleAsync(ListZonesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Zone> all = await zones.ListForLocationAsync(query.LocationId, cancellationToken).ConfigureAwait(false);

        return [.. all.Select(ToResult)];
    }

    internal static ZoneResult ToResult(Zone zone) => new(zone.Id, zone.LocationId, zone.Code, zone.Name, zone.Type, zone.IsActive);
}

/// <summary>A bin, as read back out.</summary>
public sealed record BinResult(
    Guid Id, Guid LocationId, Guid ZoneId, string Code, string Name, BinType Type, Quantity? Capacity, bool IsActive);

/// <summary>Lists every bin in a zone.</summary>
public sealed record ListBinsForZoneQuery(Guid ZoneId) : IQuery<IReadOnlyList<BinResult>>;

/// <summary>Lists a zone's bins.</summary>
public sealed class ListBinsForZoneQueryHandler(IBinRepository bins) : IQueryHandler<ListBinsForZoneQuery, IReadOnlyList<BinResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BinResult>> HandleAsync(ListBinsForZoneQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Bin> all = await bins.ListForZoneAsync(query.ZoneId, cancellationToken).ConfigureAwait(false);

        return [.. all.Select(ToResult)];
    }

    internal static BinResult ToResult(Bin bin)
        => new(bin.Id, bin.LocationId, bin.ZoneId, bin.Code, bin.Name, bin.Type, bin.Capacity, bin.IsActive);
}

/// <summary>Lists every active bin at a location.</summary>
public sealed record ListActiveBinsForLocationQuery(Guid LocationId) : IQuery<IReadOnlyList<BinResult>>;

/// <summary>Lists a location's active bins.</summary>
public sealed class ListActiveBinsForLocationQueryHandler(IBinRepository bins)
    : IQueryHandler<ListActiveBinsForLocationQuery, IReadOnlyList<BinResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BinResult>> HandleAsync(
        ListActiveBinsForLocationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Bin> all = await bins.ListActiveForLocationAsync(query.LocationId, cancellationToken).ConfigureAwait(false);

        return [.. all.Select(ListBinsForZoneQueryHandler.ToResult)];
    }
}

/// <summary>A bin's balance for one stock-keeping unit, as read back out.</summary>
/// <param name="BinId">The bin.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="QuantityOnHand">What is physically in the bin.</param>
/// <param name="QuantityReserved">Spoken for by a released, unconfirmed pick allocation (§7 rule 21).</param>
/// <param name="Available">What the allocator may still promise — <c>QuantityOnHand - QuantityReserved</c>.</param>
public sealed record BinStockResult(
    Guid BinId, Guid? ItemId, Guid? ItemVariantId, Quantity QuantityOnHand, Quantity QuantityReserved, Quantity Available);

/// <summary>Reads one stock-keeping unit's balance in one bin.</summary>
public sealed record GetBinStockQuery(Guid BinId, Guid? ItemId, Guid? ItemVariantId) : IQuery<BinStockResult>;

/// <summary>Reads a bin balance.</summary>
public sealed class GetBinStockQueryHandler(IBinStockRepository binStocks) : IQueryHandler<GetBinStockQuery, BinStockResult>
{
    /// <inheritdoc />
    public async Task<BinStockResult> HandleAsync(GetBinStockQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        BinStock balance = await binStocks.FindAsync(query.BinId, query.ItemId, query.ItemVariantId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin stock", query.ItemId ?? query.ItemVariantId ?? Guid.Empty);

        return ToResult(balance);
    }

    internal static BinStockResult ToResult(BinStock balance)
        => new(balance.BinId, balance.ItemId, balance.ItemVariantId, balance.QuantityOnHand, balance.QuantityReserved, balance.Available);
}

/// <summary>Lists every balance held in one bin.</summary>
public sealed record ListBinStockForBinQuery(Guid BinId) : IQuery<IReadOnlyList<BinStockResult>>;

/// <summary>Lists a bin's balances.</summary>
public sealed class ListBinStockForBinQueryHandler(IBinStockRepository binStocks)
    : IQueryHandler<ListBinStockForBinQuery, IReadOnlyList<BinStockResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BinStockResult>> HandleAsync(
        ListBinStockForBinQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<BinStock> all = await binStocks.ListForBinAsync(query.BinId, cancellationToken).ConfigureAwait(false);

        return [.. all.Select(GetBinStockQueryHandler.ToResult)];
    }
}

/// <summary>A putaway task, as read back out.</summary>
public sealed record PutawayTaskResult(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    PutawayStatus Status,
    Guid? SuggestedBinId,
    Guid? ConfirmedBinId,
    Quantity ConfirmedQuantity,
    Quantity Remaining);

/// <summary>Reads one putaway task.</summary>
public sealed record GetPutawayTaskQuery(Guid PutawayTaskId) : IQuery<PutawayTaskResult>;

/// <summary>Reads a task.</summary>
public sealed class GetPutawayTaskQueryHandler(IPutawayTaskRepository putawayTasks)
    : IQueryHandler<GetPutawayTaskQuery, PutawayTaskResult>
{
    /// <inheritdoc />
    public async Task<PutawayTaskResult> HandleAsync(GetPutawayTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        PutawayTask task = await putawayTasks.FindAsync(query.PutawayTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("putaway task", query.PutawayTaskId);

        return ToResult(task);
    }

    internal static PutawayTaskResult ToResult(PutawayTask task) => new(
        task.Id, task.LocationId, task.ItemId, task.ItemVariantId, task.Quantity, task.Status,
        task.SuggestedBinId, task.ConfirmedBinId, task.ConfirmedQuantity, task.Remaining);
}

/// <summary>Lists every task still pending at a location.</summary>
public sealed record ListPendingPutawayTasksQuery(Guid LocationId) : IQuery<IReadOnlyList<PutawayTaskResult>>;

/// <summary>Lists pending tasks.</summary>
public sealed class ListPendingPutawayTasksQueryHandler(IPutawayTaskRepository putawayTasks)
    : IQueryHandler<ListPendingPutawayTasksQuery, IReadOnlyList<PutawayTaskResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PutawayTaskResult>> HandleAsync(
        ListPendingPutawayTasksQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<PutawayTask> all = await putawayTasks
            .ListPendingForLocationAsync(query.LocationId, cancellationToken)
            .ConfigureAwait(false);

        return [.. all.Select(GetPutawayTaskQueryHandler.ToResult)];
    }
}

/// <summary>One pick task within a wave, as read back out.</summary>
public sealed record PickTaskResult(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity RequestedQuantity,
    string OutboundReference,
    Guid? AllocatedBinId,
    Quantity? AllocatedQuantity,
    Quantity? PickedQuantity,
    PickTaskStatus Status);

/// <summary>A pick wave and its lines, as read back out.</summary>
public sealed record PickWaveResult(
    Guid Id,
    Guid LocationId,
    PickWaveStatus Status,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? PickedAt,
    DateTimeOffset? PackedAt,
    DateTimeOffset? ShippedAt,
    IReadOnlyList<PickTaskResult> Tasks);

/// <summary>Reads one wave and its lines.</summary>
public sealed record GetPickWaveQuery(Guid PickWaveId) : IQuery<PickWaveResult>;

/// <summary>Reads a wave.</summary>
public sealed class GetPickWaveQueryHandler(IPickWaveRepository waves) : IQueryHandler<GetPickWaveQuery, PickWaveResult>
{
    /// <inheritdoc />
    public async Task<PickWaveResult> HandleAsync(GetPickWaveQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        PickWave wave = await waves.FindAsync(query.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", query.PickWaveId);

        IReadOnlyList<PickTask> tasks = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        return new PickWaveResult(
            wave.Id, wave.LocationId, wave.Status, wave.ReleasedAt, wave.PickedAt, wave.PackedAt, wave.ShippedAt,
            [.. tasks.Select(task => new PickTaskResult(
                task.Id, task.ItemId, task.ItemVariantId, task.RequestedQuantity, task.OutboundReference,
                task.AllocatedBinId, task.AllocatedQuantity, task.PickedQuantity, task.Status))]);
    }
}

/// <summary>A wave's pack record, as read back out.</summary>
public sealed record PackTaskResult(Guid Id, Guid PickWaveId, int PackageCount, string? Note, DateTimeOffset PackedAt);

/// <summary>Reads a wave's pack record.</summary>
public sealed record GetPackTaskQuery(Guid PickWaveId) : IQuery<PackTaskResult>;

/// <summary>Reads a pack record.</summary>
public sealed class GetPackTaskQueryHandler(IPackTaskRepository packTasks) : IQueryHandler<GetPackTaskQuery, PackTaskResult>
{
    /// <inheritdoc />
    public async Task<PackTaskResult> HandleAsync(GetPackTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        PackTask task = await packTasks.FindForWaveAsync(query.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pack task", query.PickWaveId);

        return new PackTaskResult(task.Id, task.PickWaveId, task.PackageCount, task.Note, task.PackedAt);
    }
}

/// <summary>A wave's shipment confirmation, as read back out.</summary>
public sealed record ShipmentConfirmationResult(
    Guid Id, Guid PickWaveId, string? Carrier, string? TrackingNumber, DateTimeOffset ShippedAt);

/// <summary>Reads a wave's shipment confirmation.</summary>
public sealed record GetShipmentConfirmationQuery(Guid PickWaveId) : IQuery<ShipmentConfirmationResult>;

/// <summary>Reads a shipment confirmation.</summary>
public sealed class GetShipmentConfirmationQueryHandler(IShipmentConfirmationRepository shipments)
    : IQueryHandler<GetShipmentConfirmationQuery, ShipmentConfirmationResult>
{
    /// <inheritdoc />
    public async Task<ShipmentConfirmationResult> HandleAsync(
        GetShipmentConfirmationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ShipmentConfirmation shipment = await shipments.FindForWaveAsync(query.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("shipment confirmation", query.PickWaveId);

        return new ShipmentConfirmationResult(shipment.Id, shipment.PickWaveId, shipment.Carrier, shipment.TrackingNumber, shipment.ShippedAt);
    }
}

/// <summary>A recorded cycle count line, as read back out.</summary>
public sealed record CycleCountLineResult(
    Guid Id, Guid BinId, Guid? ItemId, Guid? ItemVariantId, Quantity SystemQuantity, Quantity CountedQuantity, Quantity Variance);

/// <summary>A cycle count and its lines so far, as read back out.</summary>
public sealed record CycleCountResult(
    Guid Id,
    Guid LocationId,
    Guid? ZoneId,
    CycleCountStatus Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? FinalizedAt,
    IReadOnlyList<CycleCountLineResult> Lines);

/// <summary>Reads a count and its lines.</summary>
public sealed record GetCycleCountQuery(Guid CycleCountId) : IQuery<CycleCountResult>;

/// <summary>Reads a count.</summary>
public sealed class GetCycleCountQueryHandler(ICycleCountRepository cycleCounts)
    : IQueryHandler<GetCycleCountQuery, CycleCountResult>
{
    /// <inheritdoc />
    public async Task<CycleCountResult> HandleAsync(GetCycleCountQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CycleCount count = await cycleCounts.FindAsync(query.CycleCountId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("cycle count", query.CycleCountId);

        IReadOnlyList<CycleCountLine> lines = await cycleCounts.ListLinesAsync(count.Id, cancellationToken).ConfigureAwait(false);

        return new CycleCountResult(
            count.Id, count.LocationId, count.ZoneId, count.Status, count.ScheduledAt, count.FinalizedAt,
            [.. lines.Select(line => new CycleCountLineResult(
                line.Id, line.BinId, line.ItemId, line.ItemVariantId, line.SystemQuantity, line.CountedQuantity, line.Variance))]);
    }
}
