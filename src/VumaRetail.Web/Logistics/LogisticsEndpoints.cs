using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Logistics;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Contracts.Logistics;
using VumaRetail.Domain.Logistics;
using VumaRetail.Infrastructure.Persistence;
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
        api.MapPost("/carriers", async (CreateCarrierRequest request, ILogisticsRepository repo, ITenantContext tenant, VumaRetailDbContext db, CancellationToken ct) =>
        { var carrier = Carrier.Create(tenant.TenantId, request.Code, request.Name, request.Phone); repo.Add(carrier); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/logistics/carriers/{carrier.Id}", new CarrierResponse(carrier.Id, carrier.Code, carrier.Name, carrier.Phone, carrier.IsActive)); })
            .RequirePermission(WarehousePermissions.LayoutManage);
        api.MapGet("/shipments", async (string? status, ILogisticsRepository repo, CancellationToken ct) => Results.Ok((await repo.ListShipmentsAsync(Enum.TryParse<LogisticsShipmentStatus>(status, true, out var parsed) ? parsed : null, ct)).Select(ToResponse)))
            .RequirePermission(WarehousePermissions.View);
        api.MapPost("/shipments", async (CreateShipmentRequest request, ILogisticsRepository repo, ITenantContext tenant, VumaRetailDbContext db, CancellationToken ct) =>
        { var shipment = Shipment.Create(tenant.TenantId, request.StoreId, request.Number, request.OrderId, request.ShipmentConfirmationId, request.CarrierId, request.TrackingNumber, request.AddressLine1, request.AddressLine2, request.City, request.PostalCode, request.Country); repo.Add(shipment); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/logistics/shipments/{shipment.Id}", ToResponse(shipment)); })
            .RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapPost("/shipments/{shipmentId:guid}/dispatch", async (Guid shipmentId, ILogisticsRepository repo, VumaRetailDbContext db, CancellationToken ct) => { var x = await repo.FindShipmentAsync(shipmentId, ct); if (x is null) return Results.NotFound(); x.Dispatch(DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); return Results.NoContent(); }).RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapGet("/shipments/{shipmentId:guid}/pod", async (Guid shipmentId, ILogisticsRepository repo, CancellationToken ct) => { var x = await repo.FindPodAsync(shipmentId, ct); return x is null ? Results.NotFound() : Results.Ok(new PodResponse(x.ShipmentId, x.StopId, x.Outcome.ToString(), x.RecipientName, x.SignatureHash, x.PhotoBlobKey, x.Latitude, x.Longitude, x.DeliveredAt, x.Notes)); }).RequirePermission(WarehousePermissions.View);
        api.MapPost("/shipments/{shipmentId:guid}/pod", async (Guid shipmentId, RecordPodRequest request, ILogisticsRepository repo, ITenantContext tenant, VumaRetailDbContext db, CancellationToken ct) =>
        { var shipment = await repo.FindShipmentAsync(shipmentId, ct); if (shipment is null) return Results.NotFound(); if (!Enum.TryParse<ProofOfDeliveryOutcome>(request.Outcome, true, out var outcome)) return Results.BadRequest(new { error = "Invalid POD outcome." }); if (await repo.FindPodAsync(shipmentId, ct) is not null) return Results.Conflict(); var pod = ProofOfDelivery.Record(tenant.TenantId, shipment.StoreId, shipmentId, request.StopId, outcome, request.RecipientName, request.SignatureHash, request.PhotoBlobKey, request.Latitude, request.Longitude, request.DeliveredAt, request.Notes); repo.Add(pod); shipment.MarkDelivered(request.DeliveredAt); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/logistics/shipments/{shipmentId}/pod", new PodResponse(pod.ShipmentId, pod.StopId, pod.Outcome.ToString(), pod.RecipientName, pod.SignatureHash, pod.PhotoBlobKey, pod.Latitude, pod.Longitude, pod.DeliveredAt, pod.Notes)); }).RequirePermission(WarehousePermissions.ShipConfirm);
        api.MapPost("/runs", async (CreateDeliveryRunRequest request, ILogisticsRepository repo, ITenantContext tenant, VumaRetailDbContext db, CancellationToken ct) => { var run = DeliveryRun.Create(tenant.TenantId, request.StoreId, request.CarrierId, request.RunNumber, request.PlannedDate, request.DriverName, request.VehicleRegistration); repo.Add(run); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/logistics/runs/{run.Id}", new DeliveryRunResponse(run.Id, run.RunNumber, run.PlannedDate, run.CarrierId, run.DriverName, run.VehicleRegistration, run.Status.ToString(), [])); }).RequirePermission(WarehousePermissions.PickManage);
        api.MapGet("/runs/{runId:guid}", async (Guid runId, ILogisticsRepository repo, CancellationToken ct) => { var run = await repo.FindRunAsync(runId, ct); if (run is null) return Results.NotFound(); var stops = await repo.ListStopsAsync(runId, ct); return Results.Ok(new DeliveryRunResponse(run.Id, run.RunNumber, run.PlannedDate, run.CarrierId, run.DriverName, run.VehicleRegistration, run.Status.ToString(), stops.Select(x => new DeliveryStopResponse(x.Id, x.ShipmentId, x.Sequence, x.AddressLine1, x.City, x.Status.ToString())).ToList())); }).RequirePermission(WarehousePermissions.View);
        api.MapPost("/runs/{runId:guid}/stops", async (Guid runId, AddDeliveryStopRequest request, ILogisticsRepository repo, ITenantContext tenant, VumaRetailDbContext db, CancellationToken ct) => { var run = await repo.FindRunAsync(runId, ct); var shipment = await repo.FindShipmentAsync(request.ShipmentId, ct); if (run is null || shipment is null) return Results.NotFound(); var stop = DeliveryStop.Create(tenant.TenantId, run.StoreId, runId, shipment.Id, request.Sequence, shipment.AddressLine1, shipment.City); repo.Add(stop); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/logistics/runs/{runId}", new DeliveryStopResponse(stop.Id, stop.ShipmentId, stop.Sequence, stop.AddressLine1, stop.City, stop.Status.ToString())); }).RequirePermission(WarehousePermissions.PickManage);
        return endpoints;
    }
    private static ShipmentResponse ToResponse(Shipment x) => new(x.Id, x.Number, x.OrderId, x.CarrierId, x.TrackingNumber, x.Status.ToString(), x.AddressLine1, x.City, x.Country, x.DispatchedAt, x.DeliveredAt);
}
