#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

[CommandSideEffect(SideEffect.Write)]
public sealed record RequestReportExportCommand(Guid CompanyId, Guid OperationId, string ReportCode) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteReportExportCommand(Guid CompanyId, Guid ExportId, string ArtifactReference) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record FailReportExportCommand(Guid CompanyId, Guid ExportId, string Reason) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record ScheduleReportCommand(Guid CompanyId, string ReportCode, int IntervalMinutes, DateTimeOffset FirstRunAtUtc) : ICommand<Guid>;

public sealed class RequestReportExportCommandHandler(IReportingRepository reports, ITenantContext tenant, ICompanyContext company, IClock clock) : ICommandHandler<RequestReportExportCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestReportExportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
        {
            throw new InvalidOperationException("The report company is not the active company.");
        }
        ReportExport? existing = await reports.FindExportByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || !string.Equals(existing.ReportCode, command.ReportCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The report export operation was replayed with different content.");
            }
            return existing.Id;
        }
        if (await reports.FindPublishedDefinitionByCodeAsync(command.ReportCode, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException("The requested report is not published.");
        }
        ReportExport export = ReportExport.Queue(tenant.TenantId, tenant.StoreId, command.CompanyId, command.OperationId, command.ReportCode, clock.UtcNow);
        reports.Add(export);
        return export.Id;
    }
}

public sealed class CompleteReportExportCommandHandler(IReportingRepository reports, ITenantContext tenant, ICompanyContext company, IClock clock) : ICommandHandler<CompleteReportExportCommand, Unit>
{
    public async Task<Unit> HandleAsync(CompleteReportExportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
        {
            throw new InvalidOperationException("The report company is not the active company.");
        }
        ReportExport export = await reports.FindExportAsync(command.ExportId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Report export was not found.");
        EnsureScope(tenant, company, export.TenantId, export.CompanyId!.Value);
        export.Complete(clock.UtcNow, command.ArtifactReference);
        return Unit.Value;
    }

    internal static void EnsureScope(ITenantContext tenant, ICompanyContext context, Guid expectedTenant, Guid expectedCompany)
    {
        if (tenant.TenantId != expectedTenant || context.CompanyId is not { } active || active != expectedCompany)
        {
            throw new InvalidOperationException("The report is outside the active tenant/company scope.");
        }
    }
}

public sealed class FailReportExportCommandHandler(IReportingRepository reports, ITenantContext tenant, ICompanyContext company) : ICommandHandler<FailReportExportCommand, Unit>
{
    public async Task<Unit> HandleAsync(FailReportExportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
        {
            throw new InvalidOperationException("The report company is not the active company.");
        }
        ReportExport export = await reports.FindExportAsync(command.ExportId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Report export was not found.");
        CompleteReportExportCommandHandler.EnsureScope(tenant, company, export.TenantId, export.CompanyId!.Value);
        export.Fail(command.Reason);
        return Unit.Value;
    }
}

public sealed class ScheduleReportCommandHandler(IReportingRepository reports, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<ScheduleReportCommand, Guid>
{
    public async Task<Guid> HandleAsync(ScheduleReportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
        {
            throw new InvalidOperationException("The report company is not the active company.");
        }
        if (await reports.FindPublishedDefinitionByCodeAsync(command.ReportCode, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException("The requested report is not published.");
        }
        ScheduledReport schedule = ScheduledReport.Create(tenant.TenantId, tenant.StoreId, command.CompanyId,
            command.ReportCode, command.IntervalMinutes, command.FirstRunAtUtc);
        reports.Add(schedule);
        return schedule.Id;
    }
}
