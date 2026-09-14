using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Contracts.Manufacturing;
using VumaRetail.Domain.Manufacturing;
using VumaRetail.Infrastructure.Security.Identity;
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
        api.MapPost("/", CreateAsync).RequirePermission(ManufacturingPermissions.Manage).Produces<BillOfMaterialsIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        api.MapGet("/{id:guid}", GetAsync).RequirePermission(ManufacturingPermissions.View).Produces<BillOfMaterialsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        api.MapPost("/{id:guid}/publish", PublishAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        RouteGroupBuilder production = endpoints.MapVumaApi().MapGroup("/manufacturing/production-orders").WithTags("Manufacturing").RequireModule("manufacturing");
        production.MapPost("/", CreateProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces<ProductionOrderIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapGet("/{id:guid}", GetProductionAsync).RequirePermission(ManufacturingPermissions.View).Produces<ProductionOrderResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        production.MapGet("/{id:guid}/genealogy", GetProductionAsync).RequirePermission(ManufacturingPermissions.View).Produces<ProductionOrderResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        production.MapGet("/{id:guid}/capacity", GetProductionCapacityAsync).RequirePermission(ManufacturingPermissions.View).Produces<ProductionCapacityResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapPost("/{id:guid}/release", ReleaseProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapPost("/{id:guid}/issues", IssueProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapPost("/{id:guid}/receipts", ReceiveProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapPost("/{id:guid}/scrap", ScrapProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        production.MapPost("/{id:guid}/close", CloseProductionAsync).RequirePermission(ManufacturingPermissions.Manage).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateBillOfMaterialsRequest request, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        VumaCompanyClaims.RequireCompany(http.User, request.CompanyId);
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateBillOfMaterialsCommand(
            request.CompanyId,
            request.FinishedItemId,
            request.FinishedVariantId,
            request.Version,
            request.Name,
            [.. request.Lines.Select(line => new BillOfMaterialsLineInput(line.ComponentItemId, line.ComponentVariantId, line.Quantity, line.UnitOfMeasure, line.ScrapPercent, line.AlternateGroup))]), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/manufacturing/boms/{id:D}", new BillOfMaterialsIdResponse(id));
    }

    private static async Task<IResult> GetAsync(Guid id, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        var bom = await dispatcher.QueryAsync(new GetBillOfMaterialsQuery(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BillOfMaterialsResponse(
            bom.Id, bom.FinishedItemId, bom.FinishedVariantId, bom.Version, bom.Name, bom.Status.ToString(),
            [.. bom.Lines.Select(line => new BillOfMaterialsLineRequest(line.ComponentItemId, line.ComponentVariantId, line.Quantity.Value, line.Quantity.UnitOfMeasure, line.ScrapPercent, line.AlternateGroup))]));
    }

    private static async Task<IResult> PublishAsync(Guid id, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new PublishBillOfMaterialsCommand(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static void BindCompany(HttpContext http, ICompanyContext company, Guid? companyId)
    {
        if (companyId is { } requestedCompany)
        {
            VumaCompanyClaims.RequireCompany(http.User, requestedCompany);
            company.SetCompany(requestedCompany);
        }
        else if (company.CompanyId is { } activeCompany)
        {
            VumaCompanyClaims.RequireCompany(http.User, activeCompany);
        }
        else
        {
            throw ManufacturingRuleException.NotFound(Guid.Empty);
        }
    }

    private static async Task<IResult> CreateProductionAsync(CreateProductionOrderRequest request, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        VumaCompanyClaims.RequireCompany(http.User, request.CompanyId);
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateProductionOrderCommand(request.OperationId, request.CompanyId, request.FinishedItemId, request.Quantity, request.UnitOfMeasure, request.OrderNumber, request.BillOfMaterialsId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/manufacturing/production-orders/{id:D}", new ProductionOrderIdResponse(id));
    }

    private static async Task<IResult> ReleaseProductionAsync(Guid id, ReleaseProductionOrderRequest request, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new ReleaseProductionOrderCommand(id, request.OperationId, request.BillOfMaterialsId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetProductionAsync(Guid id, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        var order = await dispatcher.QueryAsync(new GetProductionOrderQuery(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new ProductionOrderResponse(
            order.Id, order.CompanyId, order.FinishedItemId, order.PlannedQuantity, order.UnitOfMeasure,
            order.OrderNumber, order.Status, order.BillOfMaterialsId,
            order.Routing.Select(step => new ProductionRoutingStepResponse(step.Sequence, step.OperationName, step.SetupMinutes, step.RunMinutes)).ToArray(),
            order.Materials.Select(material => new ProductionMaterialRequirementResponse(material.ComponentItemId, material.ComponentVariantId, material.Quantity, material.UnitOfMeasure, material.ScrapPercent, material.AlternateGroup)).ToArray(),
            order.Issues.Select(issue => new ProductionMaterialIssueResponse(issue.OperationId, issue.ComponentItemId, issue.ComponentVariantId, issue.Quantity, issue.UnitOfMeasure, issue.UnitCost, issue.Currency)).ToArray(),
            order.Receipts.Select(receipt => new ProductionOutputReceiptResponse(receipt.OperationId, receipt.Quantity, receipt.UnitOfMeasure, receipt.UnitCost, receipt.Currency)).ToArray(),
            order.Scrap.Select(scrap => new ProductionScrapResponse(scrap.OperationId, scrap.Quantity, scrap.UnitOfMeasure, scrap.UnitCost, scrap.Currency)).ToArray()));
    }

    private static async Task<IResult> GetProductionCapacityAsync(Guid id, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        var capacity = await dispatcher.QueryAsync(new GetProductionCapacityQuery(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new ProductionCapacityResponse(capacity.ProductionOrderId, capacity.PlannedQuantity, capacity.UnitOfMeasure, capacity.SetupMinutes, capacity.RunMinutes, capacity.TotalMinutes));
    }

    private static async Task<IResult> IssueProductionAsync(Guid id, IssueProductionMaterialRequest request, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new IssueProductionMaterialCommand(id, request.LocationId, request.OperationId, request.ComponentItemId, request.ComponentVariantId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReceiveProductionAsync(Guid id, ReceiveProductionOutputRequest request, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new ReceiveProductionOutputCommand(id, request.LocationId, request.OperationId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ScrapProductionAsync(Guid id, RecordProductionScrapRequest request, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new RecordProductionScrapCommand(id, request.OperationId, request.Quantity, request.UnitOfMeasure, request.UnitCost, request.Currency), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CloseProductionAsync(Guid id, Guid? companyId, HttpContext http, ICompanyContext company, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BindCompany(http, company, companyId);
        await dispatcher.SendAsync(new CloseProductionOrderCommand(id), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
