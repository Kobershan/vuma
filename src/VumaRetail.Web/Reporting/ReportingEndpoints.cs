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
        return endpoints;
    }

    private static async Task<IResult> GetDefinitionAsync(string code, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ReportDefinitionResult? result = await dispatcher.QueryAsync(new GetReportDefinitionQuery(code), cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
