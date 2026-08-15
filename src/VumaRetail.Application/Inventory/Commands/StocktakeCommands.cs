using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory.Commands;

/// <summary>Opens a new physical count session at a location.</summary>
/// <param name="LocationId">The location to count.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenStocktakeCommand(Guid LocationId) : ICommand<Guid>;

/// <summary>Rejects a malformed open-stocktake command before it reaches the handler.</summary>
public sealed class OpenStocktakeCommandValidator : AbstractValidator<OpenStocktakeCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenStocktakeCommandValidator() => RuleFor(command => command.LocationId).NotEmpty();
}

/// <summary>Opens a stocktake session, refusing a location this tenant does not have.</summary>
/// <param name="locations">Location lookup.</param>
/// <param name="stocktakes">Session insertion.</param>
public sealed class OpenStocktakeCommandHandler(
    IStockLocationRepository locations,
    IStocktakeRepository stocktakes) : ICommandHandler<OpenStocktakeCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenStocktakeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        StocktakeSession session = StocktakeSession.Open(location.TenantId, location.StoreId, location.Id);

        stocktakes.AddSession(session);

        return session.Id;
    }
}

/// <summary>
/// Records a physical count for one stock-keeping unit within an open session — a fresh line, or a
/// recount of one already recorded this session.
/// </summary>
/// <param name="SessionId">The session this count belongs to.</param>
/// <param name="ItemId">The item counted, when it has no variants.</param>
/// <param name="ItemVariantId">The variant counted.</param>
/// <param name="CountedQuantity">What was physically found. Must not be negative.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordStocktakeCountCommand(
    Guid SessionId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity CountedQuantity) : ICommand<Guid>;

/// <summary>Rejects a malformed record-count command before it reaches the handler.</summary>
public sealed class RecordStocktakeCountCommandValidator : AbstractValidator<RecordStocktakeCountCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordStocktakeCountCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.CountedQuantity.Value).GreaterThanOrEqualTo(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(RecordStocktakeCountCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Snapshots the current system quantity and records it against the count — refusing a closed session,
/// same as every other mutation on it.
/// </summary>
/// <param name="stocktakes">Session and line lookup and insertion.</param>
/// <param name="balances">Reads the current on-hand quantity to snapshot as the line's system quantity.</param>
public sealed class RecordStocktakeCountCommandHandler(
    IStocktakeRepository stocktakes,
    IStockBalanceRepository balances) : ICommandHandler<RecordStocktakeCountCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(RecordStocktakeCountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StocktakeSession session = await stocktakes.FindSessionAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stocktake session", command.SessionId);

        session.EnsureOpen();

        StockBalance? balance = await balances
            .FindAsync(session.LocationId, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        // No balance yet means nothing has ever moved for this stock-keeping unit at this location —
        // the system quantity is honestly zero, in whatever unit the count itself was taken in.
        Quantity systemQuantity = balance?.QuantityOnHand ?? Quantity.Zero(command.CountedQuantity.UnitOfMeasure);

        StocktakeLine? existing = await stocktakes
            .FindLineAsync(command.SessionId, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Recount(systemQuantity, command.CountedQuantity);
            return existing.Id;
        }

        StocktakeLine line = StocktakeLine.Record(
            session.TenantId,
            session.StoreId,
            session.Id,
            command.ItemId,
            command.ItemVariantId,
            systemQuantity,
            command.CountedQuantity);

        stocktakes.AddLine(line);

        return line.Id;
    }
}

/// <summary>
/// Finalizes a stocktake session: posts every line's variance as an immutable ledger entry, then closes
/// the session. No further counts may be recorded once this returns.
/// </summary>
/// <param name="SessionId">The session to finalize.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record FinalizeStocktakeCommand(Guid SessionId) : ICommand;

/// <summary>Rejects a malformed finalize command before it reaches the handler.</summary>
public sealed class FinalizeStocktakeCommandValidator : AbstractValidator<FinalizeStocktakeCommand>
{
    /// <summary>Builds the rules.</summary>
    public FinalizeStocktakeCommandValidator() => RuleFor(command => command.SessionId).NotEmpty();
}

/// <summary>
/// Posts every line's variance through <see cref="IStockLedgerPoster"/> before closing the session — a
/// stocktake result is a document, and CLAUDE.md §7 rule 7 makes it immutable the moment it is finalized.
/// </summary>
/// <param name="stocktakes">Session and line lookup.</param>
/// <param name="locations">Location lookup.</param>
/// <param name="poster">Posts each line's variance and maintains the balance projection.</param>
/// <param name="clock">The only source of time.</param>
public sealed class FinalizeStocktakeCommandHandler(
    IStocktakeRepository stocktakes,
    IStockLocationRepository locations,
    IStockLedgerPoster poster,
    IClock clock) : ICommandHandler<FinalizeStocktakeCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(FinalizeStocktakeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StocktakeSession session = await stocktakes.FindSessionAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stocktake session", command.SessionId);

        session.EnsureOpen();

        StockLocation location = await locations.FindAsync(session.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", session.LocationId);

        IReadOnlyList<StocktakeLine> lines = await stocktakes
            .ListLinesAsync(command.SessionId, cancellationToken)
            .ConfigureAwait(false);

        foreach (StocktakeLine line in lines)
        {
            await poster
                .PostStocktakeVarianceAsync(location, line.ItemId, line.ItemVariantId, line.Variance, session.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        session.Finalize(clock.UtcNow);

        return Unit.Value;
    }
}
