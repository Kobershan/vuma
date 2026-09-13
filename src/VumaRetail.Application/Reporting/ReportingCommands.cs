#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Application.Reporting;

[CommandSideEffect(SideEffect.Write)]
public sealed record RequestReportExportCommand(Guid CompanyId, Guid OperationId, string ReportCode) : ICommand<Guid>;

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
