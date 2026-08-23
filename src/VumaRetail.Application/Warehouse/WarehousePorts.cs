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
