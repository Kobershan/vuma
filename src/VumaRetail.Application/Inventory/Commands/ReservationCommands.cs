using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory.Commands;

/// <summary>Holds stock for a document — an order line, an approval, a transfer.</summary>
/// <remarks>
/// Partial by design: holds what exists and reports the shortfall rather than refusing. The
/// caller (a sourcing commit, an order handler) decides what a shortfall means — re-source,
/// backorder, refuse — because only the caller knows whether other companies exist.
/// </remarks>
/// <param name="LocationId">Where the stock sits.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/> must be set.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Demanded">How much is wanted. Must be positive.</param>
/// <param name="Source">What kind of document this hold belongs to.</param>
/// <param name="SourceDocumentId">The document's id.</param>
/// <param name="GroupDocumentRef">The cross-company order reference, when one exists.</param>
/// <param name="ExpiresAt">When the hold lapses, or <c>null</c> for a hold that never expires.</param>
/// <param name="Reason">Why the hold was taken.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReserveStockCommand(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Demanded,
    ReservationSource Source,
    Guid SourceDocumentId,
    string? GroupDocumentRef = null,
    DateTimeOffset? ExpiresAt = null,
    string? Reason = null) : ICommand<ReserveOutcome>;

/// <summary>Rejects a malformed reserve command before it reaches the handler.</summary>
public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReserveStockCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Demanded.Value).GreaterThan(0m);
        RuleFor(command => command.Source).IsInEnum();
        RuleFor(command => command.SourceDocumentId).NotEmpty();
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(ReserveStockCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Takes a hold through <see cref="IReservationService"/>.</summary>
/// <remarks>
/// Thin by design: the handler maps the command onto the service, and the service owns the
/// serialisable transaction, the row lock and the re-check. The pipeline transaction around
/// this handler stays empty — the hold commits on the service's company context, not the
/// ambient one — so there is exactly one writer and one transaction per hold.
/// </remarks>
/// <param name="reservations">Takes the hold.</param>
public sealed class ReserveStockCommandHandler(IReservationService reservations)
    : ICommandHandler<ReserveStockCommand, ReserveOutcome>
{
    /// <inheritdoc />
    public Task<ReserveOutcome> HandleAsync(ReserveStockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return reservations.ReserveAsync(
            command.LocationId,
            command.ItemId,
            command.ItemVariantId,
            command.Demanded,
            command.Source,
            command.SourceDocumentId,
            command.GroupDocumentRef,
            command.ExpiresAt,
            reason: command.Reason,
            cancellationToken: cancellationToken);
    }
}

/// <summary>Consumes a live hold — the held quantity shipped or issued.</summary>
/// <param name="ReservationId">The logical reservation.</param>
/// <param name="ConsumedByReferenceId">What consumed it — a shipment, a sale issue.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConsumeReservationCommand(Guid ReservationId, Guid ConsumedByReferenceId) : ICommand;

/// <summary>Rejects a malformed consume command before it reaches the handler.</summary>
public sealed class ConsumeReservationCommandValidator : AbstractValidator<ConsumeReservationCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConsumeReservationCommandValidator()
    {
        RuleFor(command => command.ReservationId).NotEmpty();
        RuleFor(command => command.ConsumedByReferenceId).NotEmpty();
    }
}

/// <summary>Consumes a hold through <see cref="IReservationService"/>.</summary>
/// <param name="reservations">Consumes the hold.</param>
public sealed class ConsumeReservationCommandHandler(IReservationService reservations)
    : ICommandHandler<ConsumeReservationCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ConsumeReservationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await reservations.ConsumeAsync(command.ReservationId, command.ConsumedByReferenceId, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>Releases a live hold — available is restored by a new ledger row, never an edit.</summary>
/// <param name="ReservationId">The logical reservation.</param>
/// <param name="Reason">Why the hold was released.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReleaseReservationCommand(Guid ReservationId, string? Reason = null) : ICommand;

/// <summary>Rejects a malformed release command before it reaches the handler.</summary>
public sealed class ReleaseReservationCommandValidator : AbstractValidator<ReleaseReservationCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReleaseReservationCommandValidator()
    {
        RuleFor(command => command.ReservationId).NotEmpty();
    }
}

/// <summary>Releases a hold through <see cref="IReservationService"/>.</summary>
/// <param name="reservations">Releases the hold.</param>
public sealed class ReleaseReservationCommandHandler(IReservationService reservations)
    : ICommandHandler<ReleaseReservationCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ReleaseReservationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await reservations.ReleaseAsync(command.ReservationId, command.Reason, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
