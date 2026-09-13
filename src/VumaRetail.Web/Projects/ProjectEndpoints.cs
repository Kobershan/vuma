#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Projects;
using VumaRetail.Domain.Projects;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Projects;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapVumaProjects(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapVumaApi().MapGroup("/projects").WithTags("Projects").RequireModule("projects");
        group.MapPost("/", CreateAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/{projectId:guid}/costs", AllocateCostAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/budgets/{id:guid}/approve", ApproveBudgetAsync).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/contract-variations/{id:guid}/approve", ApproveVariationAsync).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/milestones/{id:guid}/bill", BillMilestoneAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status202Accepted);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateProjectRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateProjectCommand(request.CompanyId, request.Code, request.Name, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/projects/{id:D}", id);
    }

    private static async Task<IResult> ApproveBudgetAsync(Guid id, ProjectCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ApproveProjectBudgetCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> AllocateCostAsync(Guid projectId, AllocateProjectCostRequest request,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new AllocateProjectCostCommand(request.CompanyId, projectId,
            request.SourceReference, request.Kind, request.Amount, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/projects/{projectId:D}/costs/{id:D}", id);
    }

    private static async Task<IResult> ApproveVariationAsync(Guid id, ProjectCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ApproveContractVariationCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> BillMilestoneAsync(Guid id, ProjectCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid billedId = await dispatcher.SendAsync(new BillMilestoneCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/api/v1/projects/milestones/{billedId:D}", billedId);
    }

    public sealed record CreateProjectRequest(Guid CompanyId, string Code, string Name, string Currency);
    public sealed record ProjectCompanyRequest(Guid CompanyId);
    public sealed record AllocateProjectCostRequest(Guid CompanyId, string SourceReference, ProjectCostKind Kind,
        decimal Amount, string Currency);
}
