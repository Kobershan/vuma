#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;

namespace VumaRetail.Application.Hr;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePayrollRunCommand(Guid CompanyId, DateOnly From, DateOnly To, string Currency, Guid RequestId, IReadOnlyCollection<PayrollDeductionInput> Deductions) : ICommand<Guid>;
public sealed record PayrollDeductionInput(Guid EmployeeId, decimal Amount);
[CommandSideEffect(SideEffect.Write)]
public sealed record FinalizePayrollRunCommand(Guid CompanyId, Guid RunId) : ICommand;
public sealed record PayrollRunResult(Guid Id, Guid CompanyId, DateOnly From, DateOnly To, string Currency, PayrollRunStatus Status, decimal GrossAmount, decimal DeductionAmount, decimal NetAmount);

public sealed class CreatePayrollRunCommandHandler(IEmployeeRepository employees, IEmploymentContractRepository contracts, IAttendanceRepository attendance, IPayrollRepository payroll, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<CreatePayrollRunCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePayrollRunCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); EnsureCompany(command.CompanyId);
        if (await payroll.FindByRequestIdAsync(command.RequestId, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (existing.CompanyId != command.CompanyId || existing.From != command.From || existing.To != command.To || existing.Currency != command.Currency.Trim().ToUpperInvariant())
            {
                throw new InvalidOperationException("Payroll request identity was reused with different content.");
            }
            return existing.Id;
        }
        PayrollRun run = PayrollRun.Create(tenant.TenantId, command.CompanyId, command.From, command.To, command.Currency, command.RequestId);
        DateTimeOffset from = new(command.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), to = new(command.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        IReadOnlyList<AttendanceRecord> allAttendance = await attendance.ListAsync(from, to, null, cancellationToken).ConfigureAwait(false);
        foreach (Employee employee in (await employees.ListAsync(cancellationToken).ConfigureAwait(false)).Where(x => x.TenantId == tenant.TenantId && x.CompanyId == command.CompanyId && x.Status != EmploymentStatus.Terminated))
        {
            EmploymentContract? contract = (await contracts.ListAsync(employee.Id, cancellationToken).ConfigureAwait(false)).Where(x => x.CompanyId == command.CompanyId && x.StartsOn <= command.To && (x.EndsOn is null || x.EndsOn >= command.From)).OrderByDescending(x => x.StartsOn).FirstOrDefault();
            if (contract is null || contract.Currency != run.Currency)
            {
                continue;
            }
            decimal hours = PayrollHoursCalculator.Calculate(allAttendance.Where(x => x.EmployeeId == employee.Id).OrderBy(x => x.OccurredAt).ToArray());
            decimal deductions = command.Deductions.FirstOrDefault(x => x.EmployeeId == employee.Id)?.Amount ?? 0m;
            PayrollLine line = PayrollLine.Create(tenant.TenantId, command.CompanyId, run.Id, employee.Id, hours, contract.HourlyRate, deductions, run.Currency);
            payroll.AddLine(line); run.AddLine(line);
        }
        payroll.Add(run); return run.Id;
    }
    private void EnsureCompany(Guid id)
    {
        if (company.CompanyId != id)
        {
            throw new InvalidOperationException("The payroll company is not active.");
        }
    }
}

public sealed class FinalizePayrollRunCommandHandler(IPayrollRepository payroll, ICompanyContext company, ITenantContext tenant) : ICommandHandler<FinalizePayrollRunCommand, Unit>
{
    public async Task<Unit> HandleAsync(FinalizePayrollRunCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId != command.CompanyId) { throw new InvalidOperationException("The payroll company is not active."); }
        PayrollRun run = await payroll.FindAsync(command.RunId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Payroll run was not found.");
        if (run.TenantId != tenant.TenantId || run.CompanyId != command.CompanyId) { throw new InvalidOperationException("Payroll run is outside the active company."); }
        run.FinalizeRun();
        return Unit.Value;
    }
}

public interface IPayrollRepository
{
    Task<PayrollRun?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PayrollRun?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    void Add(PayrollRun run);
    void AddLine(PayrollLine line);
}
