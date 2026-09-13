using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Projects;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(VumaRetailDbContext context) : IProjectRepository
{
    public Task<Project?> FindProjectAsync(Guid id, CancellationToken cancellationToken = default) => context.Projects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ProjectBudget?> FindBudgetAsync(Guid id, CancellationToken cancellationToken = default) => context.ProjectBudgets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ProjectContract?> FindContractAsync(Guid id, CancellationToken cancellationToken = default) => context.ProjectContracts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ContractVariation?> FindVariationAsync(Guid id, CancellationToken cancellationToken = default) => context.ContractVariations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<BillingMilestone?> FindMilestoneAsync(Guid id, CancellationToken cancellationToken = default) => context.BillingMilestones.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ProjectCostEntry?> FindCostEntryAsync(Guid id, CancellationToken cancellationToken = default) => context.ProjectCostEntries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public void Add(Project project) => context.Projects.Add(project);
    public void Add(ProjectBudget budget) => context.ProjectBudgets.Add(budget);
    public void Add(ProjectContract contract) => context.ProjectContracts.Add(contract);
    public void Add(ContractVariation variation) => context.ContractVariations.Add(variation);
    public void Add(BillingMilestone milestone) => context.BillingMilestones.Add(milestone);
    public void Add(ProjectCostEntry entry) => context.ProjectCostEntries.Add(entry);
}
