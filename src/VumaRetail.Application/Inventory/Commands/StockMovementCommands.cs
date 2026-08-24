using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory.Commands;

/// <summary>Receives stock into a location.</summary>
/// <param name="LocationId">Where the stock arrived.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/> must be set.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/> must be set.</param>
/// <param name="Quantity">How much arrived. Must be positive.</param>
/// <param name="UnitCost">What it cost per unit.</param>
/// <param name="Note">An optional free-text note.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReceiveStockCommand(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money UnitCost,
    string? Note = null) : ICommand<Guid>;

/// <summary>Rejects a malformed receive-stock command before it reaches the handler.</summary>
public sealed class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReceiveStockCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.UnitCost.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(ReceiveStockCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Posts a receipt through <see cref="IStockLedgerPoster"/>, refusing a location or stock-keeping unit
/// this tenant does not have, and a quantity in the wrong unit of measure for it.
/// </summary>
/// <param name="locations">Location lookup.</param>
/// <param name="skus">Resolves an item/variant to the unit of measure it must be received in.</param>
/// <param name="poster">Posts the ledger entry and maintains the balance projection.</param>
public sealed class ReceiveStockCommandHandler(
    IStockLocationRepository locations,
    IStockKeepingUnitResolver skus,
    IStockLedgerPoster poster) : ICommandHandler<ReceiveStockCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(ReceiveStockCommand command, CancellationToken cancellationToken = default)
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

        StockLedgerEntry entry = await poster
            .ReceiveAsync(location, command.ItemId, command.ItemVariantId, command.Quantity, command.UnitCost, command.Note, cancellationToken)
            .ConfigureAwait(false);

        return entry.Id;
    }
}

/// <summary>
/// Records a sale issuing stock from a location, through the same <see cref="IStockLedgerPoster"/> port
/// POS's own <c>SaleCompletionService</c> calls directly (<c>CONVENTIONS.md</c> §4: extract a domain
/// service, do not call one handler from another). This command is the standalone API surface over that
/// same port for a caller other than a completing sale — it is not on POS's own completion path.
/// </summary>
/// <remarks>
/// <b>§4.11 residual gap, not yet closed:</b> <see cref="SaleReferenceId"/> has no dedupe. A direct
/// replay of this specific command could still double-issue stock; the offline-replay path that
/// motivated §4.11 does not reach it, because <c>SaleCompletionService</c> bypasses the command
/// pipeline entirely. Recorded in <c>docs/PROGRESS.md</c> rather than fixed here — a dedupe key on
/// <see cref="IStockLedgerPoster.IssueForSaleAsync"/> needs to be the line, not the sale, since every
/// line of one sale shares the same <see cref="SaleReferenceId"/>.
/// </remarks>
/// <param name="LocationId">Where the stock left from.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much was sold. Must be positive.</param>
/// <param name="SaleReferenceId">The sale this issue correlates to.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordSaleIssueCommand(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Guid SaleReferenceId) : ICommand<Guid>;

/// <summary>Rejects a malformed sale-issue command before it reaches the handler.</summary>
public sealed class RecordSaleIssueCommandValidator : AbstractValidator<RecordSaleIssueCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordSaleIssueCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.SaleReferenceId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(RecordSaleIssueCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Posts a sale issue through <see cref="IStockLedgerPoster"/>, refusing insufficient stock.</summary>
/// <param name="locations">Location lookup.</param>
/// <param name="skus">Resolves an item/variant to its unit of measure.</param>
/// <param name="poster">Posts the ledger entry and maintains the balance projection.</param>
public sealed class RecordSaleIssueCommandHandler(
    IStockLocationRepository locations,
    IStockKeepingUnitResolver skus,
    IStockLedgerPoster poster) : ICommandHandler<RecordSaleIssueCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(RecordSaleIssueCommand command, CancellationToken cancellationToken = default)
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

        StockLedgerEntry entry = await poster
            .IssueForSaleAsync(location, command.ItemId, command.ItemVariantId, command.Quantity, command.SaleReferenceId, cancellationToken)
            .ConfigureAwait(false);

        return entry.Id;
    }
}

/// <summary>Posts a documented adjustment — a signed correction to on-hand quantity.</summary>
/// <param name="LocationId">Where the adjustment applies.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Delta">The signed quantity change. Must not be zero.</param>
/// <param name="ReasonCode">Why the adjustment was made.</param>
/// <param name="UnitCost">The cost to value an increase at, or <c>null</c> to use the current average.</param>
/// <param name="Note">An optional free-text note.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AdjustStockCommand(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Delta,
    AdjustmentReasonCode ReasonCode,
    Money? UnitCost = null,
    string? Note = null) : ICommand<Guid>;

/// <summary>Rejects a malformed adjust-stock command before it reaches the handler.</summary>
public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    /// <summary>Builds the rules.</summary>
    public AdjustStockCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Delta.Value).NotEqual(0m);
        RuleFor(command => command.ReasonCode).IsInEnum();
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(AdjustStockCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Posts an adjustment through <see cref="IStockLedgerPoster"/>.</summary>
/// <param name="locations">Location lookup.</param>
/// <param name="skus">Resolves an item/variant to its unit of measure.</param>
/// <param name="poster">Posts the ledger entry and maintains the balance projection.</param>
public sealed class AdjustStockCommandHandler(
    IStockLocationRepository locations,
    IStockKeepingUnitResolver skus,
    IStockLedgerPoster poster) : ICommandHandler<AdjustStockCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AdjustStockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        string expectedUnitOfMeasure = await skus
            .ResolveUnitOfMeasureCodeAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(expectedUnitOfMeasure, command.Delta.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(expectedUnitOfMeasure, command.Delta.UnitOfMeasure);
        }

        StockLedgerEntry entry = await poster
            .AdjustAsync(location, command.ItemId, command.ItemVariantId, command.Delta, command.ReasonCode, command.UnitCost, command.Note, cancellationToken)
            .ConfigureAwait(false);

        return entry.Id;
    }
}

/// <summary>Transfers stock between two locations as two correlated ledger entries.</summary>
/// <param name="SourceLocationId">Where the stock leaves.</param>
/// <param name="DestinationLocationId">Where the stock arrives. Must differ from <paramref name="SourceLocationId"/>.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much moved. Must be positive.</param>
/// <param name="Note">An optional free-text note.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record TransferStockCommand(
    Guid SourceLocationId,
    Guid DestinationLocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    string? Note = null) : ICommand<Guid>;

/// <summary>Rejects a malformed transfer command before it reaches the handler.</summary>
public sealed class TransferStockCommandValidator : AbstractValidator<TransferStockCommand>
{
    /// <summary>Builds the rules.</summary>
    public TransferStockCommandValidator()
    {
        RuleFor(command => command.SourceLocationId).NotEmpty();
        RuleFor(command => command.DestinationLocationId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
        RuleFor(command => command)
            .Must(command => command.SourceLocationId != command.DestinationLocationId)
            .WithMessage("Source and destination locations must differ.");
    }

    private static bool HaveExactlyOneItemOrVariant(TransferStockCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Posts both sides of a transfer through <see cref="IStockLedgerPoster"/> and records the correlating
/// <see cref="StockTransfer"/> document.
/// </summary>
/// <param name="locations">Location lookup.</param>
/// <param name="skus">Resolves an item/variant to its unit of measure.</param>
/// <param name="poster">Posts both ledger entries and maintains both balance projections.</param>
/// <param name="transfers">Records the completed transfer document.</param>
public sealed class TransferStockCommandHandler(
    IStockLocationRepository locations,
    IStockKeepingUnitResolver skus,
    IStockLedgerPoster poster,
    IStockTransferRepository transfers) : ICommandHandler<TransferStockCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(TransferStockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation source = await locations.FindAsync(command.SourceLocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.SourceLocationId);

        StockLocation destination = await locations.FindAsync(command.DestinationLocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.DestinationLocationId);

        string expectedUnitOfMeasure = await skus
            .ResolveUnitOfMeasureCodeAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(expectedUnitOfMeasure, command.Quantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(expectedUnitOfMeasure, command.Quantity.UnitOfMeasure);
        }

        // Minted before either ledger entry is posted, so both can carry it as their ReferenceId and
        // this transfer document — once StockTransfer.Record constructs it below — needs no separate
        // correlation field to be found by (see the new Entity(id, tenantId, ...) constructor's remarks).
        Guid transferId = Domain.Primitives.UuidV7.NewGuid();

        (StockLedgerEntry outEntry, StockLedgerEntry inEntry) = await poster
            .TransferAsync(source, destination, command.ItemId, command.ItemVariantId, command.Quantity, transferId, command.Note, cancellationToken)
            .ConfigureAwait(false);

        StockTransfer transfer = StockTransfer.Record(
            transferId,
            source.TenantId,
            source.Id,
            destination.Id,
            command.ItemId,
            command.ItemVariantId,
            command.Quantity,
            outEntry.UnitCost,
            outEntry.Id,
            inEntry.Id,
            command.Note);

        transfers.Add(transfer);

        return transfer.Id;
    }
}
