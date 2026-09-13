#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Service;

/// <summary>Stage 23 service ticket, warranty, repair and custody endpoints.</summary>
public static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapVumaServiceManagement(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder service = endpoints.MapVumaApi().MapGroup("/service")
            .WithTags("Service").RequireModule("service");
        service.MapPost("/tickets", OpenTicketAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/tickets/{id:guid}/close", CloseTicketAsync).RequirePermission(ServicePermissions.Manage).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/warranties", SubmitWarrantyAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/warranties/{id:guid}/approve", ApproveWarrantyAsync).RequirePermission(ServicePermissions.ApproveWarranty).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/repairs", OpenRepairAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/repairs/{id:guid}/complete", CompleteRepairAsync).RequirePermission(ServicePermissions.Manage).Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<IResult> OpenTicketAsync(OpenTicketRequest request, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new OpenServiceTicketCommand(request.OperationId, request.CompanyId,
            request.CustomerId, request.Subject), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/service/tickets/{id:D}", id);
    }

    private static async Task<IResult> SubmitWarrantyAsync(SubmitWarrantyRequest request, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new SubmitWarrantyClaimCommand(request.CompanyId, request.TicketId,
            request.CustomerId, request.SaleReference, request.SaleDate, request.SerialNumber), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/service/warranties/{id:D}", id);
    }

    private static async Task<IResult> CloseTicketAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CloseServiceTicketCommand(id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ApproveWarrantyAsync(Guid id, ApproveWarrantyRequest request,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ApproveWarrantyClaimCommand(id, request.SoldSerialNumber), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> OpenRepairAsync(OpenRepairRequest request, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new OpenRepairJobCommand(request.CompanyId, request.TicketId,
            request.ItemReference), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/service/repairs/{id:D}", id);
    }

    private static async Task<IResult> CompleteRepairAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CompleteRepairCommand(id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    public sealed record OpenTicketRequest(Guid OperationId, Guid CompanyId, Guid CustomerId, string Subject);
    public sealed record SubmitWarrantyRequest(Guid CompanyId, Guid TicketId, Guid CustomerId, string SaleReference, DateOnly SaleDate, string SerialNumber);
    public sealed record ApproveWarrantyRequest(string SoldSerialNumber);
    public sealed record OpenRepairRequest(Guid CompanyId, Guid TicketId, string ItemReference);
}
