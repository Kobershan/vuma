using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>Opens a new, empty pick wave at a location.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenPickWaveCommand(Guid LocationId) : ICommand<Guid>;

/// <summary>Rejects a malformed open-wave command before it reaches the handler.</summary>
public sealed class OpenPickWaveCommandValidator : AbstractValidator<OpenPickWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenPickWaveCommandValidator() => RuleFor(command => command.LocationId).NotEmpty();
}

/// <summary>Opens a wave.</summary>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="waves">Wave insertion.</param>
public sealed class OpenPickWaveCommandHandler(IStockLocationRepository locations, IPickWaveRepository waves)
    : ICommandHandler<OpenPickWaveCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenPickWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        PickWave wave = PickWave.Open(location.TenantId, location.StoreId, location.Id);
        waves.AddWave(wave);

        return wave.Id;
    }
}

/// <summary>Adds a demand line to an open wave.</summary>
/// <param name="PickWaveId">The wave to add to.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="RequestedQuantity">How much is demanded.</param>
/// <param name="OutboundReference">What raised the demand.</param>
/// <param name="PickTaskId">
/// The task's identity, or <c>null</c> to mint one here. A caller that already sent this create once
/// supplies the id it used the first time, which makes a dropped-connection retry idempotent (§4.19).
/// </param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddPickTaskCommand(
    Guid PickWaveId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity RequestedQuantity,
    string OutboundReference,
    Guid? PickTaskId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed add-pick-task command before it reaches the handler.</summary>
public sealed class AddPickTaskCommandValidator : AbstractValidator<AddPickTaskCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddPickTaskCommandValidator()
    {
        RuleFor(command => command.PickWaveId).NotEmpty();
        RuleFor(command => command.RequestedQuantity.Value).GreaterThan(0m);
        RuleFor(command => command.OutboundReference).NotEmpty().MaximumLength(128);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(AddPickTaskCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Adds a demand line, refusing a wave that is not <see cref="PickWaveStatus.Open"/>.</summary>
/// <param name="waves">Wave lookup and line insertion.</param>
/// <param name="skus">Resolves an item/variant to its unit of measure.</param>
public sealed class AddPickTaskCommandHandler(IPickWaveRepository waves, IStockKeepingUnitResolver skus)
    : ICommandHandler<AddPickTaskCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddPickTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PickTaskId is { } replayed)
        {
            PickTask? already = await waves.FindTaskAsync(replayed, cancellationToken).ConfigureAwait(false);

            if (already is not null)
            {
                // The idempotent replay (§4.19) — a dropped-connection retry returns the line it already
                // added instead of adding a second one.
                return already.Id;
            }
        }

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        if (wave.Status != PickWaveStatus.Open)
        {
            throw WarehouseRuleException.PickWaveWrongStatus(PickWaveStatus.Open, wave.Status);
        }

        string expectedUnitOfMeasure = await skus
            .ResolveUnitOfMeasureCodeAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(expectedUnitOfMeasure, command.RequestedQuantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(expectedUnitOfMeasure, command.RequestedQuantity.UnitOfMeasure);
        }

        PickTask task = PickTask.Create(
            wave.TenantId, wave.StoreId, wave.Id, command.ItemId, command.ItemVariantId,
            command.RequestedQuantity, command.OutboundReference, command.PickTaskId);

        waves.AddTask(task);

        return task.Id;
    }
}

/// <summary>
/// Releases a wave for picking: allocates every pending line to bin(s) via <see cref="IPickAllocationStrategy"/>.
/// </summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReleasePickWaveCommand(Guid PickWaveId) : ICommand;

/// <summary>Rejects a malformed release command before it reaches the handler.</summary>
public sealed class ReleasePickWaveCommandValidator : AbstractValidator<ReleasePickWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReleasePickWaveCommandValidator() => RuleFor(command => command.PickWaveId).NotEmpty();
}

/// <summary>
/// Allocates every pending line. A line whose demand spans more than one bin is split: the original
/// line is allocated to the largest candidate, and one additional line is created per further
/// candidate the allocator draws on.
/// </summary>
/// <param name="waves">Wave and task lookup.</param>
/// <param name="binStocks">Bin balance lookup — the allocator's candidate pool.</param>
/// <param name="allocator">Decides which bin(s) satisfy a line's demand.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ReleasePickWaveCommandHandler(
    IPickWaveRepository waves,
    IBinStockRepository binStocks,
    IPickAllocationStrategy allocator,
    IClock clock) : ICommandHandler<ReleasePickWaveCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ReleasePickWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        IReadOnlyList<PickTask> tasks = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        // What this call has already reserved, by bin — ListCandidatesAsync's snapshot is read-only and
        // does not see an earlier iteration's in-flight reservation until SaveChanges runs, so a second
        // task in the same wave demanding the same stock-keeping unit would otherwise be allocated
        // against on-hand nobody told it was already spoken for (§4.19).
        Dictionary<Guid, Quantity> reservedThisRelease = [];

        foreach (PickTask task in tasks.Where(task => task.Status == PickTaskStatus.Pending))
        {
            IReadOnlyList<Domain.Warehouse.BinStock> candidates = await binStocks
                .ListCandidatesAsync(wave.LocationId, task.ItemId, task.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            foreach (Domain.Warehouse.BinStock candidate in candidates)
            {
                if (reservedThisRelease.TryGetValue(candidate.BinId, out Quantity already) && !already.IsZero)
                {
                    candidate.Reserve(already);
                }
            }

            IReadOnlyList<BinAllocation> allocations = allocator.Allocate(task.RequestedQuantity, candidates);

            Quantity totalAllocated = allocations.Aggregate(
                Quantity.Zero(task.RequestedQuantity.UnitOfMeasure), (sum, a) => sum + a.Quantity);

            if (totalAllocated < task.RequestedQuantity)
            {
                throw WarehouseRuleException.InsufficientStockToAllocate(task.RequestedQuantity, totalAllocated);
            }

            // Reserved here, at release — not only recorded on the confirm-time movement — so a second
            // wave released before this one is picked sees what is genuinely still available rather
            // than the same on-hand figure this wave already spoke for (§4.19, §7 rule 21).
            foreach (BinAllocation allocation in allocations)
            {
                Domain.Warehouse.BinStock balance = await binStocks
                    .FindAsync(allocation.BinId, task.ItemId, task.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new WarehouseNotFoundException("bin stock", allocation.BinId);

                balance.Reserve(allocation.Quantity);

                reservedThisRelease[allocation.BinId] = reservedThisRelease.TryGetValue(allocation.BinId, out Quantity existing)
                    ? existing + allocation.Quantity
                    : allocation.Quantity;
            }

            task.Allocate(allocations[0].BinId, allocations[0].Quantity);

            foreach (BinAllocation extra in allocations.Skip(1))
            {
                PickTask split = PickTask.Create(
                    wave.TenantId, wave.StoreId, wave.Id, task.ItemId, task.ItemVariantId,
                    extra.Quantity, task.OutboundReference);

                split.Allocate(extra.BinId, extra.Quantity);
                waves.AddTask(split);
            }
        }

        wave.Release(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Confirms a pick against its allocation. A short pick is recorded, not refused (business rule 6).</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConfirmPickCommand(Guid PickTaskId, Quantity PickedQuantity) : ICommand;

/// <summary>Rejects a malformed confirm-pick command before it reaches the handler.</summary>
public sealed class ConfirmPickCommandValidator : AbstractValidator<ConfirmPickCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConfirmPickCommandValidator()
    {
        RuleFor(command => command.PickTaskId).NotEmpty();
        RuleFor(command => command.PickedQuantity.Value).GreaterThan(0m);
    }
}

/// <summary>
/// Confirms a pick, relieving the allocated bin, and — once every line in the wave is picked,
/// short-picked or cancelled — marks the wave <see cref="PickWaveStatus.Picked"/>.
/// </summary>
/// <param name="waves">Wave and task lookup.</param>
/// <param name="bins">Bin lookup.</param>
/// <param name="mover">Posts the bin-level movement and maintains the bin's balance.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ConfirmPickCommandHandler(
    IPickWaveRepository waves,
    IBinRepository bins,
    IBinStockMover mover,
    IClock clock) : ICommandHandler<ConfirmPickCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ConfirmPickCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickTask task = await waves.FindTaskAsync(command.PickTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick task", command.PickTaskId);

        if (task.AllocatedBinId is not { } binId)
        {
            throw WarehouseRuleException.PickTaskWrongStatus(PickTaskStatus.Allocated, task.Status);
        }

        Domain.Warehouse.Bin bin = await bins.FindAsync(binId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", binId);

        // Read before ConfirmPick, though ConfirmPick never changes it — the allocation is what was
        // reserved at release, and the whole of it is released here regardless of a short pick (§4.19).
        Quantity reservedQuantity = task.AllocatedQuantity!.Value;

        task.ConfirmPick(command.PickedQuantity);

        await mover.PickAsync(bin, task.ItemId, task.ItemVariantId, command.PickedQuantity, reservedQuantity, task.Id, cancellationToken)
            .ConfigureAwait(false);

        PickWave wave = await waves.FindAsync(task.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", task.PickWaveId);

        IReadOnlyList<PickTask> siblings = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        bool everyLineSettled = siblings.All(sibling =>
            sibling.Status is PickTaskStatus.Picked or PickTaskStatus.ShortPicked or PickTaskStatus.Cancelled);

        if (everyLineSettled && wave.Status == PickWaveStatus.Released)
        {
            wave.MarkPicked(clock.UtcNow);
        }

        return Unit.Value;
    }
}

/// <summary>Abandons a pick task before it is picked.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelPickTaskCommand(Guid PickTaskId) : ICommand;

/// <summary>Cancels a task.</summary>
/// <param name="waves">Task lookup.</param>
/// <param name="binStocks">Releases the reservation an allocated task placed (§4.19).</param>
public sealed class CancelPickTaskCommandHandler(IPickWaveRepository waves, IBinStockRepository binStocks)
    : ICommandHandler<CancelPickTaskCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelPickTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickTask task = await waves.FindTaskAsync(command.PickTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick task", command.PickTaskId);

        // Only an allocated task holds a reservation — a still-pending one never reserved anything.
        if (task.AllocatedBinId is { } binId && task.AllocatedQuantity is { } allocated)
        {
            Domain.Warehouse.BinStock? balance = await binStocks
                .FindAsync(binId, task.ItemId, task.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            balance?.ReleaseReservation(allocated);
        }

        task.Cancel();

        return Unit.Value;
    }
}

/// <summary>Abandons a wave before it ships.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelPickWaveCommand(Guid PickWaveId) : ICommand;

/// <summary>Cancels a wave.</summary>
/// <param name="waves">Wave and task lookup.</param>
/// <param name="binStocks">Releases every allocated-but-unconfirmed task's reservation (§4.19).</param>
public sealed class CancelPickWaveCommandHandler(IPickWaveRepository waves, IBinStockRepository binStocks)
    : ICommandHandler<CancelPickWaveCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelPickWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        // A wave may be cancelled after release, while its tasks still hold a reservation nobody will
        // ever confirm or explicitly cancel individually — released here or it leaks forever (§4.19).
        IReadOnlyList<PickTask> tasks = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        foreach (PickTask task in tasks.Where(task => task.Status == PickTaskStatus.Allocated))
        {
            if (task.AllocatedBinId is { } binId && task.AllocatedQuantity is { } allocated)
            {
                Domain.Warehouse.BinStock? balance = await binStocks
                    .FindAsync(binId, task.ItemId, task.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false);

                balance?.ReleaseReservation(allocated);
            }
        }

        wave.Cancel();

        return Unit.Value;
    }
}

/// <summary>Records a picked wave's packing.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record PackWaveCommand(Guid PickWaveId, int PackageCount, string? Note = null) : ICommand<Guid>;

/// <summary>Rejects a malformed pack command before it reaches the handler.</summary>
public sealed class PackWaveCommandValidator : AbstractValidator<PackWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public PackWaveCommandValidator()
    {
        RuleFor(command => command.PickWaveId).NotEmpty();
        RuleFor(command => command.PackageCount).GreaterThan(0);
    }
}

/// <summary>Packs a wave, refusing one that is not <see cref="PickWaveStatus.Picked"/>.</summary>
/// <param name="waves">Wave lookup.</param>
/// <param name="packTasks">Pack record insertion.</param>
/// <param name="clock">The only source of time.</param>
public sealed class PackWaveCommandHandler(IPickWaveRepository waves, IPackTaskRepository packTasks, IClock clock)
    : ICommandHandler<PackWaveCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(PackWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        DateTimeOffset now = clock.UtcNow;

        wave.MarkPacked(now);

        PackTask task = PackTask.Record(wave.TenantId, wave.StoreId, wave.Id, command.PackageCount, command.Note, now);
        packTasks.Add(task);

        return task.Id;
    }
}

/// <summary>Confirms a packed wave's shipment — the point stock leaves the location (business rule 3).</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ShipWaveCommand(Guid PickWaveId, string? Carrier = null, string? TrackingNumber = null) : ICommand<Guid>;

/// <summary>Rejects a malformed ship command before it reaches the handler.</summary>
public sealed class ShipWaveCommandValidator : AbstractValidator<ShipWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public ShipWaveCommandValidator() => RuleFor(command => command.PickWaveId).NotEmpty();
}

/// <summary>
/// Ships a wave: sums picked quantity per stock-keeping unit across every picked or short-picked line,
/// posts one <see cref="IStockLedgerPoster.IssueForShipmentAsync"/> call per distinct SKU, and records
/// the shipment.
/// </summary>
/// <param name="waves">Wave and task lookup.</param>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="poster">Posts the location-level issue.</param>
/// <param name="shipments">Shipment insertion.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ShipWaveCommandHandler(
    IPickWaveRepository waves,
    IStockLocationRepository locations,
    IStockLedgerPoster poster,
    IShipmentConfirmationRepository shipments,
    IClock clock) : ICommandHandler<ShipWaveCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(ShipWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        IReadOnlyList<PickTask> tasks = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        List<PickTask> pickedTasks = [.. tasks.Where(task =>
            task.Status is PickTaskStatus.Picked or PickTaskStatus.ShortPicked && task.PickedQuantity is { IsZero: false })];

        if (pickedTasks.Count == 0)
        {
            throw WarehouseRuleException.NothingPicked();
        }

        StockLocation location = await locations.FindAsync(wave.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", wave.LocationId);

        DateTimeOffset now = clock.UtcNow;
        Guid shipmentId = UuidV7.NewGuid();

        foreach (var group in pickedTasks.GroupBy(task => (task.ItemId, task.ItemVariantId)))
        {
            Quantity total = group
                .Select(task => task.PickedQuantity!.Value)
                .Aggregate((a, b) => a + b);

            await poster.IssueForShipmentAsync(
                location, group.Key.ItemId, group.Key.ItemVariantId, total, shipmentId, cancellationToken)
                .ConfigureAwait(false);
        }

        wave.MarkShipped(now);

        ShipmentConfirmation shipment = ShipmentConfirmation.Ship(
            shipmentId, wave.TenantId, wave.StoreId, wave.Id, command.Carrier, command.TrackingNumber, now);

        shipments.Add(shipment);

        return shipment.Id;
    }
}
