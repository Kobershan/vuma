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
        RouteGroupBuilder production = endpoints.MapVumaApi().MapGroup("/manufacturing/production-orders").WithTags("Manufacturing").RequireModule("manufacturing");
        production.MapPost("/", CreateProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces<BillOfMaterialsIdResponse>(StatusCodes.Status201Created);
        production.MapPost("/{id:guid}/release", ReleaseProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        production.MapPost("/{id:guid}/issues", IssueProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        production.MapPost("/{id:guid}/receipts", ReceiveProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        production.MapPost("/{id:guid}/scrap", ScrapProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        production.MapPost("/{id:guid}/close", CloseProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent);
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

    private static async Task<IResult> CreateProductionAsync(CreateProductionOrderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateProductionOrderCommand(request.OperationId, request.CompanyId, request.FinishedItemId, request.Quantity, request.UnitOfMeasure, request.OrderNumber, request.BillOfMaterialsId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/manufacturing/production-orders/{id:D}", new BillOfMaterialsIdResponse(id));
    }

    private static async Task<IResult> ReleaseProductionAsync(Guid id, ReleaseProductionOrderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReleaseProductionOrderCommand(id, request.OperationId, request.BillOfMaterialsId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> IssueProductionAsync(Guid id, IssueProductionMaterialRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new IssueProductionMaterialCommand(id, request.LocationId, request.OperationId, request.ComponentItemId, request.ComponentVariantId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReceiveProductionAsync(Guid id, ReceiveProductionOutputRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReceiveProductionOutputCommand(id, request.LocationId, request.OperationId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ScrapProductionAsync(Guid id, RecordProductionScrapRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RecordProductionScrapCommand(id, request.OperationId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CloseProductionAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CloseProductionOrderCommand(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
