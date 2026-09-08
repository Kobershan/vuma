using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Contracts.TradingSessions;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.TradingSessions;

/// <summary>
/// The mixed-basket trading session: one till, one customer, one payment, one tax invoice per
/// company (Stage 09b). Every write goes through a command handler; no endpoint touches a
/// database.
/// </summary>
/// <remarks>
/// Reads ride <c>trading.basket.mixed</c>; writes that move another company's money ride the
/// high-risk keys. The module flag is <c>multicompany</c> (the tenant bought multi-company
/// operation) and every cross-company step additionally checks the <c>SharedTill</c> link.
/// </remarks>
public static class TradingSessionEndpoints
{
    /// <summary>Maps the trading-session endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTradingSessions(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder sessions = api.MapGroup("/trading-sessions").WithTags("Trading").RequireModule("trading");

        sessions.MapPost("/", OpenSessionAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces<TradingIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a trading session at a shared till.")
            .WithDescription(
                "Replaying an idempotency key returns the existing session rather than opening a "
                + "second one, so a till that lost the acknowledgement replays safely.");

        sessions.MapPost("/{sessionId:guid}/lines", AddLineAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces<TradingIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds a scanned line to its company's segment.")
            .WithDescription(
                "The barcode's company comes from the routing index, never the cashier. A sister "
                + "company's line without an active SharedTill link is refused here, at scan time.");

        sessions.MapPost("/{sessionId:guid}/lines/{lineId:guid}/void", VoidLineAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes a scanned line back off the session. The line stays on the record.");

        sessions.MapGet("/{sessionId:guid}", GetSessionAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces<TradingSessionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One session: segments, per-company tax, allocation, invoices when done.");

        sessions.MapPost("/{sessionId:guid}/tender", CaptureTenderAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Captures the one tender and fixes the proportional allocation.")
            .WithDescription("422 when the tender does not cover the basket gross.");

        sessions.MapPut("/{sessionId:guid}/tender/allocations", OverrideAllocationAsync)
            .RequirePermission(TradingSessionPermissions.BasketAllocateOverride)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Replaces the proportional split with the cashier's exact one.")
            .WithDescription("The override must sum to the tender exactly, to the cent.");

        sessions.MapPost("/{sessionId:guid}/complete", CompleteSessionAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces<IReadOnlyList<CompletedSegmentResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Completes the session: one sale, invoice and receipt per company.")
            .WithDescription(
                "The saga; idempotent on the session. A replayed completion returns the same "
                + "invoice numbers and creates nothing.");

        sessions.MapPost("/{sessionId:guid}/void", VoidSessionAsync)
            .RequirePermission(TradingSessionPermissions.BasketVoid)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons the session with a reason. Releases holds, posts nothing.");

        sessions.MapGet("/{sessionId:guid}/documents", GetDocumentsAsync)
            .RequirePermission(TradingSessionPermissions.BasketMixed)
            .Produces<BasketSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("The per-company invoice ids plus the non-fiscal basket summary.");

        sessions.MapPost("/{sessionId:guid}/returns", ReturnLinesAsync)
            .RequirePermission(TradingSessionPermissions.BasketVoid)
            .Produces<TradingIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Returns lines to their origin company (ADR-128).")
            .WithDescription(
                "One credit in the origin company against its own invoice. A cross-company "
                + "invoice is refused with both invoice numbers named.");

        return endpoints;
    }

    private static async Task<IResult> OpenSessionAsync(
        OpenTradingSessionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new OpenTradingSessionCommand(
                    request.PremisesId, request.TerminalId, request.CashierUserId,
                    request.SessionCompanyId, request.Currency, request.IdempotencyKey,
                    request.CustomerGroupPartnerId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/trading-sessions/{id}", new TradingIdResponse(id));
    }

    private static async Task<IResult> AddLineAsync(
        Guid sessionId, AddBasketLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddBasketLineCommand(
                    sessionId, request.Barcode, request.QuantityValue, request.QuantityUom,
                    request.UnitPriceAmount, request.Currency, request.DiscountAmount,
                    request.TaxCode, request.LineId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/trading-sessions/{sessionId}/lines/{id}", new TradingIdResponse(id));
    }

    private static async Task<IResult> VoidLineAsync(
        Guid sessionId, Guid lineId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new VoidBasketLineCommand(sessionId, lineId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSessionAsync(
        Guid sessionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TradingSessionView view = await dispatcher
            .QueryAsync(new GetTradingSessionQuery(sessionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(view));
    }

    private static async Task<IResult> CaptureTenderAsync(
        Guid sessionId, CaptureTenderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new CaptureTenderCommand(
                    sessionId, request.TenderType, request.Amount, request.Currency, request.Reference),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> OverrideAllocationAsync(
        Guid sessionId, OverrideAllocationRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new OverrideTenderAllocationCommand(sessionId, [.. request.Allocations
                    .Select(entry => new AllocationOverride(entry.CompanyId, entry.Amount, entry.Currency))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CompleteSessionAsync(
        Guid sessionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<CompletedSegmentResult> posted = await dispatcher
            .SendAsync(new CompleteTradingSessionCommand(sessionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<CompletedSegmentResponse>>([.. posted
            .Select(segment => new CompletedSegmentResponse(
                segment.CompanyId, segment.SaleId, segment.InvoiceId, segment.InvoiceNumber))]);
    }

    private static async Task<IResult> VoidSessionAsync(
        Guid sessionId, VoidSessionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new VoidTradingSessionCommand(sessionId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetDocumentsAsync(
        Guid sessionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BasketSummaryModel model = await dispatcher
            .QueryAsync(new GetSessionDocumentsQuery(sessionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new BasketSummaryResponse(
            model.SessionNumber,
            [.. model.Invoices.Select(invoice => new SummaryInvoiceResponse(
                invoice.CompanyId, invoice.InvoiceId, invoice.InvoiceNumber, invoice.Subtotal.Amount))],
            model.Total.Amount,
            model.NotATaxInvoiceStatement));
    }

    private static async Task<IResult> ReturnLinesAsync(
        Guid sessionId,
        ReturnBasketLinesRequest request,
        IMixedBasketReturnService returns,
        CancellationToken cancellationToken)
    {
        Guid id = await returns.ReturnLinesAsync(
                sessionId,
                request.CompanyId,
                [.. request.Lines.Select(line => new ReturnLineRequest(line.SessionLineId, line.QuantityValue))],
                request.InvoiceNumber,
                request.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/trading-sessions/{sessionId}/returns/{id}", new TradingIdResponse(id));
    }

    private static TradingSessionResponse ToResponse(TradingSessionView view)
        => new(
            view.SessionId,
            view.SessionNumber,
            view.SessionCompanyId,
            view.Currency,
            view.Status.ToString(),
            view.Gross.Amount,
            view.TenderType,
            view.TenderAmount?.Amount,
            view.FailureReason,
            view.UnwoundInvoiceNumbers,
            [.. view.Segments.Select(segment => new SessionSegmentResponse(
                segment.CompanyId,
                segment.Net.Amount,
                segment.Tax.Amount,
                segment.Gross.Amount,
                segment.Allocation?.Amount,
                segment.AllocationBasis,
                segment.InvoiceNumber,
                [.. segment.Lines.Select(line => new SessionLineResponse(
                    line.LineId,
                    line.CompanyId,
                    line.Barcode,
                    line.Description,
                    line.QuantityValue,
                    line.QuantityUom,
                    line.UnitPrice.Amount,
                    line.Discount.Amount,
                    line.TaxCode,
                    line.Tax.Amount,
                    line.Net.Amount,
                    line.Gross.Amount,
                    line.PackSize,
                    line.IsVoided))]))]);
}
