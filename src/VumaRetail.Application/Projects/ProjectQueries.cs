#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Application.Projects;

public sealed record GetProjectCostSummaryQuery(Guid CompanyId, Guid ProjectId) : IQuery<ProjectCostSummaryResult?>;
public sealed record ProjectCostSummaryResult(Guid ProjectId, Guid CompanyId, int EntryCount, IReadOnlyList<ProjectCostTotal> Totals);
public sealed record ProjectCostTotal(string Currency, decimal Amount);

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
