#pragma warning disable CS1591
using VumaRetail.Domain.Projects;

namespace VumaRetail.Application.Projects;

public interface IProjectRepository
{
    Task<Project?> FindProjectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectBudget?> FindBudgetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectContract?> FindContractAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContractVariation?> FindVariationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BillingMilestone?> FindMilestoneAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectCostEntry?> FindCostEntryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectCostEntry?> FindCostBySourceAsync(Guid projectId, string sourceReference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectCostEntry>> ListCostsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListProjectsAsync(Guid companyId, CancellationToken cancellationToken = default);
    void Add(Project project); void Add(ProjectBudget budget); void Add(ProjectContract contract);
    void Add(ContractVariation variation); void Add(BillingMilestone milestone);
    void Add(ProjectCostEntry entry);
}
