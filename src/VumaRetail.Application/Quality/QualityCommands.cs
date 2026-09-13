#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Quality;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Quality;

[CommandSideEffect(SideEffect.Write)]
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

[CommandSideEffect(SideEffect.Write)]
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

[CommandSideEffect(SideEffect.Write)]
public sealed record RejectQualityHoldCommand(Guid HoldId, string Reason) : ICommand;

public sealed class RejectQualityHoldCommandHandler(IQualityHoldRepository holds, IReservationService reservations, IClock clock)
    : ICommandHandler<RejectQualityHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectQualityHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (hold.Status == QualityHoldStatus.Rejected)
        {
            return Unit.Value;
        }
        hold.Reject(clock.UtcNow, command.Reason);
        await reservations.ConsumeAsync(hold.ReservationId, hold.Id, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RecordInspectionCommand(Guid OperationId, Guid CompanyId, Guid HoldId, bool Passed, int SampleSize, string Evidence) : ICommand<Guid>;

public sealed class RecordInspectionCommandHandler(
    IInspectionResultRepository inspections, IQualityHoldRepository holds, ITenantContext tenant,
    ICompanyContext company, IClock clock) : ICommandHandler<RecordInspectionCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordInspectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        InspectionResult? existing = await inspections.FindByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.HoldId != command.HoldId || existing.CompanyId != command.CompanyId || existing.Passed != command.Passed
                || existing.SampleSize != command.SampleSize || !string.Equals(existing.Evidence, command.Evidence.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The inspection operation was replayed with different content.");
            }
            return existing.Id;
        }
        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The inspection company is not the active company.");
        }
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (hold.CompanyId != command.CompanyId || hold.Status != QualityHoldStatus.Held)
        {
            throw new InvalidOperationException("Inspections require an active hold in the selected company.");
        }
        InspectionResult result = InspectionResult.Record(tenant.TenantId, null, command.CompanyId, command.HoldId,
            command.OperationId, command.Passed, command.SampleSize, command.Evidence, clock.UtcNow);
        inspections.Add(result);
        return result.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenNonConformanceCommand(Guid OperationId, Guid CompanyId, Guid HoldId, NonConformanceSeverity Severity, string Description) : ICommand<Guid>;

public sealed class OpenNonConformanceCommandHandler(
    INonConformanceRepository nonConformances, IQualityHoldRepository holds, ITenantContext tenant,
    ICompanyContext company, IClock clock) : ICommandHandler<OpenNonConformanceCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenNonConformanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        NonConformance? existing = await nonConformances.FindByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || existing.HoldId != command.HoldId || existing.Severity != command.Severity
                || !string.Equals(existing.Description, command.Description.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The non-conformance operation was replayed with different content.");
            }
            return existing.Id;
        }
        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The non-conformance company is not the active company.");
        }
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (hold.CompanyId != command.CompanyId || hold.Status != QualityHoldStatus.Held)
        {
            throw new InvalidOperationException("A non-conformance requires an active hold in the selected company.");
        }
        NonConformance result = NonConformance.Open(tenant.TenantId, null, command.CompanyId, command.OperationId,
            command.HoldId, command.Severity, command.Description, clock.UtcNow);
        nonConformances.Add(result);
        return result.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record StartCorrectiveActionCommand(Guid NonConformanceId, Guid OperationId) : ICommand;

public sealed class StartCorrectiveActionCommandHandler(INonConformanceRepository nonConformances)
    : ICommandHandler<StartCorrectiveActionCommand, Unit>
{
    public async Task<Unit> HandleAsync(StartCorrectiveActionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        NonConformance issue = await nonConformances.FindAsync(command.NonConformanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Non-conformance not found.");
        if (issue.Status == NonConformanceStatus.CorrectiveAction && issue.CorrectiveActionOperationId == command.OperationId)
        {
            return Unit.Value;
        }
        issue.StartCorrectiveAction(command.OperationId);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CloseNonConformanceCommand(Guid NonConformanceId, Guid OperationId, string Resolution) : ICommand;

public sealed class CloseNonConformanceCommandHandler(INonConformanceRepository nonConformances, IClock clock)
    : ICommandHandler<CloseNonConformanceCommand, Unit>
{
    public async Task<Unit> HandleAsync(CloseNonConformanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        NonConformance issue = await nonConformances.FindAsync(command.NonConformanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Non-conformance not found.");
        if (issue.Status == NonConformanceStatus.Closed && issue.ClosureOperationId == command.OperationId)
        {
            return Unit.Value;
        }
        issue.Close(command.OperationId, clock.UtcNow, command.Resolution);
        return Unit.Value;
    }
}
