#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Application.Projects;

public sealed record GetProjectCostSummaryQuery(Guid CompanyId, Guid ProjectId) : IQuery<ProjectCostSummaryResult?>;
public sealed record ProjectCostSummaryResult(Guid ProjectId, Guid CompanyId, int EntryCount, IReadOnlyList<ProjectCostTotal> Totals);
public sealed record ProjectCostTotal(string Currency, decimal Amount);

public sealed record ProjectJobCostBudget(string Currency, decimal Budget, decimal Committed, decimal Actual, decimal Available);
public sealed record ProjectJobCostContract(Guid Id, string Number, decimal OriginalValue, string Currency);
public sealed record ProjectJobCostReportResult(Guid ProjectId, Guid CompanyId,
    IReadOnlyList<ProjectJobCostBudget> Budgets,
    IReadOnlyList<ProjectCostTotal> Costs,
    IReadOnlyList<ProjectJobCostContract> Contracts);

public sealed record GetProjectJobCostReportQuery(Guid CompanyId, Guid ProjectId) : IQuery<ProjectJobCostReportResult?>;

public sealed class GetProjectJobCostReportQueryHandler(IProjectRepository projects, ICompanyContext company, ITenantContext tenant)
    : IQueryHandler<GetProjectJobCostReportQuery, ProjectJobCostReportResult?>
{
    public async Task<ProjectJobCostReportResult?> HandleAsync(GetProjectJobCostReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateProjectCommandHandler.EnsureCompany(company, query.CompanyId);
        Project? project = await projects.FindProjectAsync(query.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || project.TenantId != tenant.TenantId || project.CompanyId != query.CompanyId)
        {
            return null;
        }

        IReadOnlyList<ProjectBudget> budgets = await projects.ListBudgetsAsync(project.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProjectCostEntry> costs = await projects.ListCostsAsync(project.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProjectContract> contracts = await projects.ListContractsAsync(project.Id, cancellationToken).ConfigureAwait(false);
        ProjectJobCostBudget[] budgetResults = budgets
            .Where(x => x.TenantId == tenant.TenantId && x.CompanyId == query.CompanyId)
            .Select(x => new ProjectJobCostBudget(x.Amount.Currency, x.Amount.Amount, x.Committed.Amount,
                x.Actual.Amount, x.Available.Amount)).ToArray();
        ProjectCostTotal[] costResults = costs
            .Where(x => x.TenantId == tenant.TenantId && x.CompanyId == query.CompanyId)
            .GroupBy(x => x.Amount.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProjectCostTotal(x.Key, x.Sum(entry => entry.Amount.Amount))).ToArray();
        ProjectJobCostContract[] contractResults = contracts
            .Where(x => x.TenantId == tenant.TenantId && x.CompanyId == query.CompanyId)
            .Select(x => new ProjectJobCostContract(x.Id, x.Number, x.OriginalValue.Amount, x.OriginalValue.Currency)).ToArray();
        return new ProjectJobCostReportResult(project.Id, query.CompanyId, budgetResults, costResults, contractResults);
    }
}

public sealed class GetProjectCostSummaryQueryHandler(IProjectRepository projects, ICompanyContext company)
    : IQueryHandler<GetProjectCostSummaryQuery, ProjectCostSummaryResult?>
{
    public async Task<ProjectCostSummaryResult?> HandleAsync(GetProjectCostSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateProjectCommandHandler.EnsureCompany(company, query.CompanyId);
        Project? project = await projects.FindProjectAsync(query.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || project.CompanyId != query.CompanyId)
        {
            return null;
        }
        IReadOnlyList<ProjectCostEntry> entries = await projects.ListCostsAsync(project.Id, cancellationToken).ConfigureAwait(false);
        ProjectCostTotal[] totals = entries.GroupBy(x => x.Amount.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProjectCostTotal(x.Key, x.Sum(entry => entry.Amount.Amount)))
            .ToArray();
        return new ProjectCostSummaryResult(project.Id, query.CompanyId, entries.Count, totals);
    }
}

public sealed record GetProjectQuery(Guid CompanyId, Guid ProjectId) : IQuery<ProjectResult?>;
public sealed record ProjectResult(Guid Id, Guid CompanyId, string Code, string Name, string Currency, string Status);
public sealed record ListProjectsQuery(Guid CompanyId) : IQuery<IReadOnlyList<ProjectResult>>;

public sealed class GetProjectQueryHandler(IProjectRepository projects, ICompanyContext company)
    : IQueryHandler<GetProjectQuery, ProjectResult?>
{
    public async Task<ProjectResult?> HandleAsync(GetProjectQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateProjectCommandHandler.EnsureCompany(company, query.CompanyId);
        Project? project = await projects.FindProjectAsync(query.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || project.CompanyId != query.CompanyId)
        {
            return null;
        }
        return new ProjectResult(project.Id, query.CompanyId, project.Code, project.Name, project.Currency, project.Status.ToString());
    }
}

public sealed class ListProjectsQueryHandler(IProjectRepository projects, ICompanyContext company)
    : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectResult>>
{
    public async Task<IReadOnlyList<ProjectResult>> HandleAsync(ListProjectsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateProjectCommandHandler.EnsureCompany(company, query.CompanyId);
        IReadOnlyList<Project> found = await projects.ListProjectsAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        return found.Select(p => new ProjectResult(p.Id, query.CompanyId, p.Code, p.Name, p.Currency, p.Status.ToString())).ToList();
    }
}
