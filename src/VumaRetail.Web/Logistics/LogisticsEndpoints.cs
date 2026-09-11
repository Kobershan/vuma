using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Logistics;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Contracts.Logistics;
using VumaRetail.Domain.Logistics;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Logistics;

public static class LogisticsEndpoints
{
    public static IEndpointRouteBuilder MapVumaLogistics(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapVumaApi().MapGroup("/logistics").WithTags("Logistics").RequireModule("warehouse");
        api.MapGet("/carriers", async (ILogisticsRepository repo, CancellationToken ct) => Results.Ok((await repo.ListCarriersAsync(ct)).Select(x => new CarrierResponse(x.Id, x.Code, x.Name, x.Phone, x.IsActive))))
            .RequirePermission(WarehousePermissions.View);
        api.MapPost("/carriers", async (CreateCarrierRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        { var id = await dispatcher.SendAsync(new CreateCarrierCommand(request.Code, request.Name, request.Phone), ct); return Results.Created($"/api/v1/logistics/carriers/{id}", new { id, request.Code, request.Name, request.Phone, isActive = true }); })
            .RequirePermission(WarehousePermissions.LayoutManage);
        api.MapGet("/shipments", async (string? status, ILogisticsRepository repo, CancellationToken ct) => Results.Ok((await repo.ListShipmentsAsync(Enum.TryParse<LogisticsShipmentStatus>(status, true, out var parsed) ? parsed : null, ct)).Select(ToResponse)))
            .RequirePermission(WarehousePermissions.View);
        api.MapPost("/shipments", async (CreateShipmentRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        { var id = await dispatcher.SendAsync(new CreateShipmentCommand(request.StoreId, request.OrderId, request.ShipmentConfirmationId, request.CarrierId, request.Number, request.TrackingNumber, request.AddressLine1, request.AddressLine2, request.City, request.PostalCode, request.Country), ct); return Results.Created($"/api/v1/logistics/shipments/{id}", new { id, request.Number, request.OrderId, request.CarrierId, request.TrackingNumber, status = LogisticsShipmentStatus.Planned.ToString() }); })
            .RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapPost("/shipments/{shipmentId:guid}/dispatch", async (Guid shipmentId, IDispatcher dispatcher, CancellationToken ct) => { await dispatcher.SendAsync(new DispatchShipmentCommand(shipmentId), ct); return Results.NoContent(); }).RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapGet("/shipments/{shipmentId:guid}/pod", async (Guid shipmentId, ILogisticsRepository repo, CancellationToken ct) => { var x = await repo.FindPodAsync(shipmentId, ct); return x is null ? Results.NotFound() : Results.Ok(new PodResponse(x.ShipmentId, x.StopId, x.Outcome.ToString(), x.RecipientName, x.SignatureHash, x.PhotoBlobKey, x.Latitude, x.Longitude, x.DeliveredAt, x.Notes)); }).RequirePermission(WarehousePermissions.View);
        api.MapPost("/shipments/{shipmentId:guid}/pod", async (Guid shipmentId, RecordPodRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        { if (!Enum.TryParse<ProofOfDeliveryOutcome>(request.Outcome, true, out var outcome)) return Results.BadRequest(new { error = "Invalid POD outcome." }); var id = await dispatcher.SendAsync(new RecordPodCommand(shipmentId, request.StopId, outcome, request.RecipientName, request.SignatureHash, request.PhotoBlobKey, request.Latitude, request.Longitude, request.DeliveredAt, request.Notes), ct); return Results.Created($"/api/v1/logistics/shipments/{shipmentId}/pod", new { id, shipmentId, outcome = outcome.ToString() }); }).RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapPost("/runs", async (CreateDeliveryRunRequest request, IDispatcher dispatcher, CancellationToken ct) => { var id = await dispatcher.SendAsync(new CreateDeliveryRunCommand(request.StoreId, request.CarrierId, request.RunNumber, request.PlannedDate, request.DriverName, request.VehicleRegistration), ct); return Results.Created($"/api/v1/logistics/runs/{id}", new { id, request.RunNumber, request.PlannedDate, request.CarrierId, status = LogisticsRunStatus.Planned.ToString() }); }).RequirePermission(WarehousePermissions.PickManage);
        api.MapGet("/runs/{runId:guid}", async (Guid runId, ILogisticsRepository repo, CancellationToken ct) => { var run = await repo.FindRunAsync(runId, ct); if (run is null) return Results.NotFound(); var stops = await repo.ListStopsAsync(runId, ct); return Results.Ok(new DeliveryRunResponse(run.Id, run.RunNumber, run.PlannedDate, run.CarrierId, run.DriverName, run.VehicleRegistration, run.Status.ToString(), stops.Select(x => new DeliveryStopResponse(x.Id, x.ShipmentId, x.Sequence, x.AddressLine1, x.City, x.Status.ToString())).ToList())); }).RequirePermission(WarehousePermissions.View);
        api.MapPost("/runs/{runId:guid}/stops", async (Guid runId, AddDeliveryStopRequest request, IDispatcher dispatcher, CancellationToken ct) => { var id = await dispatcher.SendAsync(new AddDeliveryStopCommand(runId, request.ShipmentId, request.Sequence), ct); return Results.Created($"/api/v1/logistics/runs/{runId}", new { id, request.ShipmentId, request.Sequence }); }).RequirePermission(WarehousePermissions.PickManage);
        return endpoints;
    }
    private static ShipmentResponse ToResponse(Shipment x) => new(x.Id, x.Number, x.OrderId, x.CarrierId, x.TrackingNumber, x.Status.ToString(), x.AddressLine1, x.City, x.Country, x.DispatchedAt, x.DeliveredAt);
}
