using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>
/// Opens a putaway task for stock that has arrived unbinned and needs to be shelved. Does not move any
/// stock itself — see <see cref="ConfirmPutawayCommand"/>.
/// </summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenPutawayTaskCommand(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    PutawaySourceReferenceType SourceReferenceType,
    Guid? SourceReferenceId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed open-putaway command before it reaches the handler.</summary>
public sealed class OpenPutawayTaskCommandValidator : AbstractValidator<OpenPutawayTaskCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenPutawayTaskCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.SourceReferenceType).IsInEnum();
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(OpenPutawayTaskCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Opens a task and proposes a bin: the bin already holding the most of this stock-keeping unit at the
/// location, or (if none does) the first active bin, or no suggestion at all if the location has none.
/// </summary>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="skus">Resolves an item/variant to its unit of measure.</param>
/// <param name="binStocks">Bin balance lookup, for the suggestion.</param>
/// <param name="bins">Active-bin lookup, for the fallback suggestion.</param>
/// <param name="putawayTasks">Task insertion.</param>
public sealed class OpenPutawayTaskCommandHandler(
    IStockLocationRepository locations,
    IStockKeepingUnitResolver skus,
    IBinStockRepository binStocks,
    IBinRepository bins,
    IPutawayTaskRepository putawayTasks) : ICommandHandler<OpenPutawayTaskCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenPutawayTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        string expectedUnitOfMeasure = await skus
            .ResolveUnitOfMeasureCodeAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(expectedUnitOfMeasure, command.Quantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(expectedUnitOfMeasure, command.Quantity.UnitOfMeasure);
        }

        PutawayTask task = PutawayTask.Create(
            location.TenantId, location.StoreId, location.Id, command.ItemId, command.ItemVariantId,
            command.Quantity, command.SourceReferenceType, command.SourceReferenceId);

        Guid? suggestion = await SuggestBinAsync(location.Id, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (suggestion is { } binId)
        {
            task.SuggestBin(binId);
        }

        putawayTasks.Add(task);

        return task.Id;
    }

    private async Task<Guid?> SuggestBinAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Warehouse.BinStock> candidates = await binStocks
            .ListCandidatesAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count > 0)
        {
            return candidates[0].BinId;
        }

        IReadOnlyList<Domain.Warehouse.Bin> activeBins = await bins
            .ListActiveForLocationAsync(locationId, cancellationToken)
            .ConfigureAwait(false);

        return activeBins.Count > 0 ? activeBins[0].Id : null;
    }
}

/// <summary>Confirms that some or all of a putaway task's remaining quantity was shelved into a bin.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConfirmPutawayCommand(Guid PutawayTaskId, Guid BinId, Quantity Quantity) : ICommand;

/// <summary>Rejects a malformed confirm-putaway command before it reaches the handler.</summary>
public sealed class ConfirmPutawayCommandValidator : AbstractValidator<ConfirmPutawayCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConfirmPutawayCommandValidator()
    {
        RuleFor(command => command.PutawayTaskId).NotEmpty();
        RuleFor(command => command.BinId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
    }
}

/// <summary>Confirms a putaway, moving stock from unbinned storage into a bin.</summary>
/// <param name="putawayTasks">Task lookup.</param>
/// <param name="bins">Bin lookup.</param>
/// <param name="mover">Posts the bin-level movement and maintains the bin's balance.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ConfirmPutawayCommandHandler(
    IPutawayTaskRepository putawayTasks,
    IBinRepository bins,
    IBinStockMover mover,
    IClock clock) : ICommandHandler<ConfirmPutawayCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ConfirmPutawayCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PutawayTask task = await putawayTasks.FindAsync(command.PutawayTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("putaway task", command.PutawayTaskId);

        Domain.Warehouse.Bin bin = await bins.FindAsync(command.BinId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", command.BinId);

        task.Confirm(command.BinId, command.Quantity, clock.UtcNow);

        await mover.PutawayAsync(bin, task.ItemId, task.ItemVariantId, command.Quantity, task.Id, cancellationToken)
            .ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>Abandons a putaway task's remaining quantity.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelPutawayTaskCommand(Guid PutawayTaskId) : ICommand;

/// <summary>Cancels a task.</summary>
/// <param name="putawayTasks">Task lookup.</param>
public sealed class CancelPutawayTaskCommandHandler(IPutawayTaskRepository putawayTasks)
    : ICommandHandler<CancelPutawayTaskCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelPutawayTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PutawayTask task = await putawayTasks.FindAsync(command.PutawayTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("putaway task", command.PutawayTaskId);

        task.Cancel();

        return Unit.Value;
    }
}
