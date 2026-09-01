using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Procurement;
using VumaRetail.Application.Procurement.Commands;
using VumaRetail.Application.Procurement.Permissions;
using VumaRetail.Application.Procurement.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Procurement;

/// <summary>
/// The <c>procurement</c> module's endpoints: requisitions, RFQs and their quotes, purchase orders,
/// goods receipts, the three-way match and supplier scorecards.
/// </summary>
/// <remarks>
/// <para>
/// R3: nothing exists in a UI before it exists here. The buyer's desk, the goods-in screen, the
/// accounts-payable queue and the supplier review meeting are all one of these calls.
/// </para>
/// <para>
/// <b>Every document is a resource with sub-resources and no <c>PUT</c> on a committed one.</b> A
/// draft requisition, RFQ or order takes lines; approval and issue freeze them, and a change after that
/// is <c>POST /purchase-orders/{id}/amend</c>, which creates a new document (§7 rule 7, business rule
/// 3). There is deliberately no endpoint that edits an issued order, a submitted quote, a completed
/// receipt or a released match.
/// </para>
/// <para>
/// <b>Matching is a <c>POST</c> that can return a refusal as a success.</b>
/// <c>POST /purchase-orders/{id}/matches</c> returns <c>201</c> with a <c>Blocked</c> verdict when the
/// invoice does not agree with the delivery — the comparison ran, it produced an answer, and the answer
/// is stored (ADR-082). Only <c>POST /matches/{id}/release</c> refuses, with a <c>422</c>.
/// </para>
/// </remarks>
public static class ProcurementEndpoints
{
    /// <summary>Maps the procurement endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaProcurement(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        MapRequisitions(api);
        MapRfqs(api);
        MapPurchaseOrders(api);
        MapGoodsReceipts(api);
        MapMatches(api);
        MapScorecards(api);

        return endpoints;
    }

    private static void MapRequisitions(RouteGroupBuilder api)
    {
        RouteGroupBuilder requisitions = api
            .MapGroup("/procurement/requisitions")
            .WithTags("Procurement").RequireModule("procurement");

        requisitions.MapPost("/", CreatePurchaseRequisitionAsync)
            .RequirePermission(ProcurementPermissions.RequisitionRaise)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises a requisition — somebody saying they need something.")
            .WithDescription(
                "Commits nothing. It has no supplier and no price the shop is held to, which is also "
                + "what lets Stage 15's forecasting raise one before anybody has chosen who to buy from.");

        requisitions.MapGet("/", ListPurchaseRequisitionsAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<PageResponse<PurchaseRequisitionResponse>>()
            .WithSummary("Requisitions, newest first.")
            .WithDescription("Narrow with ?status=Submitted for the approver's queue.");

        requisitions.MapGet("/{requisitionId:guid}", GetPurchaseRequisitionAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<PurchaseRequisitionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One requisition, with its lines.");

        requisitions.MapPost("/{requisitionId:guid}/lines", AddPurchaseRequisitionLineAsync)
            .RequirePermission(ProcurementPermissions.RequisitionRaise)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts a line on a draft requisition.")
            .WithDescription(
                "The estimated cost is an estimate and is never carried onto an order as a price — the "
                + "price on a commitment is the supplier's, quoted or negotiated.");

        requisitions.MapPost("/{requisitionId:guid}/submit", SubmitPurchaseRequisitionAsync)
            .RequirePermission(ProcurementPermissions.RequisitionRaise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sends the requisition for approval. Lines freeze.")
            .WithDescription("422 when it has no lines.");

        requisitions.MapPost("/{requisitionId:guid}/decide", DecidePurchaseRequisitionAsync)
            .RequirePermission(ProcurementPermissions.RequisitionApprove)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Approves or rejects a submitted requisition.")
            .WithDescription(
                "One endpoint with a flag rather than two, because it is the same act by the same person "
                + "at the same moment. A rejection needs a reason the requester can act on.");

        requisitions.MapPost("/{requisitionId:guid}/cancel", CancelPurchaseRequisitionAsync)
            .RequirePermission(ProcurementPermissions.RequisitionRaise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Withdraws a requisition nobody has acted on.");
    }

    private static void MapRfqs(RouteGroupBuilder api)
    {
        RouteGroupBuilder rfqs = api.MapGroup("/procurement/rfqs").WithTags("Procurement").RequireModule("procurement");

        rfqs.MapPost("/", CreateRfqAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises an RFQ.")
            .WithDescription("422 when it names a requisition that has not been approved.");

        rfqs.MapGet("/", ListRfqsAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<PageResponse<RfqResponsePayload>>()
            .WithSummary("RFQs, newest first.")
            .WithDescription("Lines and quotes are not loaded here; fetch one RFQ to see them.");

        rfqs.MapGet("/{rfqId:guid}", GetRfqAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<RfqResponsePayload>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One RFQ, with its lines and every quote against it.");

        rfqs.MapPost("/{rfqId:guid}/lines", AddRfqLineAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts a line on a draft RFQ.")
            .WithDescription("No cost of any kind — an RFQ line is a question, and pricing it anchors the answer.");

        rfqs.MapPost("/{rfqId:guid}/issue", IssueRfqAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sends the RFQ out. Lines freeze; quoting opens.");

        rfqs.MapPost("/{rfqId:guid}/responses", RecordRfqResponseAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records one supplier's quote.")
            .WithDescription(
                "409 when that supplier has already quoted; 422 when the quote is dated after the RFQ "
                + "closed. A revised price is a new quote on a new RFQ — a submitted one is frozen.");

        rfqs.MapPost("/{rfqId:guid}/responses/{responseId:guid}/lines", AddRfqResponseLineAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts a quoted price against one of the RFQ's lines.")
            .WithDescription(
                "Omit availableQuantity when the supplier can supply everything asked for. A partial "
                + "quote is extended on what they can actually deliver, not on what was requested.");

        rfqs.MapPost("/{rfqId:guid}/award", AwardRfqAsync)
            .RequirePermission(ProcurementPermissions.RfqAward)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Awards the RFQ to one supplier's quote. Every other quote is declined.")
            .WithDescription(
                "Does not raise a purchase order. The order needs a delivery location, an expected date "
                + "and tax resolved per line, none of which a quote carries — and an award that silently "
                + "commits money removes the last point at which the wrong choice could be noticed.");

        rfqs.MapPost("/{rfqId:guid}/close", CloseRfqAsync)
            .RequirePermission(ProcurementPermissions.RfqManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes an RFQ nobody won, or withdraws one.");
    }

    private static void MapPurchaseOrders(RouteGroupBuilder api)
    {
        RouteGroupBuilder orders = api.MapGroup("/procurement/purchase-orders").WithTags("Procurement").RequireModule("procurement");

        orders.MapPost("/", CreatePurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.OrderManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises a purchase order — the commitment.")
            .WithDescription(
                "The currency is the supplier's and is bound here for the life of the document; every "
                + "line, receipt and match against it is denominated in it. 422 when the partner is not "
                + "an active supplier.");

        orders.MapGet("/", ListPurchaseOrdersAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<PageResponse<PurchaseOrderResponse>>()
            .WithSummary("Purchase orders, newest first.")
            .WithDescription("Narrow with ?partnerId= and ?status=Issued for the goods-in desk's view.");

        orders.MapGet("/{purchaseOrderId:guid}", GetPurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<PurchaseOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One order, with its lines and their running received and invoiced totals.");

        orders.MapPost("/{purchaseOrderId:guid}/lines", AddPurchaseOrderLineAsync)
            .RequirePermission(ProcurementPermissions.OrderManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts a line on a draft order, resolving its tax once at authoring time.")
            .WithDescription(
                "Whether the cost is treated as inclusive or exclusive of tax comes from the matched tax "
                + "rule, not from the caller. 422 when the order is no longer a draft or the currency "
                + "differs from the order's.");

        orders.MapDelete("/{purchaseOrderId:guid}/lines/{lineId:guid}", RemovePurchaseOrderLineAsync)
            .RequirePermission(ProcurementPermissions.OrderManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes a line off a draft order. Soft-deleted, never removed (§7 rule 8).");

        orders.MapPost("/{purchaseOrderId:guid}/approve", ApprovePurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.OrderIssue)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Approves a draft order, and optionally sends it in the same act.")
            .WithDescription("Lines freeze on approval. 422 when the order has no lines.");

        orders.MapPost("/{purchaseOrderId:guid}/issue", IssuePurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.OrderIssue)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sends an approved order to the supplier. Goods may now be received against it.");

        orders.MapPost("/{purchaseOrderId:guid}/amend", AmendPurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.OrderManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a replacement order at the next version and cancels this one.")
            .WithDescription(
                "The replacement comes back empty — its lines are yours to write. Copying them would "
                + "produce a document whose every line has to be re-checked anyway, and one that "
                + "silently carries a line somebody meant to remove.");

        orders.MapPost("/{purchaseOrderId:guid}/close", ClosePurchaseOrderAsync)
            .RequirePermission(ProcurementPermissions.OrderManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes an order, or cancels it with a reason.");

        orders.MapGet("/{purchaseOrderId:guid}/receipts", ListGoodsReceiptsForOrderAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<IReadOnlyList<GoodsReceiptResponse>>()
            .WithSummary("Every delivery against one order, oldest first.");

        orders.MapPost("/{purchaseOrderId:guid}/matches", MatchSupplierInvoiceAsync)
            .RequirePermission(ProcurementPermissions.InvoiceMatch)
            .Produces<ThreeWayMatchResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Runs a three-way match of a supplier's invoice against this order and its receipts.")
            .WithDescription(
                "A blocked match is a 201, not a 422: the comparison ran and produced an answer, and "
                + "'why did we not pay this' is a question somebody asks three weeks later. 409 when the "
                + "same supplier invoice number has already been matched against this order — that is "
                + "the one thing refused outright, because paying one delivery twice is not a variance "
                + "anybody can judge.");
    }

    private static void MapGoodsReceipts(RouteGroupBuilder api)
    {
        RouteGroupBuilder receipts = api.MapGroup("/procurement/goods-receipts").WithTags("Procurement").RequireModule("procurement");

        receipts.MapPost("/", CreateGoodsReceiptAsync)
            .RequirePermission(ProcurementPermissions.ReceiptRecord)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a goods receipt against an issued order.")
            .WithDescription(
                "Supply receivedAt when the delivery is being checked in later than it arrived — a "
                + "supplier is measured on when the truck came, not on when somebody got to the paperwork.");

        receipts.MapGet("/{goodsReceiptId:guid}", GetGoodsReceiptAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<GoodsReceiptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One receipt, with what arrived and what was sent back.");

        receipts.MapPost("/{goodsReceiptId:guid}/lines", AddGoodsReceiptLineAsync)
            .RequirePermission(ProcurementPermissions.ReceiptRecord)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records what arrived against one order line.")
            .WithDescription(
                "Accepted and rejected are separate figures and only the accepted half enters stock. A "
                + "rejected quantity needs a reason and a reason needs a quantity — a rejection with no "
                + "reason is invisible on a supplier scorecard.");

        receipts.MapPost("/{goodsReceiptId:guid}/complete", CompleteGoodsReceiptAsync)
            .RequirePermission(ProcurementPermissions.ReceiptRecord)
            .Produces<GoodsReceiptCompletionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Completes a receipt: freezes it, posts its stock, advances its order.")
            .WithDescription(
                "422 when a line exceeds what was ordered beyond the tenant's over-receipt tolerance — "
                + "amend the order or send the excess back. A stock movement the ledger refuses does not "
                + "fail the receipt; it is counted in stockPostingsRefused and listed under "
                + "/procurement/reconciliation/stock-issues.");

        receipts.MapPost("/{goodsReceiptId:guid}/cancel", CancelGoodsReceiptAsync)
            .RequirePermission(ProcurementPermissions.ReceiptRecord)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a draft receipt. Nothing moved.");

        RouteGroupBuilder reconciliation = api
            .MapGroup("/procurement/reconciliation")
            .WithTags("Procurement").RequireModule("procurement");

        reconciliation.MapGet("/stock-issues", ListRefusedStockPostingsAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<IReadOnlyList<GoodsReceiptResponse>>()
            .WithSummary("Receipts that completed without their stock movement being posted.")
            .WithDescription(
                "The mirror of the till's own reconciliation queue, and the same expectation applies: it "
                + "should stay empty. A growing list means the shelf and the system have drifted apart, "
                + "not that the feature is broken.");
    }

    private static void MapMatches(RouteGroupBuilder api)
    {
        RouteGroupBuilder matches = api.MapGroup("/procurement/matches").WithTags("Procurement").RequireModule("procurement");

        matches.MapGet("/", ListSupplierInvoiceMatchesAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<IReadOnlyList<SupplierInvoiceMatchResponse>>()
            .WithSummary("Matches in a period, newest first.")
            .WithDescription("?status=Blocked is the accounts-payable work queue before a payment run.");

        matches.MapGet("/{matchId:guid}", GetSupplierInvoiceMatchAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<SupplierInvoiceMatchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One match, with the comparison line by line.");

        matches.MapPost("/{matchId:guid}/release", ReleaseSupplierInvoiceAsync)
            .RequirePermission(ProcurementPermissions.InvoiceRelease)
            .Produces<SupplierInvoiceReleaseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Releases a matched invoice for payment. Raises the financial event.")
            .WithDescription(
                "422 on a blocked match — resolve the variance with the supplier and match their credit "
                + "note. A released match is frozen, and its invoiced quantities are written back onto "
                + "the order so the next invoice against it is checked cumulatively.");
    }

    private static void MapScorecards(RouteGroupBuilder api)
    {
        RouteGroupBuilder scorecards = api.MapGroup("/procurement/scorecards").WithTags("Procurement").RequireModule("procurement");

        scorecards.MapPost("/", SnapshotSupplierScorecardAsync)
            .RequirePermission(ProcurementPermissions.ScorecardManage)
            .Produces<ProcurementIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes a supplier scorecard snapshot for one closed period.")
            .WithDescription(
                "422 while the period is still running, 409 once it has been snapshotted. A rating "
                + "recomputed on read changes when nobody did anything, and a rating that moves on its "
                + "own is one nobody trusts enough to act on.");

        scorecards.MapGet("/", ListScorecardLeagueAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<IReadOnlyList<SupplierScorecardResponse>>()
            .WithSummary("Every supplier's scorecard for one period, best rating first.")
            .WithDescription("The league table the supplier review meeting reads.");

        scorecards.MapGet("/{partnerId:guid}", ListSupplierScorecardsAsync)
            .RequirePermission(ProcurementPermissions.View)
            .Produces<IReadOnlyList<SupplierScorecardResponse>>()
            .WithSummary("One supplier's scorecards, newest period first — their trend.");
    }

    private static async Task<IResult> CreatePurchaseRequisitionAsync(
        CreatePurchaseRequisitionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreatePurchaseRequisitionCommand(
                    request.LocationId, request.RequiredBy, request.Justification),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/requisitions/{id}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> ListPurchaseRequisitionsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? status = null,
        int? limit = null,
        string? after = null)
    {
        PurchaseRequisitionStatus? narrowed = status is null
            ? null
            : ParseEnum<PurchaseRequisitionStatus>(status, nameof(status), "ListPurchaseRequisitions");

        PageResult<Domain.Procurement.PurchaseRequisition> page = await dispatcher
            .QueryAsync(new ListPurchaseRequisitionsQuery(narrowed, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<PurchaseRequisitionResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> GetPurchaseRequisitionAsync(
        Guid requisitionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Domain.Procurement.PurchaseRequisition requisition = await dispatcher
            .QueryAsync(new GetPurchaseRequisitionQuery(requisitionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(requisition));
    }

    private static async Task<IResult> AddPurchaseRequisitionLineAsync(
        Guid requisitionId,
        AddPurchaseRequisitionLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Money? estimate = BuildOptionalMoney(
            request.EstimatedUnitCost,
            request.Currency,
            nameof(request.Currency),
            "AddPurchaseRequisitionLine");

        Guid id = await dispatcher
            .SendAsync(
                new AddPurchaseRequisitionLineCommand(
                    requisitionId,
                    request.ItemId,
                    request.ItemVariantId,
                    request.Description,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    estimate),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/requisitions/{requisitionId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> SubmitPurchaseRequisitionAsync(
        Guid requisitionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new SubmitPurchaseRequisitionCommand(requisitionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DecidePurchaseRequisitionAsync(
        Guid requisitionId,
        DecidePurchaseRequisitionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new DecidePurchaseRequisitionCommand(requisitionId, request.Approve, request.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CancelPurchaseRequisitionAsync(
        Guid requisitionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new CancelPurchaseRequisitionCommand(requisitionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateRfqAsync(
        CreateRfqRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateRfqCommand(request.Title, request.PurchaseRequisitionId, request.ClosesAt),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/procurement/rfqs/{id}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> ListRfqsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? status = null,
        int? limit = null,
        string? after = null)
    {
        RfqStatus? narrowed = status is null
            ? null
            : ParseEnum<RfqStatus>(status, nameof(status), "ListRfqs");

        PageResult<Domain.Procurement.Rfq> page = await dispatcher
            .QueryAsync(new ListRfqsQuery(narrowed, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<RfqResponsePayload>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> GetRfqAsync(
        Guid rfqId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Domain.Procurement.Rfq rfq = await dispatcher
            .QueryAsync(new GetRfqQuery(rfqId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(rfq));
    }

    private static async Task<IResult> AddRfqLineAsync(
        Guid rfqId,
        AddRfqLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddRfqLineCommand(
                    rfqId,
                    request.ItemId,
                    request.ItemVariantId,
                    request.Description,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    request.Specification,
                    request.PurchaseRequisitionLineId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/procurement/rfqs/{rfqId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> IssueRfqAsync(
        Guid rfqId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new IssueRfqCommand(rfqId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RecordRfqResponseAsync(
        Guid rfqId,
        RecordRfqResponseRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordRfqResponseCommand(
                    rfqId,
                    request.PartnerId,
                    request.Currency,
                    request.QuotedAt,
                    request.ValidUntil,
                    request.LeadTimeDays,
                    request.Notes),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/procurement/rfqs/{rfqId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> AddRfqResponseLineAsync(
        Guid rfqId,
        Guid responseId,
        AddRfqResponseLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddRfqResponseLineCommand(
                    rfqId,
                    responseId,
                    request.RfqLineId,
                    new Money(request.UnitCost, request.Currency),
                    request.AvailableQuantity),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/procurement/rfqs/{rfqId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> AwardRfqAsync(
        Guid rfqId, AwardRfqRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new AwardRfqCommand(rfqId, request.RfqResponseId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CloseRfqAsync(
        Guid rfqId, CloseRfqRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new CloseRfqCommand(rfqId, request.Cancel), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreatePurchaseOrderCommand(
                    request.PartnerId,
                    request.Currency,
                    request.LocationId,
                    request.ExpectedAt,
                    request.RfqResponseId,
                    request.Notes),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/purchase-orders/{id}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> ListPurchaseOrdersAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        Guid? partnerId = null,
        string? status = null,
        int? limit = null,
        string? after = null)
    {
        PurchaseOrderStatus? narrowed = status is null
            ? null
            : ParseEnum<PurchaseOrderStatus>(status, nameof(status), "ListPurchaseOrders");

        PageResult<Domain.Procurement.PurchaseOrder> page = await dispatcher
            .QueryAsync(
                new ListPurchaseOrdersQuery(partnerId, narrowed, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<PurchaseOrderResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> GetPurchaseOrderAsync(
        Guid purchaseOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Domain.Procurement.PurchaseOrder order = await dispatcher
            .QueryAsync(new GetPurchaseOrderQuery(purchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(order));
    }

    private static async Task<IResult> AddPurchaseOrderLineAsync(
        Guid purchaseOrderId,
        AddPurchaseOrderLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddPurchaseOrderLineCommand(
                    purchaseOrderId,
                    request.ItemId,
                    request.ItemVariantId,
                    request.Description,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    new Money(request.UnitCost, request.Currency),
                    request.TaxCode,
                    request.PurchaseRequisitionLineId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/purchase-orders/{purchaseOrderId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> RemovePurchaseOrderLineAsync(
        Guid purchaseOrderId, Guid lineId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new RemovePurchaseOrderLineCommand(purchaseOrderId, lineId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ApprovePurchaseOrderAsync(
        Guid purchaseOrderId,
        ApprovePurchaseOrderRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ApprovePurchaseOrderCommand(purchaseOrderId, request.Issue), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> IssuePurchaseOrderAsync(
        Guid purchaseOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new IssuePurchaseOrderCommand(purchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AmendPurchaseOrderAsync(
        Guid purchaseOrderId,
        AmendPurchaseOrderRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new AmendPurchaseOrderCommand(purchaseOrderId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/purchase-orders/{id}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> ClosePurchaseOrderAsync(
        Guid purchaseOrderId,
        ClosePurchaseOrderRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new ClosePurchaseOrderCommand(purchaseOrderId, request.Cancel, request.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateGoodsReceiptAsync(
        CreateGoodsReceiptRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateGoodsReceiptCommand(
                    request.PurchaseOrderId, request.DeliveryNoteNumber, request.ReceivedAt),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/goods-receipts/{id}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> GetGoodsReceiptAsync(
        Guid goodsReceiptId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Domain.Procurement.GoodsReceipt receipt = await dispatcher
            .QueryAsync(new GetGoodsReceiptQuery(goodsReceiptId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(receipt));
    }

    private static async Task<IResult> AddGoodsReceiptLineAsync(
        Guid goodsReceiptId,
        AddGoodsReceiptLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        GoodsRejectionReason reason = ParseEnum<GoodsRejectionReason>(
            request.RejectionReason, nameof(request.RejectionReason), "AddGoodsReceiptLine");

        Guid id = await dispatcher
            .SendAsync(
                new AddGoodsReceiptLineCommand(
                    goodsReceiptId,
                    request.PurchaseOrderLineId,
                    new Quantity(request.AcceptedQuantity, request.UnitOfMeasure),
                    request.RejectedQuantity is { } rejected
                        ? new Quantity(rejected, request.UnitOfMeasure)
                        : null,
                    reason,
                    request.Note),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/goods-receipts/{goodsReceiptId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> CompleteGoodsReceiptAsync(
        Guid goodsReceiptId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        GoodsReceiptCompletionResult result = await dispatcher
            .SendAsync(new CompleteGoodsReceiptCommand(goodsReceiptId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new GoodsReceiptCompletionResponse(
            result.GoodsReceiptId,
            result.ReceiptNumber,
            result.ReceivedValue.Amount,
            result.ReceivedValue.Currency,
            result.OrderStatus.ToString(),
            result.StockPostingsRefused));
    }

    private static async Task<IResult> CancelGoodsReceiptAsync(
        Guid goodsReceiptId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new CancelGoodsReceiptCommand(goodsReceiptId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListGoodsReceiptsForOrderAsync(
        Guid purchaseOrderId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Procurement.GoodsReceipt> receipts = await dispatcher
            .QueryAsync(new ListGoodsReceiptsForOrderQuery(purchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<GoodsReceiptResponse>>([.. receipts.Select(ToResponse)]);
    }

    private static async Task<IResult> ListRefusedStockPostingsAsync(
        IDispatcher dispatcher,
        IClock clock,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 100)
    {
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<Domain.Procurement.GoodsReceipt> receipts = await dispatcher
            .QueryAsync(
                new ListRefusedStockPostingsQuery(from ?? today.AddDays(-30), to ?? today, limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<GoodsReceiptResponse>>([.. receipts.Select(ToResponse)]);
    }

    private static async Task<IResult> MatchSupplierInvoiceAsync(
        Guid purchaseOrderId,
        MatchSupplierInvoiceRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ThreeWayMatchResult result = await dispatcher
            .SendAsync(
                new MatchSupplierInvoiceCommand(
                    purchaseOrderId,
                    request.SupplierInvoiceNumber,
                    request.InvoiceDate,
                    new Money(request.ClaimedNet, request.Currency),
                    new Money(request.ClaimedTax, request.Currency),
                    [
                        .. request.Lines.Select(line => new SupplierInvoiceLineInput(
                            line.PurchaseOrderLineId,
                            line.Description,
                            new Quantity(line.Quantity, line.UnitOfMeasure),
                            new Money(line.UnitCost, request.Currency))),
                    ]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/matches/{result.SupplierInvoiceMatchId}",
            new ThreeWayMatchResponse(
                result.SupplierInvoiceMatchId,
                result.Status.ToString(),
                result.Variances.ToString(),
                result.ClaimedNet.Amount,
                result.MatchedNet.Amount,
                result.PriceVariance.Amount,
                result.ClaimedNet.Currency,
                result.IsPayable));
    }

    private static async Task<IResult> ListSupplierInvoiceMatchesAsync(
        IDispatcher dispatcher,
        IClock clock,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        string? status = null,
        int limit = 100)
    {
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        ThreeWayMatchStatus? narrowed = status is null
            ? null
            : ParseEnum<ThreeWayMatchStatus>(status, nameof(status), "ListSupplierInvoiceMatches");

        IReadOnlyList<Domain.Procurement.SupplierInvoiceMatch> matches = await dispatcher
            .QueryAsync(
                new ListSupplierInvoiceMatchesQuery(
                    from ?? today.AddDays(-30), to ?? today, narrowed, limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SupplierInvoiceMatchResponse>>(
            [.. matches.Select(ToResponse)]);
    }

    private static async Task<IResult> GetSupplierInvoiceMatchAsync(
        Guid matchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Domain.Procurement.SupplierInvoiceMatch match = await dispatcher
            .QueryAsync(new GetSupplierInvoiceMatchQuery(matchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(match));
    }

    private static async Task<IResult> ReleaseSupplierInvoiceAsync(
        Guid matchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SupplierInvoiceReleaseResult result = await dispatcher
            .SendAsync(new ReleaseSupplierInvoiceCommand(matchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SupplierInvoiceReleaseResponse(
            result.SupplierInvoiceMatchId,
            result.SupplierInvoiceNumber,
            result.Gross.Amount,
            result.Gross.Currency,
            result.JournalId,
            result.OrderStatus.ToString()));
    }

    private static async Task<IResult> SnapshotSupplierScorecardAsync(
        SnapshotSupplierScorecardRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new SnapshotSupplierScorecardCommand(
                    request.PartnerId, request.PeriodStart, request.PeriodEnd, request.Currency),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/procurement/scorecards/{request.PartnerId}", new ProcurementIdResponse(id));
    }

    private static async Task<IResult> ListScorecardLeagueAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Procurement.SupplierScorecard> scorecards = await dispatcher
            .QueryAsync(new ListScorecardLeagueQuery(periodStart, periodEnd), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SupplierScorecardResponse>>(
            [.. scorecards.Select(ToResponse)]);
    }

    private static async Task<IResult> ListSupplierScorecardsAsync(
        Guid partnerId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int limit = 24)
    {
        IReadOnlyList<Domain.Procurement.SupplierScorecard> scorecards = await dispatcher
            .QueryAsync(new ListSupplierScorecardsQuery(partnerId, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SupplierScorecardResponse>>(
            [.. scorecards.Select(ToResponse)]);
    }

    private static PurchaseRequisitionResponse ToResponse(Domain.Procurement.PurchaseRequisition requisition)
        => new(
            requisition.Id,
            requisition.RequisitionNumber,
            requisition.RequestedByUserId,
            requisition.LocationId,
            requisition.RequiredBy,
            requisition.Justification,
            requisition.Status.ToString(),
            requisition.RaisedAt,
            requisition.DecidedByUserId,
            requisition.DecidedAt,
            requisition.RejectionReason,
            [
                .. requisition.Lines.Select(line => new PurchaseRequisitionLineResponse(
                    line.Id,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.EstimatedUnitCost?.Amount,
                    line.EstimatedUnitCost?.Currency,
                    line.SourcedToDocumentId)),
            ]);

    private static RfqResponsePayload ToResponse(Domain.Procurement.Rfq rfq)
        => new(
            rfq.Id,
            rfq.RfqNumber,
            rfq.Title,
            rfq.PurchaseRequisitionId,
            rfq.ClosesAt,
            rfq.Status.ToString(),
            rfq.AwardedResponseId,
            [
                .. rfq.Lines.Select(line => new RfqLineResponse(
                    line.Id,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.Specification)),
            ],
            [
                // Cheapest first: the buyer's screen puts the winning candidate at the top, and a quote
                // that cannot supply everything is still shown — IsFullQuantity is on each line, and
                // hiding a partial quote is how a shop ends up believing only one supplier answered.
                .. rfq.Responses
                    .OrderBy(response => response.Total.Amount)
                    .Select(response => new RfqResponseResponse(
                        response.Id,
                        response.PartnerId,
                        response.Currency,
                        response.QuotedAt,
                        response.ValidUntil,
                        response.LeadTimeDays,
                        response.Status.ToString(),
                        response.Total.Amount,
                        response.Notes,
                        [
                            .. response.Lines.Select(line => new RfqResponseLineResponse(
                                line.Id,
                                line.RfqLineId,
                                line.RequestedQuantity.Value,
                                line.QuotedQuantity.Value,
                                line.UnitCost.Amount,
                                line.ExtendedCost.Amount)),
                        ])),
            ]);

    private static PurchaseOrderResponse ToResponse(Domain.Procurement.PurchaseOrder order)
        => new(
            order.Id,
            order.OrderNumber,
            order.PartnerId,
            order.Currency,
            order.LocationId,
            order.ExpectedAt,
            order.Status.ToString(),
            order.Version,
            order.AmendsPurchaseOrderId,
            order.RfqResponseId,
            order.Net.Amount,
            order.Tax.Amount,
            order.Gross.Amount,
            order.Notes,
            [
                .. order.Lines.Select(line => new PurchaseOrderLineResponse(
                    line.Id,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.UnitCost.Amount,
                    line.TaxCode,
                    line.Net.Amount,
                    line.Tax.Amount,
                    line.Gross.Amount,
                    line.ReceivedQuantity.Value,
                    line.RejectedQuantity.Value,
                    line.InvoicedQuantity.Value,
                    line.OutstandingQuantity.Value)),
            ]);

    private static GoodsReceiptResponse ToResponse(Domain.Procurement.GoodsReceipt receipt)
        => new(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.PurchaseOrderId,
            receipt.PartnerId,
            receipt.LocationId,
            receipt.Currency,
            receipt.DeliveryNoteNumber,
            receipt.ReceivedAt,
            receipt.Status.ToString(),
            receipt.ReceivedValue.Amount,
            receipt.StockPostingsRefused,
            [
                .. receipt.Lines.Select(line => new GoodsReceiptLineResponse(
                    line.Id,
                    line.PurchaseOrderLineId,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.OrderedQuantity.Value,
                    line.AcceptedQuantity.Value,
                    line.RejectedQuantity.Value,
                    line.RejectionReason.ToString(),
                    line.AcceptedQuantity.UnitOfMeasure,
                    line.UnitCost.Amount,
                    line.AcceptedValue.Amount,
                    line.StockPosting.ToString(),
                    line.StockLedgerEntryId,
                    line.StockPostingNote)),
            ]);

    private static SupplierInvoiceMatchResponse ToResponse(Domain.Procurement.SupplierInvoiceMatch match)
        => new(
            match.Id,
            match.PurchaseOrderId,
            match.PartnerId,
            match.SupplierInvoiceNumber,
            match.InvoiceDate,
            match.Currency,
            match.ClaimedNet.Amount,
            match.ClaimedTax.Amount,
            match.ClaimedGross.Amount,
            match.MatchedNet.Amount,
            match.PriceVariance.Amount,
            match.Status.ToString(),
            match.Variances.ToString(),
            match.IsPayable,
            match.ReleasedAt,
            match.JournalId,
            [
                // Blocked lines first. On a fifty-line invoice the two that disagree are the only rows
                // anybody is opening this document to look at.
                .. match.Lines
                    .OrderByDescending(line => line.Status)
                    .Select(line => new SupplierInvoiceMatchLineResponse(
                        line.Id,
                        line.PurchaseOrderLineId,
                        line.Description,
                        line.OrderedQuantity.Value,
                        line.ReceivedQuantity.Value,
                        line.InvoicedQuantity.Value,
                        line.PreviouslyInvoicedQuantity.Value,
                        line.InvoicedQuantity.UnitOfMeasure,
                        line.OrderedUnitCost.Amount,
                        line.InvoicedUnitCost.Amount,
                        line.OrderedValue.Amount,
                        line.InvoicedValue.Amount,
                        line.PriceVariance.Amount,
                        line.QuantityVariance.Value,
                        line.Status.ToString(),
                        line.Variances.ToString())),
            ]);

    private static SupplierScorecardResponse ToResponse(Domain.Procurement.SupplierScorecard scorecard)
        => new(
            scorecard.Id,
            scorecard.PartnerId,
            scorecard.PeriodStart,
            scorecard.PeriodEnd,
            scorecard.Currency,
            scorecard.OrdersPlaced,
            scorecard.LinesOrdered,
            scorecard.LinesDelivered,
            scorecard.LinesDeliveredOnTime,
            scorecard.LinesWithRejections,
            scorecard.QuantityOrdered,
            scorecard.QuantityReceived,
            scorecard.QuantityRejected,
            scorecard.PurchaseValue.Amount,
            scorecard.PriceVariance.Amount,
            scorecard.OnTimeDeliveryRate,
            scorecard.FillRate,
            scorecard.QualityRate,
            scorecard.OverallRating,
            scorecard.SnapshottedAt);

    /// <summary>
    /// Builds an optional monetary amount, refusing an amount with no currency rather than guessing one.
    /// </summary>
    /// <remarks>
    /// §7 rule 4: a bare decimal with no currency is how a multi-currency system quietly adds rands to
    /// dollars. Defaulting to the tenant's currency here would be the guess that makes it happen.
    /// </remarks>
    private static Money? BuildOptionalMoney(
        decimal? amount, string? currency, string propertyName, string messageName)
    {
        if (amount is not { } value)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ValidationFailedException(
                messageName,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [propertyName] = ["A currency is required when an amount is supplied."],
                });
        }

        return new Money(value, currency);
    }

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
