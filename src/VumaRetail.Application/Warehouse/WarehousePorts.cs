using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse;

/// <summary>Reads and writes <see cref="Zone"/> rows.</summary>
public interface IZoneRepository
{
    /// <summary>Finds a zone by id.</summary>
    Task<Zone?> FindAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Finds a zone by its code within a location, case-insensitively.</summary>
    Task<Zone?> FindByCodeAsync(Guid locationId, string code, CancellationToken cancellationToken = default);

    /// <summary>Lists every zone at a location.</summary>
    Task<IReadOnlyList<Zone>> ListForLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new zone.</summary>
    void Add(Zone zone);
}

/// <summary>Reads and writes <see cref="Bin"/> rows.</summary>
public interface IBinRepository
{
    /// <summary>Finds a bin by id.</summary>
    Task<Bin?> FindAsync(Guid binId, CancellationToken cancellationToken = default);

    /// <summary>Finds a bin by its code within a location, case-insensitively.</summary>
    Task<Bin?> FindByCodeAsync(Guid locationId, string code, CancellationToken cancellationToken = default);

    /// <summary>Lists every bin in a zone.</summary>
    Task<IReadOnlyList<Bin>> ListForZoneAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Lists every active bin at a location — the allocator's candidate pool.</summary>
    Task<IReadOnlyList<Bin>> ListActiveForLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new bin.</summary>
    void Add(Bin bin);
}

/// <summary>Reads and writes the <see cref="BinStock"/> projection.</summary>
public interface IBinStockRepository
{
    /// <summary>Finds the balance for one stock-keeping unit in one bin, or <c>null</c> if it has never held any.</summary>
    Task<BinStock?> FindAsync(Guid binId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every bin at a location holding stock of one stock-keeping unit, most available first — the
    /// allocator's candidate list (ADR-090, §7 rule 21: ranked and filtered on <c>Available</c>, not
    /// raw on-hand, once another wave has reserved part of a bin).
    /// </summary>
    Task<IReadOnlyList<BinStock>> ListCandidatesAsync(
        Guid locationId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);

    /// <summary>Every balance held in one bin.</summary>
    Task<IReadOnlyList<BinStock>> ListForBinAsync(Guid binId, CancellationToken cancellationToken = default);

    /// <summary>Adds a newly opened bin balance.</summary>
    void Add(BinStock balance);
}

/// <summary>Reads and writes the append-only <see cref="BinStockMovement"/> rows.</summary>
public interface IBinStockMovementRepository
{
    /// <summary>Appends a new movement. Nothing already added is ever updated or removed.</summary>
    void Add(BinStockMovement movement);

    /// <summary>
    /// True if a movement already exists against this reference — the dropped-connection-retry check for
    /// any command that posts a movement keyed by a caller-supplied id (§4.19).
    /// </summary>
    Task<bool> ExistsForReferenceAsync(
        Guid referenceId, BinStockReferenceType referenceType, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes <see cref="PutawayTask"/> rows.</summary>
public interface IPutawayTaskRepository
{
    /// <summary>Finds a task by id.</summary>
    Task<PutawayTask?> FindAsync(Guid putawayTaskId, CancellationToken cancellationToken = default);

    /// <summary>Every task still pending at a location.</summary>
    Task<IReadOnlyList<PutawayTask>> ListPendingForLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new task.</summary>
    void Add(PutawayTask task);
}

/// <summary>Reads and writes <see cref="PickWave"/> and <see cref="PickTask"/> rows.</summary>
public interface IPickWaveRepository
{
    /// <summary>Finds a wave by id.</summary>
    Task<PickWave?> FindAsync(Guid pickWaveId, CancellationToken cancellationToken = default);

    /// <summary>Every line belonging to a wave.</summary>
    Task<IReadOnlyList<PickTask>> ListTasksAsync(Guid pickWaveId, CancellationToken cancellationToken = default);

    /// <summary>Finds one task within a wave.</summary>
    Task<PickTask?> FindTaskAsync(Guid pickTaskId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new wave.</summary>
    void AddWave(PickWave wave);

    /// <summary>Adds a new demand line to an existing wave.</summary>
    void AddTask(PickTask task);

    /// <summary>
    /// Every pick task, across any wave, carrying this <see cref="PickTask.OutboundReference"/> —
    /// Stage 14's read of one order line's allocation and fulfilment. Added in Stage 14; a caller of the
    /// unchanged seam <c>PickTask.OutboundReference</c>'s own remarks named, not a new pick model.
    /// </summary>
    /// <param name="outboundReference">The demand's own reference — a Stage 14 order line's id, as text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<PickTask>> ListTasksByOutboundReferenceAsync(
        string outboundReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// The total quantity allocated to open (not shipped, not cancelled) tasks at a location for one
    /// stock-keeping unit — what a new demand line cannot promise on top of. Added in Stage 14 for
    /// <c>IOrderFulfilmentReader</c>'s available-to-promise read.
    /// </summary>
    /// <param name="locationId">The location.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="unitOfMeasure">The unit of measure to express the result in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Quantity> SumOpenAllocatedQuantityAsync(
        Guid locationId, Guid? itemId, Guid? itemVariantId, string unitOfMeasure, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes <see cref="PackTask"/> rows.</summary>
public interface IPackTaskRepository
{
    /// <summary>Finds the pack task for a wave, if it has been packed.</summary>
    Task<PackTask?> FindForWaveAsync(Guid pickWaveId, CancellationToken cancellationToken = default);

    /// <summary>Records a new pack task.</summary>
    void Add(PackTask task);
}

/// <summary>Reads and writes <see cref="ShipmentConfirmation"/> rows.</summary>
public interface IShipmentConfirmationRepository
{
    /// <summary>Finds the shipment confirmation for a wave, if it has shipped.</summary>
    Task<ShipmentConfirmation?> FindForWaveAsync(Guid pickWaveId, CancellationToken cancellationToken = default);

    /// <summary>Records a new shipment confirmation.</summary>
    void Add(ShipmentConfirmation shipment);
}

/// <summary>Reads qualifying open order lines for consolidated wave building.</summary>
public interface IOrderLineReader
{
    /// <summary>
    /// Returns every open order line that qualifies for a consolidated wave at the given
    /// location, period, geography level and company scope.
    /// </summary>
    Task<IReadOnlyList<OrderLineSummary>> ReadOpenLinesAsync(
        Guid locationId,
        DateOnly periodFrom,
        DateOnly periodTo,
        Guid? companyScopeId,
        CancellationToken cancellationToken = default);
}

/// <summary>A single open order line, as read for wave building.</summary>
public sealed record OrderLineSummary(
    Guid OrderId,
    Guid OrderLineId,
    Guid ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string PackSize,
    string GeographyValue);

/// <summary>
/// No order lines, ever: Order Management (Stage 14) does not exist yet, so there is nothing to
/// read — consolidated-wave preview and build see an empty demand pool until Stage 14 replaces
/// this with a real reader. Registered in <c>AddVumaWarehouse</c>; exists only so the host's
/// dependency validation passes and the preview path is callable.
/// </summary>
public sealed class EmptyOrderLineReader : IOrderLineReader
{
    /// <inheritdoc />
    public Task<IReadOnlyList<OrderLineSummary>> ReadOpenLinesAsync(
        Guid locationId,
        DateOnly periodFrom,
        DateOnly periodTo,
        Guid? companyScopeId,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderLineSummary>>([]);
}

/// <summary>Reads and writes <see cref="CycleCount"/> and <see cref="CycleCountLine"/> rows.</summary>
public interface ICycleCountRepository
{
    /// <summary>Finds a count by id.</summary>
    Task<CycleCount?> FindAsync(Guid cycleCountId, CancellationToken cancellationToken = default);

    /// <summary>Every line recorded in a count so far.</summary>
    Task<IReadOnlyList<CycleCountLine>> ListLinesAsync(Guid cycleCountId, CancellationToken cancellationToken = default);

    /// <summary>Finds an existing line for one bin/stock-keeping-unit pair within a count, for a recount.</summary>
    Task<CycleCountLine?> FindLineAsync(
        Guid cycleCountId, Guid binId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new count.</summary>
    void AddCount(CycleCount count);

    /// <summary>Adds a new line.</summary>
    void AddLine(CycleCountLine line);
}

/// <summary>Reads and writes <see cref="PickWaveLineBreakdown"/> rows (Stage 13b).</summary>
public interface IPickWaveLineBreakdownRepository
{
    /// <summary>Finds a breakdown by id.</summary>
    Task<PickWaveLineBreakdown?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists all breakdowns for a wave line.</summary>
    Task<IReadOnlyList<PickWaveLineBreakdown>> ListForWaveLineAsync(Guid pickWaveLineId, CancellationToken cancellationToken = default);

    /// <summary>Finds breakdowns by their wave line id (alias for ListForWaveLineAsync).</summary>
    Task<IReadOnlyList<PickWaveLineBreakdown>> FindByWaveLineIdAsync(Guid pickWaveLineId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new breakdown.</summary>
    void Add(PickWaveLineBreakdown breakdown);
}

/// <summary>Reads and writes <see cref="CountSchedule"/> rows (Stage 13b).</summary>
public interface ICountScheduleRepository
{
    /// <summary>Finds a schedule by id.</summary>
    Task<CountSchedule?> FindAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Lists active schedules due to run.</summary>
    Task<IReadOnlyList<CountSchedule>> ListActiveDueAsync(DateTimeOffset asAt, CancellationToken cancellationToken = default);

    /// <summary>Lists all schedules.</summary>
    Task<IReadOnlyList<CountSchedule>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new schedule.</summary>
    void Add(CountSchedule schedule);
}
/// <summary>
/// The cash-on-delivery dispatch gate (ADR-111), from the warehouse side. A wave ships goods out of
/// the building; when its tasks carry Stage 14 order lines, this gate refuses the ship while a
/// cash-on-delivery order behind them has neither payment nor driver-collect authorisation.
/// </summary>
/// <remarks>
/// Declared here so Stage 13 never depends on Stage 14 (<c>OrdersRulesTests</c> enforces the
/// direction): the implementation lives on the Orders side and is wired in DI. Non-order demand
/// (transfers, unreferenced tasks) passes through untouched.
/// </remarks>
public interface IOrderDispatchGate
{
    /// <summary>
    /// Refuses the dispatch when any referenced cash-on-delivery order may not ship.
    /// </summary>
    /// <param name="outboundReferences">The wave tasks' outbound references.</param>
    /// <param name="releasedAt">When the dispatch was requested, UTC.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="WarehouseRuleException">A COD order behind the wave is unpaid and unauthorised.</exception>
    Task EnsureDispatchAllowedAsync(
        IReadOnlyCollection<string> outboundReferences,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken = default);
}
