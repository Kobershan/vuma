#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Quality;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Quality;

public sealed record PlaceQualityHoldCommand(
    Guid OperationId,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string Reason) : ICommand<Guid>;

public sealed class PlaceQualityHoldCommandHandler(
    IQualityHoldRepository holds,
    IReservationService reservations,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock) : ICommandHandler<PlaceQualityHoldCommand, Guid>
{
    public async Task<Guid> HandleAsync(PlaceQualityHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty)
        {
            throw new ArgumentException("OperationId is required.", nameof(command));
        }
        QualityHold? existing = await holds.FindByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || existing.LocationId != command.LocationId
                || existing.ItemId != command.ItemId || existing.ItemVariantId != command.ItemVariantId
                || existing.Quantity != new Quantity(command.Quantity, command.UnitOfMeasure)
                || !string.Equals(existing.Reason, command.Reason.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The quality hold operation was replayed with different content.");
            }
            return existing.Id;
        }

        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The quality hold company is not the active company.");
        }
        Quantity quantity = new(command.Quantity, command.UnitOfMeasure);
        ReserveOutcome reservation = await reservations.ReserveAsync(
            command.LocationId, command.ItemId, command.ItemVariantId, quantity,
            ReservationSource.QualityHold, command.OperationId,
            reason: command.Reason, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!reservation.Shortfall.IsZero)
        {
            if (reservation.ReservationId is { } partial)
            {
                await reservations.ReleaseAsync(partial, "Quality hold shortfall", cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The requested quality hold exceeds available stock.");
        }

        QualityHold hold = QualityHold.Place(tenant.TenantId, null, command.CompanyId, command.OperationId, command.LocationId,
            command.ItemId, command.ItemVariantId, quantity, command.Reason, clock.UtcNow,
            reservation.ReservationId ?? throw new InvalidOperationException("The reservation did not return an identity."));
        holds.Add(hold);
        return hold.Id;
    }
}

public sealed record ReleaseQualityHoldCommand(Guid HoldId, string Reason) : ICommand;

public sealed class ReleaseQualityHoldCommandHandler(IQualityHoldRepository holds, IReservationService reservations, IClock clock)
    : ICommandHandler<ReleaseQualityHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(ReleaseQualityHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (hold.Status == QualityHoldStatus.Released)
        {
            return Unit.Value;
        }
        hold.Release(clock.UtcNow, command.Reason);
        await reservations.ReleaseAsync(hold.ReservationId, command.Reason, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
