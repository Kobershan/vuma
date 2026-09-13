#pragma warning disable CS1591, IDE0011
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Projects;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Projects;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateProjectCommand(Guid CompanyId, string Code, string Name, string Currency) : ICommand<Guid>;

[CommandSideEffect(SideEffect.Write)]
public sealed record AllocateProjectCostCommand(Guid CompanyId, Guid ProjectId, string SourceReference,
    ProjectCostKind Kind, decimal Amount, string Currency) : ICommand<Guid>;

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
        if (project.CompanyId != command.CompanyId) throw new InvalidOperationException("The project company is not the active company.");
        ProjectCostEntry? existing = await projects.FindCostBySourceAsync(command.ProjectId, command.SourceReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Kind != command.Kind || existing.Amount != new Money(command.Amount, command.Currency))
                throw new InvalidOperationException("The cost source was already allocated with different content.");
            return existing.Id;
        }
        ProjectCostEntry entry = ProjectCostEntry.Record(tenant.TenantId, null, command.CompanyId, command.ProjectId,
            command.SourceReference, command.Kind, new Money(command.Amount, command.Currency));
        projects.Add(entry);
        return entry.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveProjectBudgetCommand(Guid CompanyId, Guid BudgetId) : ICommand;

public sealed class ApproveProjectBudgetCommandHandler(IProjectRepository projects, ICompanyContext company)
    : ICommandHandler<ApproveProjectBudgetCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApproveProjectBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        ProjectBudget budget = await projects.FindBudgetAsync(command.BudgetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project budget not found.");
        if (budget.CompanyId != command.CompanyId) throw new InvalidOperationException("The budget company is not the active company.");
        budget.Submit(); budget.Approve(); return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveContractVariationCommand(Guid CompanyId, Guid VariationId) : ICommand;

public sealed class ApproveContractVariationCommandHandler(IProjectRepository projects, ICompanyContext company)
    : ICommandHandler<ApproveContractVariationCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApproveContractVariationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        ContractVariation variation = await projects.FindVariationAsync(command.VariationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Contract variation not found.");
        if (variation.CompanyId != command.CompanyId) throw new InvalidOperationException("The variation company is not the active company.");
        variation.Approve(); return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record BillMilestoneCommand(Guid CompanyId, Guid MilestoneId) : ICommand<Guid>;

public sealed class BillMilestoneCommandHandler(IProjectRepository projects, ICompanyContext company)
    : ICommandHandler<BillMilestoneCommand, Guid>
{
    public async Task<Guid> HandleAsync(BillMilestoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); CreateProjectCommandHandler.EnsureCompany(company, command.CompanyId);
        BillingMilestone milestone = await projects.FindMilestoneAsync(command.MilestoneId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Billing milestone not found.");
        if (milestone.CompanyId != command.CompanyId) throw new InvalidOperationException("The milestone company is not the active company.");
        milestone.MarkBilled(); return milestone.Id;
    }
}
