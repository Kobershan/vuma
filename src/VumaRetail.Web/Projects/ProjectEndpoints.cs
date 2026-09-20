#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
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
        group.MapPost("/{projectId:guid}/contracts", CreateContractAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/{projectId:guid}/costs", AllocateCostAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/{projectId:guid}/labour-costs", AllocateLabourCostAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapGet("/{projectId:guid}/costs/summary", async (Guid projectId, Guid companyId, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(companyId);
            return Results.Ok(await dispatcher.QueryAsync(new GetProjectCostSummaryQuery(companyId, projectId), cancellationToken));
        })
            .RequirePermission(ProjectPermissions.View);
        group.MapGet("/{projectId:guid}/job-cost", async (Guid projectId, Guid companyId, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(companyId);
            ProjectJobCostReportResult? report = await dispatcher.QueryAsync(new GetProjectJobCostReportQuery(companyId, projectId), cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        }).RequirePermission(ProjectPermissions.View);
        group.MapPost("/budgets/{id:guid}/approve", ApproveBudgetAsync).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/contract-variations/{id:guid}/approve", ApproveVariationAsync).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/milestones/{id:guid}/bill", BillMilestoneAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status202Accepted);
        group.MapPost("/rebates", CreateRebateAsync).RequirePermission(ProjectPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapGet("/rebates", async (Guid companyId, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(companyId);
            return Results.Ok(await dispatcher.QueryAsync(new ListRebatesQuery(companyId), cancellationToken));
        }).RequirePermission(ProjectPermissions.View);
        group.MapPost("/rebates/{id:guid}/activate", ChangeRebateAsync).RequirePermission(ProjectPermissions.Manage);
        group.MapPost("/rebates/{id:guid}/reconcile", ReconcileRebateAsync).RequirePermission(ProjectPermissions.Manage);
        group.MapPost("/rebates/{id:guid}/calculate", CalculateRebateAsync).RequirePermission(ProjectPermissions.View);
        group.MapGet("/", async (Guid companyId, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(companyId);
            return Results.Ok(await dispatcher.QueryAsync(new ListProjectsQuery(companyId), cancellationToken));
        }).RequirePermission(ProjectPermissions.View);
        group.MapGet("/{projectId:guid}", async (Guid projectId, Guid companyId, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(companyId);
            ProjectResult? result = await dispatcher.QueryAsync(new GetProjectQuery(companyId, projectId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission(ProjectPermissions.View);
        group.MapPost("/{projectId:guid}/activate", async (Guid projectId, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(request.CompanyId);
            await dispatcher.SendAsync(new ActivateProjectCommand(request.CompanyId, projectId), cancellationToken);
            return Results.NoContent();
        }).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{projectId:guid}/close", async (Guid projectId, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            company.SetCompany(request.CompanyId);
            await dispatcher.SendAsync(new CloseProjectCommand(request.CompanyId, projectId), cancellationToken);
            return Results.NoContent();
        }).RequirePermission(ProjectPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateProjectRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateProjectCommand(request.CompanyId, request.Code, request.Name, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/projects/{id:D}", id);
    }

    private static async Task<IResult> ApproveBudgetAsync(Guid id, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        await dispatcher.SendAsync(new ApproveProjectBudgetCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> AllocateCostAsync(Guid projectId, AllocateProjectCostRequest request,
        ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new AllocateProjectCostCommand(request.CompanyId, projectId,
            request.SourceReference, request.Kind, request.Amount, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/projects/{projectId:D}/costs/{id:D}", id);
    }

    private static async Task<IResult> AllocateLabourCostAsync(Guid projectId, AllocateProjectLabourCostRequest request,
        ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new AllocateProjectLabourCostCommand(request.CompanyId, projectId,
            request.EmployeeId, request.From, request.To), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/projects/{projectId:D}/costs/{id:D}", id);
    }

    private static async Task<IResult> ApproveVariationAsync(Guid id, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        await dispatcher.SendAsync(new ApproveContractVariationCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> BillMilestoneAsync(Guid id, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid billedId = await dispatcher.SendAsync(new BillMilestoneCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/api/v1/projects/milestones/{billedId:D}", billedId);
    }

    private static async Task<IResult> CreateRebateAsync(CreateRebateRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateRebateAgreementCommand(request.CompanyId, request.Number, request.Rate, request.ThresholdAmount, request.Currency), cancellationToken);
        return Results.Created($"/api/v1/projects/rebates/{id:D}", id);
    }

    private static async Task<IResult> CreateContractAsync(Guid projectId, CreateContractRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateProjectContractCommand(request.CompanyId, projectId, request.Number, request.OriginalValue, request.Currency), cancellationToken);
        return Results.Created($"/api/v1/projects/{projectId:D}/contracts/{id:D}", id);
    }

    private static async Task<IResult> CalculateRebateAsync(Guid id, CalculateRebateRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        return Results.Ok(await dispatcher.QueryAsync(new CalculateRebateQuery(request.CompanyId, id, request.EligibleAmount, request.Currency), cancellationToken));
    }

    private static async Task<IResult> ChangeRebateAsync(Guid id, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        await dispatcher.SendAsync(new ActivateRebateAgreementCommand(request.CompanyId, id), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReconcileRebateAsync(Guid id, ProjectCompanyRequest request, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        await dispatcher.SendAsync(new ReconcileRebateAgreementCommand(request.CompanyId, id), cancellationToken);
        return Results.NoContent();
    }

    public sealed record CreateProjectRequest(Guid CompanyId, string Code, string Name, string Currency);
    public sealed record ProjectCompanyRequest(Guid CompanyId);
    public sealed record AllocateProjectCostRequest(Guid CompanyId, string SourceReference, ProjectCostKind Kind,
        decimal Amount, string Currency);
    public sealed record AllocateProjectLabourCostRequest(Guid CompanyId, Guid EmployeeId, DateOnly From, DateOnly To);
    public sealed record CreateRebateRequest(Guid CompanyId, string Number, decimal Rate, decimal ThresholdAmount, string Currency);
    public sealed record CreateContractRequest(Guid CompanyId, string Number, decimal OriginalValue, string Currency);
    public sealed record CalculateRebateRequest(Guid CompanyId, decimal EligibleAmount, string Currency);
}
