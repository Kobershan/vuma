using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Application.Inventory.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Inventory;

/// <summary>
/// The <c>inventory</c> module's endpoints: locations, stock movements, balances, the ledger,
/// transfers and stocktakes.
/// </summary>
/// <remarks>
/// <para>
/// R3: nothing exists in a UI before it exists here. The five ways stock moves are five separate
/// <c>POST</c>s under <c>/inventory/stock</c> rather than one "post a movement" endpoint taking a
/// movement type — each carries genuinely different inputs (a receipt needs a cost, an adjustment
/// needs a reason code, a transfer needs two locations) and each is a different permission.
/// </para>
/// <para>
/// There is no endpoint that edits or deletes a ledger entry, and there never will be: the table is
/// append-only (ADR-005) and a correction is a new compensating movement. That is why the ledger is
/// exposed as a read and five append operations, with no <c>PUT</c> or <c>DELETE</c> anywhere.
/// </para>
/// </remarks>
public static class InventoryEndpoints
{
    /// <summary>Maps the inventory endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaInventory(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder locations = api.MapGroup("/inventory/locations").WithTags("Inventory");

        locations.MapGet("/", ListStockLocationsAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<IReadOnlyList<StockLocationResponse>>()
            .WithSummary("Every stock location in the tenant.")
            .WithDescription("Not paginated — a tenant's locations are configuration, not a catalogue.");

        locations.MapPost("/", CreateStockLocationAsync)
            .RequirePermission(InventoryPermissions.LocationManage)
            .Produces<StockLocationIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a warehouse, sales floor or other stock-holding location.");

        locations.MapPost("/{locationId:guid}/deactivate", DeactivateStockLocationAsync)
            .RequirePermission(InventoryPermissions.LocationManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a location from receiving or issuing stock. History is retained.");

        locations.MapGet("/{locationId:guid}/balances", ListStockBalancesAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<IReadOnlyList<StockBalanceResponse>>()
            .WithSummary("Every stock balance held at one location.");

        locations.MapGet("/{locationId:guid}/ledger", ListStockLedgerEntriesAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<PageResponse<StockLedgerEntryResponse>>()
            .WithSummary("A location's stock movements, newest first, a keyset page at a time.")
            .WithDescription(
                "Optionally narrowed to one item or variant. See docs/API_STANDARDS.md §8 for the "
                + "cursor contract.");

        locations.MapGet("/{locationId:guid}/balance", GetStockBalanceAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<StockBalanceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One stock-keeping unit's balance at one location.")
            .WithDescription(
                "404 when nothing has ever moved for this pairing — the balance row is opened by the "
                + "first receipt, so its absence is genuinely 'not found' rather than a zero.");

        RouteGroupBuilder stock = api.MapGroup("/inventory/stock").WithTags("Inventory");

        stock.MapPost("/receive", ReceiveStockAsync)
            .RequirePermission(InventoryPermissions.StockReceive)
            .Produces<StockLedgerEntryIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Receives stock, blending its cost into the location's weighted average.");

        stock.MapPost("/issue", RecordSaleIssueAsync)
            .RequirePermission(InventoryPermissions.StockIssue)
            .Produces<StockLedgerEntryIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Issues stock for a sale, relieved at the current weighted-average cost.")
            .WithDescription("Refused with 422 if insufficient stock is on hand. Stage 09 (POS) is this endpoint's real caller.");

        stock.MapPost("/adjust", AdjustStockAsync)
            .RequirePermission(InventoryPermissions.StockAdjust)
            .Produces<StockLedgerEntryIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts a documented adjustment — damage, loss, found stock or a correction.");

        stock.MapPost("/transfer", TransferStockAsync)
            .RequirePermission(InventoryPermissions.StockTransfer)
            .Produces<StockTransferIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Moves stock between two locations as two correlated ledger entries.");

        RouteGroupBuilder transfers = api.MapGroup("/inventory/transfers").WithTags("Inventory");

        transfers.MapGet("/{transferId:guid}", GetStockTransferAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<StockTransferResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one transfer and the two ledger entries it correlates.");

        RouteGroupBuilder stocktakes = api.MapGroup("/inventory/stocktakes").WithTags("Inventory");

        stocktakes.MapPost("/", OpenStocktakeAsync)
            .RequirePermission(InventoryPermissions.StocktakeManage)
            .Produces<StocktakeSessionIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Opens a physical count session at a location.");

        stocktakes.MapGet("/{sessionId:guid}", GetStocktakeSessionAsync)
            .RequirePermission(InventoryPermissions.StockView)
            .Produces<StocktakeSessionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a session and every line counted against it so far.");

        stocktakes.MapPost("/{sessionId:guid}/counts", RecordStocktakeCountAsync)
            .RequirePermission(InventoryPermissions.StocktakeManage)
            .Produces<StocktakeLineIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a count, or re-counts a line already recorded this session.")
            .WithDescription(
                "The system quantity is snapshotted at the moment of counting, not when the session is "
                + "finalized — so the variance is not blamed for sales made while the count was running.");

        stocktakes.MapPost("/{sessionId:guid}/finalize", FinalizeStocktakeAsync)
            .RequirePermission(InventoryPermissions.StocktakeFinalize)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts every line's variance to the ledger and closes the session.")
            .WithDescription("Irreversible. A correction afterwards is a new adjustment, not an edit to the count.");

        return endpoints;
    }

    private static async Task<IResult> ListStockLocationsAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<StockLocationResult> locations = await dispatcher
            .QueryAsync(new ListStockLocationsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<StockLocationResponse>>([.. locations.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateStockLocationAsync(
        CreateStockLocationRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        StockLocationType type = ParseEnum<StockLocationType>(
            request.Type, nameof(request.Type), nameof(CreateStockLocationCommand));

        Guid id = await dispatcher
            .SendAsync(new CreateStockLocationCommand(request.Code, request.Name, type, request.StoreId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/locations/{id}", new StockLocationIdResponse(id));
    }

    private static async Task<IResult> DeactivateStockLocationAsync(
        Guid locationId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DeactivateStockLocationCommand(locationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReceiveStockAsync(
        ReceiveStockRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new ReceiveStockCommand(
                    request.LocationId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    new Money(request.UnitCost, request.Currency),
                    request.Note),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/ledger/{id}", new StockLedgerEntryIdResponse(id));
    }

    private static async Task<IResult> RecordSaleIssueAsync(
        RecordSaleIssueRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordSaleIssueCommand(
                    request.LocationId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    request.SaleReferenceId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/ledger/{id}", new StockLedgerEntryIdResponse(id));
    }

    private static async Task<IResult> AdjustStockAsync(
        AdjustStockRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        AdjustmentReasonCode reasonCode = ParseEnum<AdjustmentReasonCode>(
            request.ReasonCode, nameof(request.ReasonCode), nameof(AdjustStockCommand));

        // A cost with no currency beside it is exactly what Money exists to prevent (§7 rule 4), so
        // refuse the pair rather than defaulting a currency nobody stated.
        if (request.UnitCost is not null && string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ValidationFailedException(
                nameof(AdjustStockCommand),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(request.Currency)] = ["A currency is required when a unit cost is supplied."],
                });
        }

        Money? unitCost = request.UnitCost is { } cost ? new Money(cost, request.Currency!) : null;

        Guid id = await dispatcher
            .SendAsync(
                new AdjustStockCommand(
                    request.LocationId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Delta, request.UnitOfMeasure),
                    reasonCode,
                    unitCost,
                    request.Note),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/ledger/{id}", new StockLedgerEntryIdResponse(id));
    }

    private static async Task<IResult> TransferStockAsync(
        TransferStockRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new TransferStockCommand(
                    request.SourceLocationId,
                    request.DestinationLocationId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    request.Note),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/transfers/{id}", new StockTransferIdResponse(id));
    }

    private static async Task<IResult> GetStockTransferAsync(
        Guid transferId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        StockTransferResult transfer = await dispatcher
            .QueryAsync(new GetStockTransferQuery(transferId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new StockTransferResponse(
            transfer.Id,
            transfer.SourceLocationId,
            transfer.DestinationLocationId,
            transfer.ItemId,
            transfer.ItemVariantId,
            transfer.Quantity.Value,
            transfer.Quantity.UnitOfMeasure,
            transfer.UnitCost.Amount,
            transfer.Value.Amount,
            transfer.Value.Currency,
            transfer.OutEntryId,
            transfer.InEntryId,
            transfer.Note));
    }

    private static async Task<IResult> GetStockBalanceAsync(
        Guid locationId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        Guid? itemId = null,
        Guid? itemVariantId = null)
    {
        StockBalanceResult balance = await dispatcher
            .QueryAsync(new GetStockBalanceQuery(locationId, itemId, itemVariantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(balance));
    }

    private static async Task<IResult> ListStockBalancesAsync(
        Guid locationId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StockBalanceResult> balances = await dispatcher
            .QueryAsync(new ListStockBalancesQuery(locationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<StockBalanceResponse>>([.. balances.Select(ToResponse)]);
    }

    private static async Task<IResult> ListStockLedgerEntriesAsync(
        Guid locationId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        Guid? itemId = null,
        Guid? itemVariantId = null,
        int? limit = null,
        string? after = null)
    {
        PageResult<StockLedgerEntryResult> page = await dispatcher
            .QueryAsync(new ListStockLedgerEntriesQuery(locationId, itemId, itemVariantId, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<StockLedgerEntryResponse>(
            [.. page.Items.Select(ToResponse)],
            page.NextCursor,
            page.HasMore));
    }

    private static async Task<IResult> OpenStocktakeAsync(
        OpenStocktakeRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new OpenStocktakeCommand(request.LocationId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/stocktakes/{id}", new StocktakeSessionIdResponse(id));
    }

    private static async Task<IResult> RecordStocktakeCountAsync(
        Guid sessionId,
        RecordStocktakeCountRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordStocktakeCountCommand(
                    sessionId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.CountedQuantity, request.UnitOfMeasure)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/inventory/stocktakes/{sessionId}/counts/{id}", new StocktakeLineIdResponse(id));
    }

    private static async Task<IResult> FinalizeStocktakeAsync(
        Guid sessionId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new FinalizeStocktakeCommand(sessionId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetStocktakeSessionAsync(
        Guid sessionId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        StocktakeSessionResult session = await dispatcher
            .QueryAsync(new GetStocktakeSessionQuery(sessionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new StocktakeSessionResponse(
            session.Id,
            session.LocationId,
            session.Status.ToString(),
            session.FinalizedAt,
            [.. session.Lines.Select(line => new StocktakeLineResponse(
                line.Id,
                line.ItemId,
                line.ItemVariantId,
                line.SystemQuantity.Value,
                line.CountedQuantity.Value,
                line.Variance.Value,
                line.CountedQuantity.UnitOfMeasure))]));
    }

    private static StockLocationResponse ToResponse(StockLocationResult location) => new(
        location.Id,
        location.Code,
        location.Name,
        location.Type.ToString(),
        location.StoreId,
        location.IsActive);

    private static StockBalanceResponse ToResponse(StockBalanceResult balance) => new(
        balance.LocationId,
        balance.ItemId,
        balance.ItemVariantId,
        balance.QuantityOnHand.Value,
        balance.QuantityOnHand.UnitOfMeasure,
        balance.AverageCost.Amount,
        balance.TotalValue.Amount,
        balance.AverageCost.Currency);

    private static StockLedgerEntryResponse ToResponse(StockLedgerEntryResult entry) => new(
        entry.Id,
        entry.LocationId,
        entry.ItemId,
        entry.ItemVariantId,
        entry.MovementType.ToString(),
        entry.Quantity.Value,
        entry.Quantity.UnitOfMeasure,
        entry.UnitCost.Amount,
        entry.Value.Amount,
        entry.UnitCost.Currency,
        entry.ReferenceType.ToString(),
        entry.ReferenceId,
        entry.ReasonCode?.ToString(),
        entry.Note,
        entry.CreatedAt);

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
