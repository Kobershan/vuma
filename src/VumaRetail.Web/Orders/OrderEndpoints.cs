using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Orders.Permissions;
using VumaRetail.Application.Orders.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Orders;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Orders;

/// <summary>The <c>orders</c> module's endpoints: sales orders, allocation, backorders and order returns.</summary>
/// <remarks>
/// No order screen (R3, the stage document's own "what this stage does not own"). Every deliverable is
/// reachable here.
/// </remarks>
public static class OrderEndpoints
{
    /// <summary>Maps the orders endpoints under the current API version.</summary>
    public static IEndpointRouteBuilder MapVumaOrders(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder orders = api.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", CreateOrderAsync)
            .RequirePermission(OrdersPermissions.Create)
            .Produces<OrderIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises a new draft order.");

        orders.MapGet("/", ListOrdersAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<SalesOrderPageResponse>()
            .WithSummary("A page of orders, newest first, optionally narrowed by status/channel/partner/date range.");

        orders.MapGet("/backordered-lines", ListBackorderedLinesAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<IReadOnlyList<BackorderedLineResponse>>()
            .WithSummary("Every backordered line, across every order, oldest order first.");

        orders.MapPost("/reattempt-backordered-allocations", ReattemptBackorderedAllocationsAsync)
            .RequirePermission(OrdersPermissions.Allocate)
            .Produces<ReattemptBackorderedAllocationsResponse>()
            .WithSummary("Reattempts allocation for every backordered line, oldest order first.")
            .WithDescription("Nothing in this codebase triggers this automatically — call it after a receipt, or on a schedule.");

        orders.MapGet("/{salesOrderId:guid}", GetOrderAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<SalesOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads an order, with each line's allocation and fulfilment computed live from Stage 13.");

        orders.MapPost("/{salesOrderId:guid}/lines", AddOrderLineAsync)
            .RequirePermission(OrdersPermissions.Create)
            .Produces<OrderIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds a demand line while the order is still a draft.");

        orders.MapPost("/{salesOrderId:guid}/confirm", ConfirmOrderAsync)
            .RequirePermission(OrdersPermissions.Confirm)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Prices every line and accepts the order, then allocates whatever fits today.")
            .WithDescription("Never refuses the order as a whole — what does not fit becomes a backorder (business rule 2).");

        orders.MapPost("/{salesOrderId:guid}/lines/{salesOrderLineId:guid}/cancel", CancelOrderLineAsync)
            .RequirePermission(OrdersPermissions.Cancel)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Cancels one order line, cancelling any open pick task first.");

        orders.MapPost("/{salesOrderId:guid}/cancel", CancelOrderAsync)
            .RequirePermission(OrdersPermissions.Cancel)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Cancels a whole order — refused if anything on it has already shipped.");

        orders.MapPost("/{salesOrderId:guid}/refresh-fulfilment", RefreshOrderFulfilmentAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reconciles the order's lines and status from Stage 13's current state.");

        orders.MapPost("/{salesOrderId:guid}/complete", CompleteOrderAsync)
            .RequirePermission(OrdersPermissions.Complete)
            .Produces<CompleteOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Refreshes fulfilment and recognises revenue once every active line has shipped or been cancelled.")
            .WithDescription("Idempotent — a second call on an already-recognised order is a no-op (business rule 5).");

        orders.MapPost("/{salesOrderId:guid}/settlement", RecordOrderSettlementAsync)
            .RequirePermission(OrdersPermissions.Complete)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Records which mechanism paid for the order.");

        RouteGroupBuilder returns = api.MapGroup("/orders/returns").WithTags("Orders");

        returns.MapPost("/", CreateOrderReturnAsync)
            .RequirePermission(OrdersPermissions.ReturnCreate)
            .Produces<OrderIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises an open return against an order.");

        returns.MapGet("/", ListOrderReturnsAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<SalesOrderReturnPageResponse>()
            .WithSummary("A page of order returns raised within a period, newest first.");

        returns.MapGet("/{salesOrderReturnId:guid}", GetOrderReturnAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<SalesOrderReturnResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a return and every line on it.");

        returns.MapPost("/{salesOrderReturnId:guid}/lines", AddOrderReturnLineAsync)
            .RequirePermission(OrdersPermissions.ReturnCreate)
            .Produces<OrderIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts one of the order's fulfilled lines onto the return.")
            .WithDescription("Refused if the quantity exceeds what was fulfilled less what earlier returns already took (business rule 6).");

        returns.MapPost("/{salesOrderReturnId:guid}/complete", CompleteOrderReturnAsync)
            .RequirePermission(OrdersPermissions.ReturnComplete)
            .Produces<OrderReturnCompletionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes the return: receives stock back at the original shipment's unit cost and raises the financial event.");

        return endpoints;
    }

    private static async Task<IResult> CreateOrderAsync(CreateOrderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SalesChannel channel = ParseEnum<SalesChannel>(request.Channel, nameof(request.Channel), nameof(CreateOrderCommand));
        OrderFulfilmentType fulfilmentType = ParseEnum<OrderFulfilmentType>(
            request.FulfilmentType, nameof(request.FulfilmentType), nameof(CreateOrderCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateOrderCommand(
                    request.PartnerId, channel, fulfilmentType, request.FulfillingLocationId, request.DeliveryLine1,
                    request.DeliveryLine2, request.DeliveryCity, request.DeliveryRegion, request.DeliveryPostalCode,
                    request.DeliveryCountryCode, request.Currency, request.RequestedFulfilmentDate),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/orders/{id}", new OrderIdResponse(id));
    }

    private static async Task<IResult> GetOrderAsync(Guid salesOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SalesOrderResult order = await dispatcher.QueryAsync(new GetOrderQuery(salesOrderId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(order));
    }

    private static async Task<IResult> ListOrdersAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? status = null,
        string? channel = null,
        Guid? partnerId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        string? after = null)
    {
        SalesOrderStatus? parsedStatus = status is null
            ? null
            : ParseEnum<SalesOrderStatus>(status, nameof(status), nameof(ListOrdersQuery));
        SalesChannel? parsedChannel = channel is null
            ? null
            : ParseEnum<SalesChannel>(channel, nameof(channel), nameof(ListOrdersQuery));

        PageResult<SalesOrderSummaryResult> page = await dispatcher
            .QueryAsync(new ListOrdersQuery(parsedStatus, parsedChannel, partnerId, fromDate, toDate, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesOrderPageResponse(
            [.. page.Items.Select(ToSummaryResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> ListBackorderedLinesAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<BackorderedLineResult> lines = await dispatcher
            .QueryAsync(new ListBackorderedLinesQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<BackorderedLineResponse>>([.. lines.Select(ToResponse)]);
    }

    private static async Task<IResult> ReattemptBackorderedAllocationsAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ReattemptBackorderedAllocationsResult result = await dispatcher
            .SendAsync(new ReattemptBackorderedAllocationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ReattemptBackorderedAllocationsResponse(result.OrdersReallocated, result.LinesReallocated));
    }

    private static async Task<IResult> AddOrderLineAsync(
        Guid salesOrderId, AddOrderLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddOrderLineCommand(salesOrderId, request.ItemId, request.ItemVariantId, request.RequestedQuantity, request.UnitOfMeasure),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/orders/{salesOrderId}", new OrderIdResponse(id));
    }

    private static async Task<IResult> ConfirmOrderAsync(Guid salesOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ConfirmOrderCommand(salesOrderId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelOrderLineAsync(
        Guid salesOrderId, Guid salesOrderLineId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelOrderLineCommand(salesOrderId, salesOrderLineId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid salesOrderId, CancelOrderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelOrderCommand(salesOrderId, request.Reason), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RefreshOrderFulfilmentAsync(Guid salesOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RefreshOrderFulfilmentCommand(salesOrderId), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CompleteOrderAsync(Guid salesOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CompleteOrderResult result = await dispatcher
            .SendAsync(new CompleteOrderCommand(salesOrderId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new CompleteOrderResponse(
            result.SalesOrderId, result.Status.ToString(), result.RevenueRecognised,
            result.Net.Amount, result.Tax.Amount, result.Gross.Amount));
    }

    private static async Task<IResult> RecordOrderSettlementAsync(
        Guid salesOrderId, RecordOrderSettlementRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        OrderPaymentStatus status = ParseEnum<OrderPaymentStatus>(
            request.PaymentStatus, nameof(request.PaymentStatus), nameof(RecordOrderSettlementCommand));

        await dispatcher
            .SendAsync(
                new RecordOrderSettlementCommand(salesOrderId, status, request.SettlingSaleId, request.SettlingCustomerAccountId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateOrderReturnAsync(
        CreateOrderReturnRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new CreateOrderReturnCommand(request.SalesOrderId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/orders/returns/{id}", new OrderIdResponse(id));
    }

    private static async Task<IResult> GetOrderReturnAsync(Guid salesOrderReturnId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SalesOrderReturnResult orderReturn = await dispatcher
            .QueryAsync(new GetOrderReturnQuery(salesOrderReturnId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(orderReturn));
    }

    private static async Task<IResult> ListOrderReturnsAsync(
        DateTimeOffset from, DateTimeOffset to, IDispatcher dispatcher, CancellationToken cancellationToken, int? limit = null, string? after = null)
    {
        PageResult<SalesOrderReturnResult> page = await dispatcher
            .QueryAsync(new ListOrderReturnsQuery(from, to, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesOrderReturnPageResponse([.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> AddOrderReturnLineAsync(
        Guid salesOrderReturnId, AddOrderReturnLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new AddOrderReturnLineCommand(salesOrderReturnId, request.SalesOrderLineId, request.Quantity), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/orders/returns/{salesOrderReturnId}", new OrderIdResponse(id));
    }

    private static async Task<IResult> CompleteOrderReturnAsync(
        Guid salesOrderReturnId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        OrderReturnCompletionResult result = await dispatcher
            .SendAsync(new CompleteOrderReturnCommand(salesOrderReturnId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new OrderReturnCompletionResponse(
            result.SalesOrderReturnId, result.ReturnNumber, result.Net.Amount, result.Tax.Amount, result.Gross.Amount,
            result.Gross.Currency, result.StockReturnsRefused));
    }

    private static OrderAddressResponse? ToResponse(Address? address) => address is null
        ? null
        : new OrderAddressResponse(address.Line1, address.Line2, address.City, address.Region, address.PostalCode, address.CountryCode);

    private static SalesOrderLineResponse ToResponse(SalesOrderLineResult line) => new(
        line.Id, line.ItemId, line.ItemVariantId, line.RequestedQuantity.Value, line.RequestedQuantity.UnitOfMeasure,
        line.UnitPrice.Amount, line.DiscountAmount.Amount, line.TaxAmount.Amount, line.PriceListId, line.PromotionsSummary,
        line.BackorderedQuantity.Value, line.LineStatus.ToString(), line.AllocatedQuantity.Value, line.FulfilledQuantity.Value);

    private static SalesOrderResponse ToResponse(SalesOrderResult order) => new(
        order.Id, order.OrderNumber, order.PartnerId, order.Channel.ToString(), order.FulfilmentType.ToString(),
        order.FulfillingLocationId, ToResponse(order.DeliveryAddress), order.Status.ToString(), order.PaymentStatus.ToString(),
        order.SettlingSaleId, order.SettlingCustomerAccountId, order.Currency, order.OrderDate, order.RequestedFulfilmentDate,
        order.IsRevenueRecognised, order.Net.Amount, order.Tax.Amount, order.Gross.Amount, [.. order.Lines.Select(ToResponse)]);

    private static SalesOrderSummaryResponse ToSummaryResponse(SalesOrderSummaryResult order) => new(
        order.Id, order.OrderNumber, order.PartnerId, order.Channel.ToString(), order.FulfilmentType.ToString(), order.Status.ToString(),
        order.PaymentStatus.ToString(), order.Currency, order.OrderDate, order.Net.Amount, order.Tax.Amount, order.Gross.Amount);

    private static BackorderedLineResponse ToResponse(BackorderedLineResult line) => new(
        line.SalesOrderId, line.OrderNumber, line.OrderDate, line.SalesOrderLineId, line.ItemId, line.ItemVariantId,
        line.BackorderedQuantity.Value, line.BackorderedQuantity.UnitOfMeasure);

    private static SalesOrderReturnLineResponse ToResponse(SalesOrderReturnLineResult line) => new(
        line.Id, line.SalesOrderLineId, line.ItemId, line.ItemVariantId, line.Quantity.Value, line.FulfilledQuantity.Value,
        line.UnitPrice.Amount, line.Net.Amount, line.Tax.Amount, line.Gross.Amount, line.StockReturn.ToString(),
        line.StockLedgerEntryId, line.StockReturnNote);

    private static SalesOrderReturnResponse ToResponse(SalesOrderReturnResult orderReturn) => new(
        orderReturn.Id, orderReturn.SalesOrderId, orderReturn.ReturnNumber, orderReturn.Reason, orderReturn.AuthorisedByUserId,
        orderReturn.Status.ToString(), orderReturn.RefundStatus.ToString(), orderReturn.Net.Amount, orderReturn.Tax.Amount,
        orderReturn.Gross.Amount, orderReturn.Gross.Currency, orderReturn.RaisedAt, orderReturn.CompletedAt,
        [.. orderReturn.Lines.Select(ToResponse)]);

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
