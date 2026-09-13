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
