using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Contracts.Manufacturing;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Manufacturing;

/// <summary>Stage 16 BOM definition endpoints.</summary>
public static class ManufacturingEndpoints
{
    /// <summary>Maps BOM creation, publication, and read endpoints.</summary>
    public static IEndpointRouteBuilder MapVumaManufacturing(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        RouteGroupBuilder api = endpoints.MapVumaApi().MapGroup("/manufacturing/boms").WithTags("Manufacturing").RequireModule("manufacturing");
        api.MapPost("/", CreateAsync).RequirePermission(ManufacturingPermissions.Manage).Produces<BillOfMaterialsIdResponse>(StatusCodes.Status201Created);
        api.MapGet("/{id:guid}", GetAsync).RequirePermission(ManufacturingPermissions.View).Produces<BillOfMaterialsResponse>();
        api.MapPost("/{id:guid}/publish", PublishAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateBillOfMaterialsRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateBillOfMaterialsCommand(
            request.CompanyId,
            request.FinishedItemId,
            request.FinishedVariantId,
            request.Version,
            request.Name,
            [.. request.Lines.Select(line => new BillOfMaterialsLineInput(line.ComponentItemId, line.ComponentVariantId, line.Quantity, line.UnitOfMeasure, line.ScrapPercent, line.AlternateGroup))]), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/manufacturing/boms/{id:D}", new BillOfMaterialsIdResponse(id));
    }

    private static async Task<IResult> GetAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var bom = await dispatcher.QueryAsync(new GetBillOfMaterialsQuery(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BillOfMaterialsResponse(
            bom.Id, bom.FinishedItemId, bom.FinishedVariantId, bom.Version, bom.Name, bom.Status.ToString(),
            [.. bom.Lines.Select(line => new BillOfMaterialsLineRequest(line.ComponentItemId, line.ComponentVariantId, line.Quantity.Value, line.Quantity.UnitOfMeasure, line.ScrapPercent, line.AlternateGroup))]));
    }

    private static async Task<IResult> PublishAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PublishBillOfMaterialsCommand(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
