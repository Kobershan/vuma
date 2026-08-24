using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>Opens a new cycle count of one or more bins.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenCycleCountCommand(Guid LocationId, Guid? ZoneId = null, DateTimeOffset? ScheduledAt = null) : ICommand<Guid>;

/// <summary>Rejects a malformed open-count command before it reaches the handler.</summary>
public sealed class OpenCycleCountCommandValidator : AbstractValidator<OpenCycleCountCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenCycleCountCommandValidator() => RuleFor(command => command.LocationId).NotEmpty();
}

/// <summary>Opens a count.</summary>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="cycleCounts">Count insertion.</param>
public sealed class OpenCycleCountCommandHandler(IStockLocationRepository locations, ICycleCountRepository cycleCounts)
    : ICommandHandler<OpenCycleCountCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenCycleCountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        CycleCount count = CycleCount.Open(location.TenantId, location.StoreId, location.Id, command.ZoneId, command.ScheduledAt);
        cycleCounts.AddCount(count);

        return count.Id;
    }
}

/// <summary>Records a count, or re-counts a bin/stock-keeping-unit pair already recorded this count.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordCycleCountCommand(
    Guid CycleCountId, Guid BinId, Guid? ItemId, Guid? ItemVariantId, Quantity CountedQuantity) : ICommand<Guid>;

/// <summary>Rejects a malformed record-count command before it reaches the handler.</summary>
public sealed class RecordCycleCountCommandValidator : AbstractValidator<RecordCycleCountCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordCycleCountCommandValidator()
    {
        RuleFor(command => command.CycleCountId).NotEmpty();
        RuleFor(command => command.BinId).NotEmpty();
        RuleFor(command => command.CountedQuantity.Value).GreaterThanOrEqualTo(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(RecordCycleCountCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Records a count. The system quantity is snapshotted from <see cref="Domain.Warehouse.BinStock"/> at
/// the moment of counting, not read live when the count is finalized (mirroring Stage 08's
/// <c>StocktakeLine</c>).
/// </summary>
/// <param name="cycleCounts">Count and line lookup and insertion.</param>
/// <param name="bins">Bin lookup.</param>
/// <param name="binStocks">Bin balance lookup, for the snapshot.</param>
public sealed class RecordCycleCountCommandHandler(
    ICycleCountRepository cycleCounts, IBinRepository bins, IBinStockRepository binStocks)
    : ICommandHandler<RecordCycleCountCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(RecordCycleCountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CycleCount count = await cycleCounts.FindAsync(command.CycleCountId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("cycle count", command.CycleCountId);

        count.EnsureOpen();

        Domain.Warehouse.Bin bin = await bins.FindAsync(command.BinId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", command.BinId);

        Domain.Warehouse.BinStock? balance = await binStocks
            .FindAsync(command.BinId, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        Quantity systemQuantity = balance?.QuantityOnHand ?? Quantity.Zero(command.CountedQuantity.UnitOfMeasure);

        CycleCountLine? existing = await cycleCounts
            .FindLineAsync(count.Id, command.BinId, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Recount(systemQuantity, command.CountedQuantity);
            return existing.Id;
        }

        CycleCountLine line = CycleCountLine.Record(
            count.TenantId, count.StoreId, count.Id, bin.Id, command.ItemId, command.ItemVariantId,
            systemQuantity, command.CountedQuantity);

        cycleCounts.AddLine(line);

        return line.Id;
    }
}

/// <summary>Posts every line's variance and closes the count. Irreversible.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record FinalizeCycleCountCommand(Guid CycleCountId) : ICommand;

/// <summary>Rejects a malformed finalize command before it reaches the handler.</summary>
public sealed class FinalizeCycleCountCommandValidator : AbstractValidator<FinalizeCycleCountCommand>
{
    /// <summary>Builds the rules.</summary>
    public FinalizeCycleCountCommandValidator() => RuleFor(command => command.CycleCountId).NotEmpty();
}

/// <summary>
/// Finalizes a count: for every line whose variance against the bin's <em>current</em> on-hand quantity
/// is non-zero, posts it through Stage 08's <see cref="IStockLedgerPoster.PostCycleCountVarianceAsync"/>
/// (which corrects the location's own on-hand quantity — business rule 4) and applies the same delta to
/// the bin's own <see cref="Domain.Warehouse.BinStock"/>.
/// </summary>
/// <remarks>
/// Re-derives the delta at finalize time — <c>line.CountedQuantity</c> minus whatever
/// <see cref="Domain.Warehouse.BinStock.QuantityOnHand"/> is <em>now</em> — rather than trusting
/// <see cref="CycleCountLine.Variance"/>, which is frozen against the snapshot <c>RecordCycleCountCommand</c>
/// took when the line was counted. A pick, putaway or internal move that legitimately touched this
/// bin/stock-keeping unit between recording and finalizing already changed on-hand for a real reason;
/// applying the frozen variance on top of it double-counts that movement and posts a phantom shortage or
/// overage neither the picker nor the mover actually caused (§4.19).
/// </remarks>
/// <param name="cycleCounts">Count and line lookup.</param>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="binStocks">Bin balance lookup and insertion.</param>
/// <param name="poster">Posts the location-level variance.</param>
/// <param name="clock">The only source of time.</param>
public sealed class FinalizeCycleCountCommandHandler(
    ICycleCountRepository cycleCounts,
    IStockLocationRepository locations,
    IBinStockRepository binStocks,
    IStockLedgerPoster poster,
    IClock clock) : ICommandHandler<FinalizeCycleCountCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(FinalizeCycleCountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CycleCount count = await cycleCounts.FindAsync(command.CycleCountId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("cycle count", command.CycleCountId);

        count.EnsureOpen();

        StockLocation location = await locations.FindAsync(count.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", count.LocationId);

        IReadOnlyList<CycleCountLine> lines = await cycleCounts.ListLinesAsync(count.Id, cancellationToken).ConfigureAwait(false);

        foreach (CycleCountLine line in lines)
        {
            Domain.Warehouse.BinStock? balance = await binStocks
                .FindAsync(line.BinId, line.ItemId, line.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            Quantity currentSystemQuantity = balance?.QuantityOnHand ?? Quantity.Zero(line.CountedQuantity.UnitOfMeasure);
            Quantity currentVariance = line.CountedQuantity - currentSystemQuantity;

            if (currentVariance.IsZero)
            {
                // What was physically counted now agrees with on-hand — either nothing moved, or
                // something already moved it to exactly the counted figure. Either way, nothing to post.
                continue;
            }

            await poster.PostCycleCountVarianceAsync(
                location, line.ItemId, line.ItemVariantId, currentVariance, count.Id, line.BinId, cancellationToken)
                .ConfigureAwait(false);

            if (balance is null)
            {
                balance = Domain.Warehouse.BinStock.Open(
                    count.TenantId, count.StoreId, line.BinId, line.ItemId, line.ItemVariantId,
                    currentVariance.UnitOfMeasure);

                binStocks.Add(balance);
            }

            if (currentVariance.IsNegative)
            {
                balance.ApplyOut(-currentVariance);
            }
            else
            {
                balance.ApplyIn(currentVariance);
            }
        }

        count.Finalize(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>
/// Moves stock directly from one bin to another within the same location. <paramref name="TransferId"/>
/// is the identity correlating the two movement rows this posts, or <c>null</c> to mint one here — a
/// caller that already sent this move once supplies the id it used the first time, which makes a
/// dropped-connection retry idempotent instead of moving the same quantity twice (§4.19).
/// </summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record MoveBinStockCommand(
    Guid SourceBinId,
    Guid DestinationBinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Guid? TransferId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed move command before it reaches the handler.</summary>
public sealed class MoveBinStockCommandValidator : AbstractValidator<MoveBinStockCommand>
{
    /// <summary>Builds the rules.</summary>
    public MoveBinStockCommandValidator()
    {
        RuleFor(command => command.SourceBinId).NotEmpty();
        RuleFor(command => command.DestinationBinId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
        RuleFor(command => command)
            .Must(command => command.SourceBinId != command.DestinationBinId)
            .WithMessage("Source and destination bins must differ.");
    }

    private static bool HaveExactlyOneItemOrVariant(MoveBinStockCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Moves bin stock, refusing bins from different locations.</summary>
/// <param name="bins">Bin lookup.</param>
/// <param name="mover">Posts both movements and maintains both balances.</param>
/// <param name="movements">The idempotent-replay check against an already-posted transfer id.</param>
public sealed class MoveBinStockCommandHandler(IBinRepository bins, IBinStockMover mover, IBinStockMovementRepository movements)
    : ICommandHandler<MoveBinStockCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(MoveBinStockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.TransferId is { } replayed)
        {
            bool already = await movements
                .ExistsForReferenceAsync(replayed, Domain.Warehouse.BinStockReferenceType.InternalTransfer, cancellationToken)
                .ConfigureAwait(false);

            if (already)
            {
                // The idempotent replay (§4.19) — a dropped-connection retry returns the transfer id it
                // already posted instead of moving the same quantity a second time.
                return replayed;
            }
        }

        Domain.Warehouse.Bin source = await bins.FindAsync(command.SourceBinId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", command.SourceBinId);

        Domain.Warehouse.Bin destination = await bins.FindAsync(command.DestinationBinId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", command.DestinationBinId);

        if (source.LocationId != destination.LocationId)
        {
            throw new ArgumentException("An internal bin move must stay within one location.");
        }

        Guid transferId = command.TransferId ?? UuidV7.NewGuid();

        (Domain.Warehouse.BinStockMovement outMovement, _) = await mover
            .InternalTransferAsync(source, destination, command.ItemId, command.ItemVariantId, command.Quantity, transferId, cancellationToken)
            .ConfigureAwait(false);

        return outMovement.ReferenceId;
    }
}
