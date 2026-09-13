using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Reporting;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Reporting;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapVumaReporting(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapVumaApi().MapGroup("/reports")
            .WithTags("Reporting").RequireModule("reporting");
        group.MapGet("/{code}", GetDefinitionAsync)
            .RequirePermission(ReportingPermissions.View)
            .Produces<ReportDefinitionResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns a published tenant-scoped report definition and its AsAt timestamp.");
        group.MapPost("/exports", RequestExportAsync)
            .RequirePermission(ReportingPermissions.View)
            .Produces<Guid>(StatusCodes.Status202Accepted);
        group.MapGet("/exports/{id:guid}", GetExportAsync)
            .RequirePermission(ReportingPermissions.View)
            .Produces<ReportExportResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static async Task<IResult> RequestExportAsync(RequestExportRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new RequestReportExportCommand(request.CompanyId, request.OperationId, request.ReportCode), cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/api/v1/reports/exports/{id:D}", id);
    }

    private static async Task<IResult> GetExportAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ReportExportResult? result = await dispatcher.QueryAsync(new GetReportExportQuery(id), cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    public sealed record RequestExportRequest(Guid CompanyId, Guid OperationId, string ReportCode);

    private static async Task<IResult> GetDefinitionAsync(string code, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ReportDefinitionResult? result = await dispatcher.QueryAsync(new GetReportDefinitionQuery(code), cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
