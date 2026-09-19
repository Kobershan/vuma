#pragma warning disable CS1591, IDE0011
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Application.Projects;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateProjectCommand(Guid CompanyId, string Code, string Name, string Currency) : ICommand<Guid>;

[CommandSideEffect(SideEffect.Write)]
public sealed record AllocateProjectCostCommand(Guid CompanyId, Guid ProjectId, string SourceReference,
    ProjectCostKind Kind, decimal Amount, string Currency) : ICommand<Guid>;

[CommandSideEffect(SideEffect.Write)]
public sealed record AllocateProjectLabourCostCommand(Guid CompanyId, Guid ProjectId, Guid EmployeeId,
    DateOnly From, DateOnly To) : ICommand<Guid>;

public sealed class CreateProjectCommandHandler(IProjectRepository projects, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<CreateProjectCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); EnsureCompany(company, command.CompanyId);
        Project project = Project.Create(tenant.TenantId, null, command.CompanyId, command.Code, command.Name, command.Currency);
        projects.Add(project); return Task.FromResult(project.Id);
    }

    internal static void EnsureCompany(ICompanyContext company, Guid expected)
    {
        if (company.CompanyId is not { } active || active != expected) throw new InvalidOperationException("The project company is not the active company.");
    }
}

public sealed class AllocateProjectCostCommandHandler(IProjectRepository projects, ITenantContext tenant,
    ICompanyContext company) : ICommandHandler<AllocateProjectCostCommand, Guid>
{
    public async Task<Guid> HandleAsync(AllocateProjectCostCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        Project project = await projects.FindProjectAsync(command.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project not found.");
        if (project.TenantId != tenant.TenantId || project.CompanyId != command.CompanyId) throw new InvalidOperationException("The project is outside the active tenant/company scope.");
        ProjectCostEntry? existing = await projects.FindCostBySourceAsync(command.ProjectId, command.SourceReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.TenantId != tenant.TenantId || existing.CompanyId != command.CompanyId || existing.Kind != command.Kind || existing.Amount != new Money(command.Amount, command.Currency))
                throw new InvalidOperationException("The cost source was already allocated with different content.");
            return existing.Id;
        }
        ProjectCostEntry entry = ProjectCostEntry.Record(tenant.TenantId, null, command.CompanyId, command.ProjectId,
            command.SourceReference, command.Kind, new Money(command.Amount, command.Currency));
        projects.Add(entry);
        return entry.Id;
    }
}

public sealed class AllocateProjectLabourCostCommandHandler(
    IProjectRepository projects, IEmployeeRepository employees,
    IEmploymentContractRepository contracts, IAttendanceRepository attendance,
    ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<AllocateProjectLabourCostCommand, Guid>
{
    public async Task<Guid> HandleAsync(AllocateProjectLabourCostCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        if (command.To < command.From) throw new ArgumentException("Labour period cannot end before it starts.", nameof(command));
        Employee employee = await employees.FindAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Employee not found.");
        if (employee.TenantId != tenant.TenantId || employee.CompanyId != command.CompanyId)
            throw new InvalidOperationException("The employee is outside the active tenant/company scope.");
        DateTimeOffset from = new(command.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new(command.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        AttendanceRecord[] events = (await attendance.ListAsync(from, to, employee.Id, cancellationToken)
                .ConfigureAwait(false))
            .Where(x => x.TenantId == tenant.TenantId).OrderBy(x => x.OccurredAt).ToArray();
        decimal hours = PayrollHoursCalculator.Calculate(events);
        EmploymentContract contract = (await contracts.ListAsync(employee.Id, cancellationToken)
                .ConfigureAwait(false))
            .Where(x => x.TenantId == tenant.TenantId && x.StartsOn <= command.To
                && (x.EndsOn is null || x.EndsOn >= command.From))
            .OrderByDescending(x => x.StartsOn).FirstOrDefault()
            ?? throw new InvalidOperationException("Employee has no contract for the labour period.");
        string source = $"labour:{employee.Id:D}:{command.From:yyyy-MM-dd}:{command.To:yyyy-MM-dd}";
        return await new AllocateProjectCostCommandHandler(projects, tenant, company).HandleAsync(
            new AllocateProjectCostCommand(command.CompanyId, command.ProjectId, source,
                ProjectCostKind.Labour, decimal.Round(hours * contract.HourlyRate, 2, MidpointRounding.AwayFromZero), contract.Currency),
            cancellationToken).ConfigureAwait(false);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveProjectBudgetCommand(Guid CompanyId, Guid BudgetId) : ICommand;

public sealed class ApproveProjectBudgetCommandHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : ICommandHandler<ApproveProjectBudgetCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApproveProjectBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        ProjectBudget budget = await projects.FindBudgetAsync(command.BudgetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project budget not found.");
        if (budget.TenantId != tenant.TenantId || budget.CompanyId != command.CompanyId) throw new InvalidOperationException("The budget is outside the active tenant/company scope.");
        budget.Submit(); budget.Approve(); return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveContractVariationCommand(Guid CompanyId, Guid VariationId) : ICommand;

public sealed class ApproveContractVariationCommandHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : ICommandHandler<ApproveContractVariationCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApproveContractVariationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        ContractVariation variation = await projects.FindVariationAsync(command.VariationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Contract variation not found.");
        if (variation.TenantId != tenant.TenantId || variation.CompanyId != command.CompanyId) throw new InvalidOperationException("The variation is outside the active tenant/company scope.");
        variation.Approve(); return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record BillMilestoneCommand(Guid CompanyId, Guid MilestoneId) : ICommand<Guid>;
public sealed class BillMilestoneCommandHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : ICommandHandler<BillMilestoneCommand, Guid>
{
    public async Task<Guid> HandleAsync(BillMilestoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        BillingMilestone milestone = await projects.FindMilestoneAsync(command.MilestoneId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Billing milestone not found.");
        if (milestone.TenantId != tenant.TenantId || milestone.CompanyId != command.CompanyId) throw new InvalidOperationException("The milestone is outside the active tenant/company scope.");
        milestone.MarkBilled(); return milestone.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ActivateProjectCommand(Guid CompanyId, Guid ProjectId) : ICommand;

public sealed class ActivateProjectCommandHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : ICommandHandler<ActivateProjectCommand, Unit>
{
    public async Task<Unit> HandleAsync(ActivateProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        Project project = await projects.FindProjectAsync(command.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project not found.");
        if (project.TenantId != tenant.TenantId || project.CompanyId != command.CompanyId) throw new InvalidOperationException("The project is outside the active tenant/company scope.");
        project.Activate(); return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CloseProjectCommand(Guid CompanyId, Guid ProjectId) : ICommand;

public sealed class CloseProjectCommandHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : ICommandHandler<CloseProjectCommand, Unit>
{
    public async Task<Unit> HandleAsync(CloseProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        Project project = await projects.FindProjectAsync(command.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project not found.");
        if (project.TenantId != tenant.TenantId || project.CompanyId != command.CompanyId) throw new InvalidOperationException("The project is outside the active tenant/company scope.");
        project.Close(); return Unit.Value;
    }
}
