using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Application.Pos.Permissions;
using VumaRetail.Application.Pos.Queries;
using VumaRetail.Contracts.Pos;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Hardware.Receipts;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Pos;

/// <summary>
/// The <c>pos</c> module's endpoints: till sessions, sales, lines, tenders, receipts and the
/// reconciliation queue.
/// </summary>
/// <remarks>
/// <para>
/// R3: nothing exists in a UI before it exists here. Every screen the WPF till will need — open a
/// shift, scan an item, take a payment, print, park, void, count the drawer — is one of these calls,
/// which is what makes the deferred desktop shell a rendering job rather than a second implementation.
/// </para>
/// <para>
/// A sale is a resource with sub-resources rather than one "record a sale" endpoint, because that is
/// the shape a till actually works in: lines arrive one scan at a time and the customer changes their
/// mind halfway through. The one-shot path exists too — a terminal that traded offline supplies its
/// own <c>saleId</c> on <c>POST /pos/sales</c> and replays the sequence, and the replay is idempotent.
/// </para>
/// <para>
/// There is no <c>PUT</c> or <c>DELETE</c> on a sale, a line or a tender. A line is voided, a sale is
/// voided, and a completed sale is corrected by a Stage 10 return — never by an edit (§7 rule 7).
/// </para>
/// </remarks>
public static class PosEndpoints
{
    /// <summary>Maps the POS endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaPos(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder tills = api.MapGroup("/pos/till-sessions").WithTags("POS").RequireModule("pos");

        tills.MapPost("/", OpenTillSessionAsync)
            .RequirePermission(PosPermissions.TillOpen)
            .Produces<PosIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a shift at the calling terminal with a counted float.")
            .WithDescription(
                "409 when the terminal already has an open session — two open sessions on one drawer "
                + "means neither one's expected cash is a real number.");

        tills.MapGet("/", ListTillSessionsAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<IReadOnlyList<TillSessionResponse>>()
            .WithSummary("Recent till sessions, newest first.");

        tills.MapGet("/{tillSessionId:guid}", GetTillSessionAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<TillSessionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One till session and its derived cash position.")
            .WithDescription(
                "The expected cash is computed on every read from the session's own sales, so a "
                + "supervisor can take a mid-shift reading without closing anything.");

        tills.MapPost("/{tillSessionId:guid}/close", CloseTillSessionAsync)
            .RequirePermission(PosPermissions.TillClose)
            .Produces<CashUpResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Counts the drawer and closes the shift permanently.")
            .WithDescription(
                "422 while any sale on the session is still open or parked. The expected figure is "
                + "derived and is never an input.");

        RouteGroupBuilder sales = api.MapGroup("/pos/sales").WithTags("POS").RequireModule("pos");

        sales.MapPost("/", OpenSaleAsync)
            .RequirePermission(PosPermissions.SaleRing)
            .Produces<PosIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Opens a sale at the calling terminal's open till session.")
            .WithDescription(
                "Supplying a saleId that already exists returns that sale rather than creating a "
                + "second one, so a terminal replaying an offline sale is idempotent.");

        sales.MapGet("/{saleId:guid}", GetSaleAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<SaleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One sale, with its lines and tenders.");

        sales.MapPost("/{saleId:guid}/lines", AddSaleLineAsync)
            .RequirePermission(PosPermissions.SaleRing)
            .Produces<PosIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rings a line up, pricing its tax through the tax rules engine.")
            .WithDescription(
                "The unit price is supplied by the caller: Stage 06 shipped no price and the price "
                + "list is Stage 10's (ADR-072).");

        sales.MapPost("/{saleId:guid}/lines/{saleLineId:guid}/void", VoidSaleLineAsync)
            .RequirePermission(PosPermissions.SaleVoid)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes a line off the sale. The line stays on the record.");

        sales.MapPost("/{saleId:guid}/tenders", TenderSaleAsync)
            .RequirePermission(PosPermissions.SaleTender)
            .Produces<PosIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes a payment. Immutable once captured.");

        sales.MapPost("/{saleId:guid}/complete", CompleteSaleAsync)
            .RequirePermission(PosPermissions.SaleTender)
            .Produces<SaleCompletionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes the sale: freezes it, relieves stock and raises its financial event.")
            .WithDescription(
                "A line whose stock the ledger refuses does not fail the sale — it completes with "
                + "stockIssuesRefused above zero and appears in the reconciliation queue (ADR-073).");

        sales.MapPost("/{saleId:guid}/park", ParkSaleAsync)
            .RequirePermission(PosPermissions.SaleRing)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Sets the sale aside so the terminal can serve the next customer.");

        sales.MapPost("/{saleId:guid}/resume", ResumeSaleAsync)
            .RequirePermission(PosPermissions.SaleRing)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Brings a parked sale back to the screen.");

        sales.MapPost("/{saleId:guid}/void", VoidSaleAsync)
            .RequirePermission(PosPermissions.SaleVoid)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a sale before it is paid for, with a recorded reason.")
            .WithDescription("422 on a completed sale — that is a Stage 10 return, not a void.");

        sales.MapGet("/{saleId:guid}/receipt", GetReceiptAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<ReceiptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Builds the receipt, including the fixed-width layout a printer would set.")
            .WithDescription("A read. Recording that a copy was printed is the POST below.");

        sales.MapPost("/{saleId:guid}/receipt/prints", RecordReceiptPrintAsync)
            .RequirePermission(PosPermissions.ReceiptPrint)
            .Produces<PosIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records that the receipt came out of a printer.")
            .WithDescription(
                "Whether this is a reprint is derived from the log, not supplied — and a reprint with "
                + "no reason is refused with 422.");

        sales.MapGet("/{saleId:guid}/receipt/prints", ListReceiptPrintsAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<IReadOnlyList<ReceiptPrintResponse>>()
            .WithSummary("Every print of this receipt, oldest first.");

        RouteGroupBuilder terminals = api.MapGroup("/pos/terminals").WithTags("POS").RequireModule("pos");

        terminals.MapGet("/{terminalId:guid}/parked-sales", ListParkedSalesAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<IReadOnlyList<SaleResponse>>()
            .WithSummary("Every sale set aside at one terminal, oldest first.");

        RouteGroupBuilder catalog = api.MapGroup("/pos/catalog").WithTags("POS").RequireModule("pos");

        catalog.MapGet("/barcode/{barcode}", LookupBarcodeAsync)
            .RequirePermission(PosPermissions.SaleRing)
            .Produces<SellableItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Resolves a scanned barcode into something the till can ring up.");

        RouteGroupBuilder reconciliation = api.MapGroup("/pos/reconciliation").WithTags("POS").RequireModule("pos");

        reconciliation.MapGet("/stock-issues", ListRefusedStockIssuesAsync)
            .RequirePermission(PosPermissions.SaleView)
            .Produces<IReadOnlyList<RefusedStockIssueResponse>>()
            .WithSummary("Sales that completed without relieving stock (ADR-073).")
            .WithDescription(
                "The queue a manager works through with a stocktake. An empty list is the normal "
                + "state; a growing one means the shelf and the system have drifted apart.");

        return endpoints;
    }

    private static async Task<IResult> OpenTillSessionAsync(
        OpenTillSessionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new OpenTillSessionCommand(new Money(request.OpeningFloat, request.Currency)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/pos/till-sessions/{id}", new PosIdResponse(id));
    }

    private static async Task<IResult> ListTillSessionsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken, Guid? terminalId = null, int limit = 50)
    {
        IReadOnlyList<TillSession> sessions = await dispatcher
            .QueryAsync(new ListTillSessionsQuery(terminalId, limit), cancellationToken)
            .ConfigureAwait(false);

        // Listed without the derived cash position: computing it needs every sale on every session,
        // which turns a list of fifty shifts into fifty aggregate loads. The single-session read below
        // is where a person asks that question.
        return TypedResults.Ok<IReadOnlyList<TillSessionResponse>>(
        [
            .. sessions.Select(session => ToResponse(session, session.OpeningFloat, 0, 0)),
        ]);
    }

    private static async Task<IResult> GetTillSessionAsync(
        Guid tillSessionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TillSessionView view = await dispatcher
            .QueryAsync(new GetTillSessionQuery(tillSessionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(view.Session, view.ExpectedCash, view.SalesCompleted, view.SalesUnfinished));
    }

    private static async Task<IResult> CloseTillSessionAsync(
        Guid tillSessionId,
        CloseTillSessionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        CashUpResult result = await dispatcher
            .SendAsync(
                new CloseTillSessionCommand(
                    tillSessionId,
                    new Money(request.CountedCash, request.Currency),
                    request.Note),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new CashUpResponse(
            result.ExpectedCash.Amount,
            result.CountedCash.Amount,
            result.Variance.Amount,
            result.Variance.Currency));
    }

    private static async Task<IResult> OpenSaleAsync(
        OpenSaleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new OpenSaleCommand(request.SaleId, request.LocationId, request.CustomerId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/pos/sales/{id}", new PosIdResponse(id));
    }

    private static async Task<IResult> GetSaleAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Sale sale = await dispatcher.QueryAsync(new GetSaleQuery(saleId), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(sale));
    }

    private static async Task<IResult> AddSaleLineAsync(
        Guid saleId, AddSaleLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Money? discount = request.DiscountAmount is { } amount
            ? new Money(amount, request.Currency)
            : null;

        Guid id = await dispatcher
            .SendAsync(
                new AddSaleLineCommand(
                    saleId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    new Money(request.UnitPrice, request.Currency),
                    discount),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/pos/sales/{saleId}", new PosIdResponse(id));
    }

    private static async Task<IResult> VoidSaleLineAsync(
        Guid saleId, Guid saleLineId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new VoidSaleLineCommand(saleId, saleLineId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> TenderSaleAsync(
        Guid saleId, TenderSaleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TenderType type = ParseEnum<TenderType>(
            request.TenderType, nameof(request.TenderType), nameof(TenderSaleCommand));

        Guid id = await dispatcher
            .SendAsync(
                new TenderSaleCommand(saleId, type, new Money(request.Amount, request.Currency), request.Reference),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/pos/sales/{saleId}", new PosIdResponse(id));
    }

    private static async Task<IResult> CompleteSaleAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SaleCompletionResult result = await dispatcher
            .SendAsync(new CompleteSaleCommand(saleId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SaleCompletionResponse(
            result.SaleId,
            result.SaleNumber,
            result.Gross.Amount,
            result.AmountTendered.Amount,
            result.ChangeGiven.Amount,
            result.Gross.Currency,
            result.StockIssuesRefused));
    }

    private static async Task<IResult> ParkSaleAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ParkSaleCommand(saleId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResumeSaleAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ResumeSaleCommand(saleId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> VoidSaleAsync(
        Guid saleId, VoidSaleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new VoidSaleCommand(saleId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetReceiptAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ReceiptDocument receipt = await dispatcher
            .QueryAsync(new BuildReceiptQuery(saleId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(receipt));
    }

    private static async Task<IResult> RecordReceiptPrintAsync(
        Guid saleId,
        RecordReceiptPrintRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new RecordReceiptPrintCommand(saleId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/pos/sales/{saleId}/receipt/prints", new PosIdResponse(id));
    }

    private static async Task<IResult> ListReceiptPrintsAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReceiptPrint> prints = await dispatcher
            .QueryAsync(new ListReceiptPrintsQuery(saleId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ReceiptPrintResponse>>(
        [
            .. prints.Select(print => new ReceiptPrintResponse(
                print.Id,
                print.PrintedByUserId,
                print.TerminalId,
                print.IsReprint,
                print.Reason,
                print.PrintedAt)),
        ]);
    }

    private static async Task<IResult> ListParkedSalesAsync(
        Guid terminalId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<Sale> sales = await dispatcher
            .QueryAsync(new ListParkedSalesQuery(terminalId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SaleResponse>>([.. sales.Select(ToResponse)]);
    }

    private static async Task<IResult> LookupBarcodeAsync(
        string barcode, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SellableItem item = await dispatcher
            .QueryAsync(new LookupBarcodeQuery(barcode), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SellableItemResponse(
            item.ItemId,
            item.ItemVariantId,
            item.Description,
            item.UnitOfMeasureCode,
            item.TaxClassCode));
    }

    private static async Task<IResult> ListRefusedStockIssuesAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken, Guid? locationId = null, int limit = 100)
    {
        IReadOnlyList<RefusedStockIssue> rows = await dispatcher
            .QueryAsync(new ListRefusedStockIssuesQuery(locationId, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<RefusedStockIssueResponse>>(
        [
            .. rows.Select(row => new RefusedStockIssueResponse(
                row.SaleId,
                row.SaleNumber,
                row.SaleLineId,
                row.LocationId,
                row.ItemId,
                row.ItemVariantId,
                row.Description,
                row.Quantity.Value,
                row.Quantity.UnitOfMeasure,
                row.Reason,
                row.CompletedAt)),
        ]);
    }

    private static TillSessionResponse ToResponse(
        TillSession session, Money expectedCash, int salesCompleted, int salesUnfinished)
        => new(
            session.Id,
            session.TerminalId,
            session.OperatorUserId,
            session.Status.ToString(),
            session.Currency,
            session.OpeningFloat.Amount,
            session.OpenedAt,
            session.ClosedAt,
            session.CountedCash?.Amount,
            session.IsClosed ? session.ExpectedCash!.Value.Amount : expectedCash.Amount,
            session.Variance?.Amount,
            salesCompleted,
            salesUnfinished,
            session.Note);

    private static SaleResponse ToResponse(Sale sale)
        => new(
            sale.Id,
            sale.SaleNumber,
            sale.Status.ToString(),
            sale.TillSessionId,
            sale.TerminalId,
            sale.OperatorUserId,
            sale.LocationId,
            sale.CustomerId,
            sale.Currency,
            sale.Net.Amount,
            sale.Tax.Amount,
            sale.Gross.Amount,
            sale.AmountTendered.Amount,
            sale.ChangeGiven.Amount,
            sale.OpenedAt,
            sale.CompletedAt,
            sale.VoidedAt,
            sale.VoidReason,
            [
                .. sale.Lines.OrderBy(line => line.LineNumber).Select(line => new SaleLineResponse(
                    line.Id,
                    line.LineNumber,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.UnitPrice.Amount,
                    line.DiscountAmount.Amount,
                    line.TaxCode,
                    line.Net.Amount,
                    line.Tax.Amount,
                    line.Gross.Amount,
                    line.IsVoided,
                    line.StockIssue.ToString(),
                    line.StockIssueNote)),
            ],
            [
                .. sale.Tenders.Select(tender => new SaleTenderResponse(
                    tender.Id,
                    tender.Type.ToString(),
                    tender.Amount.Amount,
                    tender.Reference,
                    tender.CapturedAt)),
            ]);

    private static ReceiptResponse ToResponse(ReceiptDocument receipt)
        => new(
            receipt.SaleNumber,
            receipt.StoreName,
            receipt.StoreAddress,
            receipt.TaxNumber,
            receipt.CompletedAt,
            receipt.TerminalId,
            receipt.OperatorName,
            [
                .. receipt.Lines.Select(line => new ReceiptLineResponse(
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.UnitPrice.Amount,
                    line.DiscountAmount.Amount,
                    line.Gross.Amount,
                    line.TaxCode)),
            ],
            [
                .. receipt.TaxLines.Select(tax => new ReceiptTaxLineResponse(
                    tax.TaxCode, tax.Net.Amount, tax.Tax.Amount)),
            ],
            receipt.Net.Amount,
            receipt.Tax.Amount,
            receipt.Gross.Amount,
            [
                .. receipt.Tenders.Select(tender => new ReceiptTenderResponse(
                    tender.Type.ToString(), tender.Amount.Amount, tender.Reference)),
            ],
            receipt.ChangeGiven.Amount,
            receipt.Gross.Currency,
            receipt.IsReprint,

            // Rendered here rather than by the caller. The layout is the part that is easy to get
            // subtly wrong — column alignment, the reprint banner, the per-rate tax block a VAT invoice
            // must carry — and having one implementation means a screen preview, an emailed copy and
            // the thermal printer cannot disagree about what the customer was handed. UTC because the
            // API's own contract is UTC (§7 rule 9); a till renders its own local time from the store's
            // timezone when it drives the printer directly.
            ReceiptRenderer.RenderText(receipt, TimeZoneInfo.Utc));

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
