#pragma warning disable CS1591
using System.Globalization;
using System.Text;
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
        service.MapGet("/tickets", ListTicketsAsync).RequirePermission(ServicePermissions.View).Produces<IReadOnlyList<ServiceTicketResult>>();
        service.MapGet("/tickets/{id:guid}/sla", async (Guid id, Guid companyId, string slaName, DateTimeOffset asOfUtc, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new GetServiceSlaDeadlinesQuery(companyId, id, slaName, asOfUtc), ct))).RequirePermission(ServicePermissions.View);
        service.MapPost("/tickets/{id:guid}/close", CloseTicketAsync).RequirePermission(ServicePermissions.Manage).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/tickets/{id:guid}/resume", ResumeTicketAsync).RequirePermission(ServicePermissions.Manage).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/warranties", SubmitWarrantyAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/warranties/{id:guid}/approve", ApproveWarrantyAsync).RequirePermission(ServicePermissions.ApproveWarranty).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/repairs", OpenRepairAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/repairs/{id:guid}/complete", CompleteRepairAsync).RequirePermission(ServicePermissions.Manage).Produces(StatusCodes.Status204NoContent);
        service.MapPost("/parts", IssuePartAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapPost("/slas", CreateSlaAsync).RequirePermission(ServicePermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        service.MapGet("/custody", ListCustodyAsync).RequirePermission(ServicePermissions.View).Produces<IReadOnlyList<ServiceCustodyResult>>();
        service.MapGet("/custody/export.csv", ExportCustodyAsync).RequirePermission(ServicePermissions.Export)
            .Produces<string>(StatusCodes.Status200OK, "text/csv");
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

    private static async Task<IResult> ResumeTicketAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ResumeServiceTicketCommand(id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListTicketsAsync(Guid companyId, Guid? customerId, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(companyId);
        IReadOnlyList<ServiceTicketResult> result = await dispatcher.QueryAsync(
            new ListServiceTicketsQuery(companyId, customerId), cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListCustodyAsync(Guid companyId, Guid? customerId, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(companyId);
        IReadOnlyList<ServiceCustodyResult> result = await dispatcher.QueryAsync(
            new ListServiceCustodyQuery(companyId, customerId), cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportCustodyAsync(Guid companyId, Guid? customerId, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(companyId);
        IReadOnlyList<ServiceCustodyResult> result = await dispatcher.QueryAsync(
            new ListServiceCustodyQuery(companyId, customerId), cancellationToken).ConfigureAwait(false);
        static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        StringBuilder csv = new("id,company_id,ticket_id,customer_id,event_type,item_reference,occurred_at_utc\n");
        foreach (ServiceCustodyResult row in result)
        {
            csv.Append(row.Id.ToString("D")).Append(',')
                .Append(row.CompanyId.ToString("D")).Append(',')
                .Append(row.TicketId.ToString("D")).Append(',')
                .Append(row.CustomerId.ToString("D")).Append(',')
                .Append(Escape(row.EventType)).Append(',')
                .Append(Escape(row.ItemReference)).Append(',')
                .Append(row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        }
        return Results.Text(csv.ToString(), "text/csv; charset=utf-8");
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

    private static async Task<IResult> IssuePartAsync(IssuePartRequest request, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new IssueServicePartCommand(request.OperationId, request.CompanyId,
            request.RepairJobId, request.LocationId, request.ItemId, request.ItemVariantId, request.Quantity,
            request.UnitOfMeasure), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/service/parts/{id:D}", id);
    }

    private static async Task<IResult> CreateSlaAsync(CreateSlaRequest request, ICompanyContext company,
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        company.SetCompany(request.CompanyId);
        Guid id = await dispatcher.SendAsync(new CreateServiceSlaCommand(request.CompanyId, request.Name,
            request.ResponseHours, request.ResolutionHours), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/service/slas/{id:D}", id);
    }

    public sealed record OpenTicketRequest(Guid OperationId, Guid CompanyId, Guid CustomerId, string Subject);
    public sealed record SubmitWarrantyRequest(Guid CompanyId, Guid TicketId, Guid CustomerId, string SaleReference, DateOnly SaleDate, string SerialNumber);
    public sealed record ApproveWarrantyRequest(string SoldSerialNumber);
    public sealed record OpenRepairRequest(Guid CompanyId, Guid TicketId, string ItemReference);
    public sealed record IssuePartRequest(Guid OperationId, Guid CompanyId, Guid RepairJobId, Guid LocationId,
        Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure);
    public sealed record CreateSlaRequest(Guid CompanyId, string Name, decimal ResponseHours, decimal ResolutionHours);
}
