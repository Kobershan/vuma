#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Quality;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Quality;

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenRecallCommand(Guid OperationId, Guid CompanyId, string CaseNumber, string LotReference, string Reason) : ICommand<Guid>;

public sealed class OpenRecallCommandHandler(IRecallCaseRepository recalls, ITenantContext tenant, ICompanyContext company, IClock clock)
    : ICommandHandler<OpenRecallCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenRecallCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecallCase? existing = await recalls.FindByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || existing.CaseNumber != command.CaseNumber.Trim()
                || existing.LotReference != command.LotReference.Trim() || existing.Reason != command.Reason.Trim())
            {
                throw new InvalidOperationException("The recall operation was replayed with different content.");
            }
            return existing.Id;
        }
        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The recall company is not the active company.");
        }
        RecallCase recall = RecallCase.Open(tenant.TenantId, null, command.CompanyId, command.OperationId, command.CaseNumber,
            command.LotReference, command.Reason, clock.UtcNow);
        recalls.Add(recall);
        return recall.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AddRecallTraceCommand(Guid RecallCaseId, string Kind, string Reference) : ICommand;

public sealed class AddRecallTraceCommandHandler(IRecallCaseRepository recalls, ICompanyContext company)
    : ICommandHandler<AddRecallTraceCommand, Unit>
{
    public async Task<Unit> HandleAsync(AddRecallTraceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecallCase recall = await recalls.FindAsync(command.RecallCaseId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Recall case not found.");
        if (company.CompanyId is not { } activeCompany || recall.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The recall company is not the active company.");
        }
        recall.AddTraceReference(command.Kind, command.Reference);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CloseRecallCommand(Guid RecallCaseId, string Reason) : ICommand;

public sealed class CloseRecallCommandHandler(IRecallCaseRepository recalls, ICompanyContext company, IClock clock)
    : ICommandHandler<CloseRecallCommand, Unit>
{
    public async Task<Unit> HandleAsync(CloseRecallCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecallCase recall = await recalls.FindAsync(command.RecallCaseId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Recall case not found.");
        if (company.CompanyId is not { } activeCompany || recall.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The recall company is not the active company.");
        }
        if (recall.Status == RecallCaseStatus.Closed)
        {
            return Unit.Value;
        }
        recall.Close(clock.UtcNow, command.Reason);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record IssueQualityCertificateCommand(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, string CertificateNumber,
    string Issuer, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string Evidence) : ICommand<Guid>;

public sealed class IssueQualityCertificateCommandHandler(IQualityCertificateRepository certificates, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<IssueQualityCertificateCommand, Guid>
{
    public Task<Guid> HandleAsync(IssueQualityCertificateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The certificate company is not the active company.");
        }
        QualityCertificate certificate = QualityCertificate.Issue(tenant.TenantId, null, command.CompanyId, command.ItemId, command.ItemVariantId,
            command.CertificateNumber, command.Issuer, command.IssuedAt, command.ExpiresAt, command.Evidence);
        certificates.Add(certificate);
        return Task.FromResult(certificate.Id);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RevokeQualityCertificateCommand(Guid CertificateId, string Reason) : ICommand;

public sealed class RevokeQualityCertificateCommandHandler(IQualityCertificateRepository certificates, ICompanyContext company, IClock clock)
    : ICommandHandler<RevokeQualityCertificateCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeQualityCertificateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        QualityCertificate certificate = await certificates.FindAsync(command.CertificateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality certificate not found.");
        if (company.CompanyId is not { } activeCompany || certificate.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The quality certificate company is not the active company.");
        }
        if (certificate.Status == QualityCertificateStatus.Revoked)
        {
            return Unit.Value;
        }
        certificate.Revoke(clock.UtcNow, command.Reason);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateInspectionPlanCommand(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, int Version,
    string Name, int SampleSize, string AcceptanceCriteria) : ICommand<Guid>;

public sealed class CreateInspectionPlanCommandHandler(IInspectionPlanRepository plans, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<CreateInspectionPlanCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateInspectionPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } activeCompany || activeCompany != command.CompanyId)
        {
            throw new InvalidOperationException("The inspection-plan company is not the active company.");
        }
        InspectionPlan plan = InspectionPlan.Create(tenant.TenantId, null, command.CompanyId, command.ItemId, command.ItemVariantId,
            command.Version, command.Name, command.SampleSize, command.AcceptanceCriteria);
        plans.Add(plan);
        return Task.FromResult(plan.Id);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishInspectionPlanCommand(Guid PlanId) : ICommand;

public sealed class PublishInspectionPlanCommandHandler(IInspectionPlanRepository plans, ICompanyContext company)
    : ICommandHandler<PublishInspectionPlanCommand, Unit>
{
    public async Task<Unit> HandleAsync(PublishInspectionPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        InspectionPlan plan = await plans.FindAsync(command.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Inspection plan not found.");
        if (company.CompanyId is not { } activeCompany || plan.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The inspection-plan company is not the active company.");
        }
        plan.Publish();
        return Unit.Value;
    }
}

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

public sealed class ReleaseQualityHoldCommandHandler(IQualityHoldRepository holds, IReservationService reservations, ICompanyContext company, IClock clock)
    : ICommandHandler<ReleaseQualityHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(ReleaseQualityHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (company.CompanyId is not { } activeCompany || hold.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The quality hold company is not the active company.");
        }
        if (hold.Status == QualityHoldStatus.Released)
        {
            return Unit.Value;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        await reservations.ReleaseAsync(hold.ReservationId, command.Reason, cancellationToken).ConfigureAwait(false);
        hold.Release(clock.UtcNow, command.Reason);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RejectQualityHoldCommand(Guid HoldId, string Reason) : ICommand;

public sealed class RejectQualityHoldCommandHandler(IQualityHoldRepository holds, IReservationService reservations, ICompanyContext company, IClock clock)
    : ICommandHandler<RejectQualityHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectQualityHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        QualityHold hold = await holds.FindAsync(command.HoldId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Quality hold not found.");
        if (company.CompanyId is not { } activeCompany || hold.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The quality hold company is not the active company.");
        }
        if (hold.Status == QualityHoldStatus.Rejected)
        {
            return Unit.Value;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        await reservations.ConsumeAsync(hold.ReservationId, hold.Id, cancellationToken).ConfigureAwait(false);
        hold.Reject(clock.UtcNow, command.Reason);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RecordInspectionCommand(Guid OperationId, Guid CompanyId, Guid HoldId, Guid? PlanId, bool Passed, int SampleSize, string Evidence) : ICommand<Guid>;

public sealed class RecordInspectionCommandHandler(
    IInspectionResultRepository inspections, IInspectionPlanRepository plans, IQualityHoldRepository holds, ITenantContext tenant,
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
        InspectionPlan? plan = command.PlanId is { } planId
            ? await plans.FindAsync(planId, cancellationToken).ConfigureAwait(false)
            : null;
        if (command.PlanId is not null && (plan is null || plan.CompanyId != command.CompanyId || plan.Status != InspectionPlanStatus.Published))
        {
            throw new InvalidOperationException("Inspection requires a published plan in the selected company.");
        }
        if (plan is not null && command.SampleSize < plan.SampleSize)
        {
            throw new InvalidOperationException("Inspection sample size is below the plan requirement.");
        }
        InspectionResult result = InspectionResult.Record(tenant.TenantId, null, command.CompanyId, command.HoldId,
            plan?.Id, plan?.Version ?? 0, command.OperationId, command.Passed, command.SampleSize, command.Evidence, clock.UtcNow);
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

public sealed class StartCorrectiveActionCommandHandler(INonConformanceRepository nonConformances, ICompanyContext company)
    : ICommandHandler<StartCorrectiveActionCommand, Unit>
{
    public async Task<Unit> HandleAsync(StartCorrectiveActionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        NonConformance issue = await nonConformances.FindAsync(command.NonConformanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Non-conformance not found.");
        if (company.CompanyId is not { } activeCompany || issue.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The non-conformance company is not the active company.");
        }
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

public sealed class CloseNonConformanceCommandHandler(INonConformanceRepository nonConformances, ICompanyContext company, IClock clock)
    : ICommandHandler<CloseNonConformanceCommand, Unit>
{
    public async Task<Unit> HandleAsync(CloseNonConformanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        NonConformance issue = await nonConformances.FindAsync(command.NonConformanceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Non-conformance not found.");
        if (company.CompanyId is not { } activeCompany || issue.CompanyId != activeCompany)
        {
            throw new InvalidOperationException("The non-conformance company is not the active company.");
        }
        if (issue.Status == NonConformanceStatus.Closed && issue.ClosureOperationId == command.OperationId)
        {
            return Unit.Value;
        }
        issue.Close(command.OperationId, clock.UtcNow, command.Resolution);
        return Unit.Value;
    }
}
