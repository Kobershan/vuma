using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Application.Warehouse.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Warehouse;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Warehouse;

/// <summary>
/// The <c>warehouse</c> module's endpoints: zones, bins, bin stock, putaway, pick/pack/ship and cycle
/// counts.
/// </summary>
/// <remarks>
/// R3: "handheld" means this API surface — a scanner-driven client scans a bin barcode, confirms a
/// putaway or a pick, records a count — not a hardware integration. Every deliverable in the stage
/// document is reachable here; there is no warehouse screen.
/// </remarks>
public static class WarehouseEndpoints
{
    /// <summary>Maps the warehouse endpoints under the current API version.</summary>
    public static IEndpointRouteBuilder MapVumaWarehouse(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder zones = api.MapGroup("/warehouse/zones").WithTags("Warehouse").RequireModule("warehouse");

        zones.MapPost("/", CreateZoneAsync)
            .RequirePermission(WarehousePermissions.LayoutManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a zone at a Stage 08 location.");

        zones.MapGet("/", ListZonesAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<IReadOnlyList<ZoneResponse>>()
            .WithSummary("Every zone at a location.");

        zones.MapPost("/{zoneId:guid}/deactivate", DeactivateZoneAsync)
            .RequirePermission(WarehousePermissions.LayoutManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a zone. History is retained.");

        zones.MapGet("/{zoneId:guid}/bins", ListBinsForZoneAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<IReadOnlyList<BinResponse>>()
            .WithSummary("Every bin in a zone.");

        RouteGroupBuilder bins = api.MapGroup("/warehouse/bins").WithTags("Warehouse").RequireModule("warehouse");

        bins.MapPost("/", CreateBinAsync)
            .RequirePermission(WarehousePermissions.LayoutManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a bin within a zone.");

        bins.MapPost("/{binId:guid}/deactivate", DeactivateBinAsync)
            .RequirePermission(WarehousePermissions.LayoutManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a bin from receiving or issuing stock.");

        bins.MapGet("/{binId:guid}/stock", ListBinStockForBinAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<IReadOnlyList<BinStockResponse>>()
            .WithSummary("Every balance held in one bin — the scan-a-bin lookup a handheld drives.");

        bins.MapGet("/{binId:guid}/stock/one", GetBinStockAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<BinStockResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One stock-keeping unit's balance in one bin.");

        bins.MapPost("/move", MoveBinStockAsync)
            .RequirePermission(WarehousePermissions.LayoutManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Moves stock directly from one bin to another within the same location.");

        RouteGroupBuilder locations = api.MapGroup("/warehouse/locations").WithTags("Warehouse").RequireModule("warehouse");

        locations.MapGet("/{locationId:guid}/bins", ListActiveBinsForLocationAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<IReadOnlyList<BinResponse>>()
            .WithSummary("Every active bin at a location.");

        RouteGroupBuilder putaway = api.MapGroup("/warehouse/putaway").WithTags("Warehouse").RequireModule("warehouse");

        putaway.MapPost("/", OpenPutawayTaskAsync)
            .RequirePermission(WarehousePermissions.PutawayManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a putaway task for stock that has arrived unbinned.")
            .WithDescription("Proposes a suggested bin. Does not move any stock — see the confirm endpoint.");

        putaway.MapGet("/{putawayTaskId:guid}", GetPutawayTaskAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<PutawayTaskResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one putaway task.");

        locations.MapGet("/{locationId:guid}/putaway/pending", ListPendingPutawayTasksAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<IReadOnlyList<PutawayTaskResponse>>()
            .WithSummary("Every putaway task still pending at a location.");

        putaway.MapPost("/{putawayTaskId:guid}/confirm", ConfirmPutawayAsync)
            .RequirePermission(WarehousePermissions.PutawayConfirm)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Confirms that some or all of a task's remaining quantity was shelved into a bin.")
            .WithDescription("The scan-a-bin, confirm-a-quantity flow a handheld drives.");

        putaway.MapPost("/{putawayTaskId:guid}/cancel", CancelPutawayTaskAsync)
            .RequirePermission(WarehousePermissions.PutawayManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a putaway task's remaining quantity.");

        RouteGroupBuilder waves = api.MapGroup("/warehouse/pick-waves").WithTags("Warehouse").RequireModule("warehouse");

        waves.MapPost("/", OpenPickWaveAsync)
            .RequirePermission(WarehousePermissions.PickManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Opens a new, empty pick wave at a location.");

        waves.MapGet("/{pickWaveId:guid}", GetPickWaveAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<PickWaveResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a wave and every line on it.");

        waves.MapPost("/{pickWaveId:guid}/tasks", AddPickTaskAsync)
            .RequirePermission(WarehousePermissions.PickManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds a demand line to an open wave.")
            .WithDescription(
                "Order Management (Stage 14) does not exist yet, so demand is supplied directly by the "
                + "caller; OutboundReference is free text.");

        waves.MapPost("/{pickWaveId:guid}/release", ReleasePickWaveAsync)
            .RequirePermission(WarehousePermissions.PickManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Allocates every pending line to bin(s) and releases the wave for picking.");

        waves.MapPost("/{pickWaveId:guid}/cancel", CancelPickWaveAsync)
            .RequirePermission(WarehousePermissions.PickManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a wave before it ships.");

        waves.MapPost("/{pickWaveId:guid}/pack", PackWaveAsync)
            .RequirePermission(WarehousePermissions.PackConfirm)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a picked wave's packing.");

        waves.MapGet("/{pickWaveId:guid}/pack", GetPackTaskAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<PackTaskResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a wave's pack record.");

        waves.MapPost("/{pickWaveId:guid}/ship", ShipWaveAsync)
            .RequirePermission(WarehousePermissions.ShipConfirm)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Confirms a packed wave's shipment — the point stock leaves the location.")
            .WithDescription("Refused with 422 if nothing on the wave was picked.");

        waves.MapGet("/{pickWaveId:guid}/shipment", GetShipmentConfirmationAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<ShipmentConfirmationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a wave's shipment confirmation.");

        RouteGroupBuilder pickTasks = api.MapGroup("/warehouse/pick-tasks").WithTags("Warehouse").RequireModule("warehouse");

        pickTasks.MapPost("/{pickTaskId:guid}/confirm", ConfirmPickAsync)
            .RequirePermission(WarehousePermissions.PickConfirm)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Confirms a pick against its allocation.")
            .WithDescription("A quantity below what was allocated is a short pick — recorded, not refused.");

        pickTasks.MapPost("/{pickTaskId:guid}/cancel", CancelPickTaskAsync)
            .RequirePermission(WarehousePermissions.PickManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a pick task before it is picked.");

        RouteGroupBuilder cycleCounts = api.MapGroup("/warehouse/cycle-counts").WithTags("Warehouse").RequireModule("warehouse");

        cycleCounts.MapPost("/", OpenCycleCountAsync)
            .RequirePermission(WarehousePermissions.CycleCountManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Opens a cycle count of one or more bins.");

        cycleCounts.MapGet("/{cycleCountId:guid}", GetCycleCountAsync)
            .RequirePermission(WarehousePermissions.View)
            .Produces<CycleCountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a count and every line recorded so far.");

        cycleCounts.MapPost("/{cycleCountId:guid}/counts", RecordCycleCountAsync)
            .RequirePermission(WarehousePermissions.CycleCountManage)
            .Produces<WarehouseIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a count, or re-counts a bin/stock-keeping-unit pair already recorded.")
            .WithDescription(
                "The system quantity is snapshotted at the moment of counting, not when the count is "
                + "finalized.");

        cycleCounts.MapPost("/{cycleCountId:guid}/finalize", FinalizeCycleCountAsync)
            .RequirePermission(WarehousePermissions.CycleCountManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts every line's variance and closes the count.")
            .WithDescription(
                "Irreversible. Corrects the location's own on-hand quantity through Stage 08's ledger, "
                + "not only the bin's balance — business rule 4.");

        return endpoints;
    }

    private static async Task<IResult> CreateZoneAsync(CreateZoneRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ZoneType type = ParseEnum<ZoneType>(request.Type, nameof(request.Type), nameof(CreateZoneCommand));

        Guid id = await dispatcher
            .SendAsync(new CreateZoneCommand(request.LocationId, request.Code, request.Name, type), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/zones/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> ListZonesAsync(Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ZoneResult> zones = await dispatcher
            .QueryAsync(new ListZonesQuery(locationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ZoneResponse>>([.. zones.Select(ToResponse)]);
    }

    private static async Task<IResult> DeactivateZoneAsync(Guid zoneId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DeactivateZoneCommand(zoneId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListBinsForZoneAsync(Guid zoneId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<BinResult> bins = await dispatcher
            .QueryAsync(new ListBinsForZoneQuery(zoneId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<BinResponse>>([.. bins.Select(ToResponse)]);
    }

    private static async Task<IResult> ListActiveBinsForLocationAsync(
        Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<BinResult> bins = await dispatcher
            .QueryAsync(new ListActiveBinsForLocationQuery(locationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<BinResponse>>([.. bins.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateBinAsync(CreateBinRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BinType type = ParseEnum<BinType>(request.Type, nameof(request.Type), nameof(CreateBinCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateBinCommand(request.ZoneId, request.Code, request.Name, type, request.CapacityQuantity, request.CapacityUnitOfMeasure),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/bins/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> DeactivateBinAsync(Guid binId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DeactivateBinCommand(binId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListBinStockForBinAsync(Guid binId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<BinStockResult> stock = await dispatcher
            .QueryAsync(new ListBinStockForBinQuery(binId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<BinStockResponse>>([.. stock.Select(ToResponse)]);
    }

    private static async Task<IResult> GetBinStockAsync(
        Guid binId, IDispatcher dispatcher, CancellationToken cancellationToken, Guid? itemId = null, Guid? itemVariantId = null)
    {
        BinStockResult stock = await dispatcher
            .QueryAsync(new GetBinStockQuery(binId, itemId, itemVariantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(stock));
    }

    private static async Task<IResult> MoveBinStockAsync(MoveBinStockRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new MoveBinStockCommand(
                    request.SourceBinId, request.DestinationBinId, request.ItemId, request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/bins/{request.DestinationBinId}/stock", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> OpenPutawayTaskAsync(
        OpenPutawayTaskRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PutawaySourceReferenceType sourceType = ParseEnum<PutawaySourceReferenceType>(
            request.SourceReferenceType, nameof(request.SourceReferenceType), nameof(OpenPutawayTaskCommand));

        Guid id = await dispatcher
            .SendAsync(
                new OpenPutawayTaskCommand(
                    request.LocationId, request.ItemId, request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure), sourceType, request.SourceReferenceId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/putaway/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> GetPutawayTaskAsync(Guid putawayTaskId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PutawayTaskResult task = await dispatcher
            .QueryAsync(new GetPutawayTaskQuery(putawayTaskId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(task));
    }

    private static async Task<IResult> ListPendingPutawayTasksAsync(
        Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<PutawayTaskResult> tasks = await dispatcher
            .QueryAsync(new ListPendingPutawayTasksQuery(locationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PutawayTaskResponse>>([.. tasks.Select(ToResponse)]);
    }

    private static async Task<IResult> ConfirmPutawayAsync(
        Guid putawayTaskId, ConfirmPutawayRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new ConfirmPutawayCommand(putawayTaskId, request.BinId, new Quantity(request.Quantity, request.UnitOfMeasure)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelPutawayTaskAsync(Guid putawayTaskId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelPutawayTaskCommand(putawayTaskId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> OpenPickWaveAsync(Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new OpenPickWaveCommand(locationId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/warehouse/pick-waves/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> GetPickWaveAsync(Guid pickWaveId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PickWaveResult wave = await dispatcher.QueryAsync(new GetPickWaveQuery(pickWaveId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(wave));
    }

    private static async Task<IResult> AddPickTaskAsync(
        Guid pickWaveId, AddPickTaskRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddPickTaskCommand(
                    pickWaveId, request.ItemId, request.ItemVariantId,
                    new Quantity(request.RequestedQuantity, request.UnitOfMeasure), request.OutboundReference),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/pick-tasks/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> ReleasePickWaveAsync(Guid pickWaveId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReleasePickWaveCommand(pickWaveId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelPickWaveAsync(Guid pickWaveId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelPickWaveCommand(pickWaveId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ConfirmPickAsync(
        Guid pickTaskId, ConfirmPickRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ConfirmPickCommand(pickTaskId, new Quantity(request.PickedQuantity, request.UnitOfMeasure)), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelPickTaskAsync(Guid pickTaskId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelPickTaskCommand(pickTaskId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PackWaveAsync(
        Guid pickWaveId, PackWaveRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new PackWaveCommand(pickWaveId, request.PackageCount, request.Note), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/pick-waves/{pickWaveId}/pack", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> GetPackTaskAsync(Guid pickWaveId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PackTaskResult task = await dispatcher.QueryAsync(new GetPackTaskQuery(pickWaveId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PackTaskResponse(task.Id, task.PickWaveId, task.PackageCount, task.Note, task.PackedAt));
    }

    private static async Task<IResult> ShipWaveAsync(
        Guid pickWaveId, ShipWaveRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new ShipWaveCommand(pickWaveId, request.Carrier, request.TrackingNumber), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/pick-waves/{pickWaveId}/shipment", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> GetShipmentConfirmationAsync(Guid pickWaveId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ShipmentConfirmationResult shipment = await dispatcher
            .QueryAsync(new GetShipmentConfirmationQuery(pickWaveId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ShipmentConfirmationResponse(
            shipment.Id, shipment.PickWaveId, shipment.Carrier, shipment.TrackingNumber, shipment.ShippedAt));
    }

    private static async Task<IResult> OpenCycleCountAsync(
        OpenCycleCountRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new OpenCycleCountCommand(request.LocationId, request.ZoneId, request.ScheduledAt), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/cycle-counts/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> GetCycleCountAsync(Guid cycleCountId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CycleCountResult count = await dispatcher.QueryAsync(new GetCycleCountQuery(cycleCountId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(count));
    }

    private static async Task<IResult> RecordCycleCountAsync(
        Guid cycleCountId, RecordCycleCountRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordCycleCountCommand(
                    cycleCountId, request.BinId, request.ItemId, request.ItemVariantId,
                    new Quantity(request.CountedQuantity, request.UnitOfMeasure)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/warehouse/cycle-counts/{cycleCountId}/counts/{id}", new WarehouseIdResponse(id));
    }

    private static async Task<IResult> FinalizeCycleCountAsync(Guid cycleCountId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new FinalizeCycleCountCommand(cycleCountId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static ZoneResponse ToResponse(ZoneResult zone) => new(zone.Id, zone.LocationId, zone.Code, zone.Name, zone.Type.ToString(), zone.IsActive);

    private static BinResponse ToResponse(BinResult bin) => new(
        bin.Id, bin.LocationId, bin.ZoneId, bin.Code, bin.Name, bin.Type.ToString(),
        bin.Capacity?.Value, bin.Capacity?.UnitOfMeasure, bin.IsActive);

    private static BinStockResponse ToResponse(BinStockResult stock)
        => new(stock.BinId, stock.ItemId, stock.ItemVariantId, stock.QuantityOnHand.Value, stock.QuantityOnHand.UnitOfMeasure);

    private static PutawayTaskResponse ToResponse(PutawayTaskResult task) => new(
        task.Id, task.LocationId, task.ItemId, task.ItemVariantId, task.Quantity.Value, task.Quantity.UnitOfMeasure,
        task.Status.ToString(), task.SuggestedBinId, task.ConfirmedBinId, task.ConfirmedQuantity.Value, task.Remaining.Value);

    private static PickWaveResponse ToResponse(PickWaveResult wave) => new(
        wave.Id, wave.LocationId, wave.Status.ToString(), wave.ReleasedAt, wave.PickedAt, wave.PackedAt, wave.ShippedAt,
        [.. wave.Tasks.Select(ToResponse)]);

    private static PickTaskResponse ToResponse(PickTaskResult task) => new(
        task.Id, task.ItemId, task.ItemVariantId, task.RequestedQuantity.Value, task.OutboundReference,
        task.AllocatedBinId, task.AllocatedQuantity?.Value, task.PickedQuantity?.Value, task.Status.ToString(),
        task.RequestedQuantity.UnitOfMeasure);

    private static CycleCountResponse ToResponse(CycleCountResult count) => new(
        count.Id, count.LocationId, count.ZoneId, count.Status.ToString(), count.ScheduledAt, count.FinalizedAt,
        [.. count.Lines.Select(ToResponse)]);

    private static CycleCountLineResponse ToResponse(CycleCountLineResult line) => new(
        line.Id, line.BinId, line.ItemId, line.ItemVariantId, line.SystemQuantity.Value, line.CountedQuantity.Value,
        line.Variance.Value, line.CountedQuantity.UnitOfMeasure);

    /// <summary>Parses an enum from a request field, refusing with a 400 rather than a 500 on a typo.</summary>
    private static TEnum ParseEnum<TEnum>(string value, string propertyName, string messageName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            messageName,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [propertyName] = [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}."],
            });
    }
}
