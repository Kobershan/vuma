using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Registry;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Sales.Commands.Analytics;
using VumaRetail.Application.Sales.Commands.Invoices;
using VumaRetail.Application.Sales.Commands.Quotes;
using VumaRetail.Application.Sales.Permissions;
using VumaRetail.Application.Sales.Queries;
using VumaRetail.Contracts.Sales;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Analytics;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Domain.Sales.Quotes;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Sales;

/// <summary>
/// The <c>sales</c> module's endpoints: price lists, promotions, price resolution, returns and the
/// price override log.
/// </summary>
/// <remarks>
/// <para>
/// R3: nothing exists in a UI before it exists here. The pricing maintenance screen, the specials
/// calendar, the returns counter and the shrinkage report are all one of these calls.
/// </para>
/// <para>
/// <b>Resolving a price is a <c>GET</c>.</b> It reads configuration and changes nothing, so it is
/// cacheable, retryable and safe for a till to call on every scan. Selling at something else is the
/// <c>POST</c> to <c>/sales/price-overrides</c>, and keeping the two apart is what stops a price check
/// entering the shrinkage report.
/// </para>
/// <para>
/// <b>A return is a document with sub-resources</b>, the shape POS uses for a sale and for the same
/// reason: goods come back one item at a time while the customer explains, and the refund is released
/// at the end by somebody who may not be the person who built it. There is no <c>PUT</c> or
/// <c>DELETE</c> on a return or its lines — a draft is cancelled and a completed one is frozen (§7 rule
/// 7). Price lists and promotions <em>are</em> mutable configuration and take <c>PUT</c>, exactly as
/// <c>catalog</c>'s items do.
/// </para>
/// </remarks>
public static class SalesEndpoints
{
    /// <summary>Maps the sales endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaSales(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder priceLists = api.MapGroup("/sales/price-lists").WithTags("Sales").RequireModule("sales");

        priceLists.MapPost("/", CreatePriceListAsync)
            .RequirePermission(SalesPermissions.PriceManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a price list.")
            .WithDescription(
                "409 when the code is already used in this tenant. A store-scoped list beats a "
                + "tenant-wide one at resolution, whatever their priorities say.");

        priceLists.MapGet("/", ListPriceListsAsync)
            .RequirePermission(SalesPermissions.PriceView)
            .Produces<IReadOnlyList<PriceListResponse>>()
            .WithSummary("Every price list, highest priority first.");

        priceLists.MapGet("/{priceListId:guid}", GetPriceListAsync)
            .RequirePermission(SalesPermissions.PriceView)
            .Produces<PriceListResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One price list, with its prices.");

        priceLists.MapPut("/{priceListId:guid}", AmendPriceListAsync)
            .RequirePermission(SalesPermissions.PriceManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Renames a price list, re-ranks it and moves its effective window.")
            .WithDescription(
                "The currency and the tax-inclusive flag are not amendable: both are properties of how "
                + "every line on the list was authored, and changing either would silently restate them.");

        priceLists.MapPut("/{priceListId:guid}/lines", SetPriceListLineAsync)
            .RequirePermission(SalesPermissions.PriceManage)
            .Produces<SalesIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sets a price, or reprices the quantity break already there.")
            .WithDescription(
                "PUT rather than POST because it is an upsert keyed on (list, item, minimum quantity) — "
                + "which is what makes a bulk price import idempotent.");

        priceLists.MapPost("/{priceListId:guid}/activate", ActivatePriceListAsync)
            .RequirePermission(SalesPermissions.PriceManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Brings a retired price list back into use.");

        priceLists.MapPost("/{priceListId:guid}/deactivate", DeactivatePriceListAsync)
            .RequirePermission(SalesPermissions.PriceManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a price list from resolution. Nothing is deleted.");

        RouteGroupBuilder promotions = api.MapGroup("/sales/promotions").WithTags("Sales").RequireModule("sales");

        promotions.MapPost("/", CreatePromotionAsync)
            .RequirePermission(SalesPermissions.PromotionManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a special. A row, never a deployment.")
            .WithDescription(
                "422 when the reward parameters do not match the declared kind — a MultibuyForAmount "
                + "with no bundle quantity is refused rather than stored as a promotion that can never "
                + "fire.");

        promotions.MapGet("/", ListPromotionsAsync)
            .RequirePermission(SalesPermissions.PriceView)
            .Produces<IReadOnlyList<PromotionResponse>>()
            .WithSummary("Every promotion, highest priority first.");

        promotions.MapGet("/effective", ListEffectivePromotionsAsync)
            .RequirePermission(SalesPermissions.PriceView)
            .Produces<IReadOnlyList<PromotionResponse>>()
            .WithSummary("The specials a store is running on a given day.")
            .WithDescription(
                "Answers the day, not the minute: a happy-hour special appears here outside its time "
                + "window. What would fire on a basket right now is the price-resolution call.");

        promotions.MapPut("/{promotionId:guid}", AmendPromotionAsync)
            .RequirePermission(SalesPermissions.PromotionManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Renames a promotion, re-ranks it and moves its windows.")
            .WithDescription(
                "The reward parameters are not amendable. Turning '3 for R50' into '20% off' on the "
                + "same row would rewrite what every already-priced basket was told it was getting; a "
                + "changed offer is a new promotion.");

        promotions.MapPost("/{promotionId:guid}/lines", AddPromotionLineAsync)
            .RequirePermission(SalesPermissions.PromotionManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds an item, variant or category for the promotion to apply to.")
            .WithDescription("A promotion with no lines applies to everything — that is a clearance.");

        promotions.MapPost("/{promotionId:guid}/activate", ActivatePromotionAsync)
            .RequirePermission(SalesPermissions.PromotionManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Brings a retired promotion back into use.");

        promotions.MapPost("/{promotionId:guid}/deactivate", DeactivatePromotionAsync)
            .RequirePermission(SalesPermissions.PromotionManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a promotion. Nothing is deleted.");

        RouteGroupBuilder prices = api.MapGroup("/sales/prices").WithTags("Sales").RequireModule("sales");

        prices.MapGet("/resolve", ResolvePriceAsync)
            .RequirePermission(SalesPermissions.PriceView)
            .Produces<PriceResolutionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("What something should sell for, and why.")
            .WithDescription(
                "The unit price comes back unrounded on purpose: pass it and the discount straight "
                + "into POST /pos/sales/{saleId}/lines so the rounding happens once, on the extended "
                + "amount. 404 when nothing prices the item; 422 when the winning list is denominated "
                + "in another currency, which is refused rather than converted.");

        RouteGroupBuilder overrides = api.MapGroup("/sales/price-overrides").WithTags("Sales").RequireModule("sales");

        overrides.MapPost("/", RecordPriceOverrideAsync)
            .RequirePermission(SalesPermissions.PriceOverride)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records that something was sold off-price, with a reason.")
            .WithDescription(
                "Records; it does not refuse. The permission is the control, and a till that refused "
                + "here would only teach the floor to stop recording overrides.");

        overrides.MapGet("/", ListPriceOverridesAsync)
            .RequirePermission(SalesPermissions.ReturnView)
            .Produces<IReadOnlyList<PriceOverrideResponse>>()
            .WithSummary("Price overrides in a period, newest first.")
            .WithDescription(
                "The shrinkage report. One cashier and one item every Friday is a pattern; the same "
                + "events read one at a time are a series of reasonable decisions.");

        RouteGroupBuilder returns = api.MapGroup("/sales/returns").WithTags("Sales").RequireModule("sales");

        returns.MapPost("/", CreateSalesReturnAsync)
            .RequirePermission(SalesPermissions.ReturnRaise)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Raises a return against a completed sale.")
            .WithDescription(
                "422 on a sale that is open, parked or voided. A return is a new document and never an "
                + "edit of the receipt the customer is holding.");

        returns.MapGet("/", ListReturnsForPeriodAsync)
            .RequirePermission(SalesPermissions.ReturnView)
            .Produces<IReadOnlyList<SalesReturnResponse>>()
            .WithSummary("Returns completed in a period, newest first.");

        returns.MapGet("/by-sale/{saleId:guid}", ListSaleReturnsAsync)
            .RequirePermission(SalesPermissions.ReturnView)
            .Produces<IReadOnlyList<SalesReturnResponse>>()
            .WithSummary("Every return raised against one sale, oldest first.")
            .WithDescription(
                "What a cashier checks before accepting goods back. The authoritative answer is the "
                + "one the aggregate gives when the line is added; this is the same answer a person "
                + "can read.");

        returns.MapGet("/{salesReturnId:guid}", GetSalesReturnAsync)
            .RequirePermission(SalesPermissions.ReturnView)
            .Produces<SalesReturnResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One return, with its lines.");

        returns.MapPost("/{salesReturnId:guid}/lines", AddSalesReturnLineAsync)
            .RequirePermission(SalesPermissions.ReturnRaise)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Puts one of the original sale's lines onto the return.")
            .WithDescription(
                "Refunded at what was actually charged, with the tax taken pro-rata off the original "
                + "line's stored tax (ADR-075). 422 when the cumulative returned quantity would exceed "
                + "what was sold.");

        returns.MapPost("/{salesReturnId:guid}/complete", CompleteSalesReturnAsync)
            .RequirePermission(SalesPermissions.ReturnComplete)
            .Produces<SalesReturnCompletionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes the return: freezes it, puts stock back and raises its financial event.")
            .WithDescription(
                "A line whose stock the ledger refuses does not fail the refund — it completes with "
                + "stockReturnsRefused above zero (ADR-073).");

        returns.MapPost("/{salesReturnId:guid}/cancel", CancelSalesReturnAsync)
            .RequirePermission(SalesPermissions.ReturnRaise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a draft return. Nothing was refunded and nothing moved.");

        RouteGroupBuilder quotes = api.MapGroup("/sales/quotes").WithTags("Sales").RequireModule("sales");

        quotes.MapPost("/", CreateQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a draft quote: a priced basket with an expiry.")
            .WithDescription(
                "Promises price, never stock. Add lines, issue to lock the prices, accept inside the "
                + "validity window, then convert into an order or a sale.");

        quotes.MapPost("/{quoteId:guid}/lines", AddQuoteLineAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Snapshots one priced line onto a draft quote.")
            .WithDescription(
                "Price, tax and pack size freeze at this instant (ADR-074, ADR-075, ADR-112). A later "
                + "price-list change never reprices a live quote.");

        quotes.MapGet("/{quoteId:guid}", GetQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteView)
            .Produces<QuoteResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One quote, with its snapshotted lines.");

        quotes.MapPost("/{quoteId:guid}/issue", IssueQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Issues a quote, locking its prices.");

        quotes.MapPost("/{quoteId:guid}/accept", AcceptQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Accepts a quote inside its validity window.");

        quotes.MapPost("/{quoteId:guid}/reject", RejectQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rejects a quote.");

        quotes.MapPost("/{quoteId:guid}/expire", ExpireQuoteAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Expires a quote before its validity period.");

        quotes.MapPost("/{quoteId:guid}/convert-order", ConvertQuoteToOrderAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces<SalesIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Converts an accepted quote into a Stage 14 order.")
            .WithDescription(
                "Marks the quote converted so one acceptance can never become two orders. Stage 14 "
                + "builds the real document from the quote's snapshots.");

        quotes.MapPost("/{quoteId:guid}/convert-sale", ConvertQuoteToSaleAsync)
            .RequirePermission(SalesPermissions.QuoteManage)
            .Produces<SalesIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Converts an accepted quote into a Stage 09 till sale.")
            .WithDescription(
                "Marks the quote converted so one acceptance can never become two sales. The till "
                + "builds the real document from the quote's snapshots.");

        quotes.MapGet("/", ListQuotesAsync)
            .RequirePermission(SalesPermissions.QuoteView)
            .Produces<IReadOnlyList<QuoteResponse>>()
            .WithSummary("Quotes, optionally narrowed by status and customer.");

        RouteGroupBuilder invoices = api.MapGroup("/sales/invoices").WithTags("Sales").RequireModule("sales");

        invoices.MapPost("/", CreateInvoiceAsync)
            .RequirePermission(SalesPermissions.InvoiceManage)
            .Produces<SalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a draft invoice in one company's books from frozen lines.")
            .WithDescription(
                "The lines arrive snapshotted — price, tax, pack size — from the order, sale or quote "
                + "being documented. Nothing is resolved or re-derived here.");

        invoices.MapPost("/{invoiceId:guid}/finalize", FinalizeInvoiceAsync)
            .RequirePermission(SalesPermissions.InvoiceManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Finalizes a draft: freezes it and posts it to the ledger. Irreversible.");

        invoices.MapPost("/{invoiceId:guid}/cancel", CancelInvoiceAsync)
            .RequirePermission(SalesPermissions.InvoiceManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a draft invoice. Posted invoices take the credit-note path.");

        invoices.MapGet("/{invoiceId:guid}", GetInvoiceAsync)
            .RequirePermission(SalesPermissions.InvoiceView)
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One invoice, with its frozen lines and pack sizes.");

        invoices.MapGet("/", ListInvoicesAsync)
            .RequirePermission(SalesPermissions.InvoiceView)
            .Produces<IReadOnlyList<InvoiceResponse>>()
            .WithSummary("One company's invoices, newest first. Company-scoped: another company's rows never appear.");

        api.MapPost("/orders/{orderId:guid}/generate-invoices", GenerateInvoicesForOrderAsync)
            .WithTags("Sales")
            .RequireModule("sales")
            .RequirePermission(SalesPermissions.InvoiceManage)
            .Produces<InvoiceIdsResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Generates the invoices for one fulfilled order: one per supplying company.")
            .WithDescription(
                "One order, N invoices (ADR-102). Each segment posts in its own company's database "
                + "under the shared group reference; the response carries one invoice id per company.");

        RouteGroupBuilder analytics = api.MapGroup("/sales/analytics").WithTags("Sales").RequireModule("sales");

        analytics.MapGet("/", GetSalesAnalyticsAsync)
            .RequirePermission(SalesPermissions.AnalyticsView)
            .Produces<IReadOnlyList<SalesAnalyticsResponse>>()
            .WithSummary("Company-scoped sales analytics: daily facts rolled to any period.");

        analytics.MapGet("/group", GetGroupAnalyticsAsync)
            .RequirePermission(SalesPermissions.AnalyticsView)
            .Produces<IReadOnlyList<SalesAnalyticsResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Group-level analytics. Stale by design; never blocks trade (ADR-119).")
            .WithDescription(
                "Additionally requires registry.analytics.view, checked in the handler. Every figure "
                + "carries AsAt; stale contributors are disclosed, never silently summed.");

        analytics.MapPost("/rebuild", RebuildAnalyticsAsync)
            .RequirePermission(SalesPermissions.AnalyticsView)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rebuilds the sales read models for a period from posted invoices.")
            .WithDescription(
                "Planning figures only. A rebuild never blocks trade and never feeds a commit.");

        return endpoints;
    }

    private static async Task<IResult> CreatePriceListAsync(
        CreatePriceListRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PriceListKind kind = ParseEnum<PriceListKind>(
            request.Kind, nameof(request.Kind), nameof(CreatePriceListCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreatePriceListCommand(
                    request.Code,
                    request.Name,
                    request.Currency,
                    kind,
                    request.PricesIncludeTax,
                    request.Priority,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/price-lists/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> ListPriceListsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken, bool includeInactive = false)
    {
        IReadOnlyList<PriceList> lists = await dispatcher
            .QueryAsync(new ListPriceListsQuery(includeInactive), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PriceListResponse>>([.. lists.Select(ToResponse)]);
    }

    private static async Task<IResult> GetPriceListAsync(
        Guid priceListId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PriceList list = await dispatcher
            .QueryAsync(new GetPriceListQuery(priceListId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(list));
    }

    private static async Task<IResult> AmendPriceListAsync(
        Guid priceListId,
        AmendPriceListRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new AmendPriceListCommand(
                    priceListId, request.Name, request.Priority, request.EffectiveFrom, request.EffectiveTo),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetPriceListLineAsync(
        Guid priceListId,
        SetPriceListLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new SetPriceListLineCommand(
                    priceListId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Money(request.UnitPrice, request.Currency),
                    request.MinimumQuantity),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesIdResponse(id));
    }

    private static async Task<IResult> ActivatePriceListAsync(
        Guid priceListId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ActivatePriceListCommand(priceListId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeactivatePriceListAsync(
        Guid priceListId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DeactivatePriceListCommand(priceListId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreatePromotionAsync(
        CreatePromotionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PromotionKind kind = ParseEnum<PromotionKind>(
            request.Kind, nameof(request.Kind), nameof(CreatePromotionCommand));

        Money? reward = request.RewardAmount is { } amount
            ? new Money(amount, RequireCurrency(request.Currency))
            : null;

        Guid id = await dispatcher
            .SendAsync(
                new CreatePromotionCommand(
                    request.Code,
                    request.Name,
                    kind,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.DiscountPercentage,
                    reward,
                    request.RequiredQuantity,
                    request.FreeQuantity,
                    request.Priority,
                    request.IsExclusive,
                    ParseDays(request.Days),
                    request.StartsAt,
                    request.EndsAt,
                    request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/promotions/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> ListPromotionsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken, bool includeInactive = false)
    {
        IReadOnlyList<Promotion> promotions = await dispatcher
            .QueryAsync(new ListPromotionsQuery(includeInactive), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PromotionResponse>>([.. promotions.Select(ToResponse)]);
    }

    private static async Task<IResult> ListEffectivePromotionsAsync(
        IDispatcher dispatcher,
        ITenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken,
        Guid? storeId = null,
        DateOnly? onDate = null)
    {
        IReadOnlyList<Promotion> promotions = await dispatcher
            .QueryAsync(
                new ListEffectivePromotionsQuery(
                    storeId ?? tenant.StoreId,
                    onDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PromotionResponse>>([.. promotions.Select(ToResponse)]);
    }

    private static async Task<IResult> AmendPromotionAsync(
        Guid promotionId,
        AmendPromotionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new AmendPromotionCommand(
                    promotionId,
                    request.Name,
                    request.Priority,
                    request.IsExclusive,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    ParseDays(request.Days),
                    request.StartsAt,
                    request.EndsAt),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AddPromotionLineAsync(
        Guid promotionId,
        AddPromotionLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddPromotionLineCommand(
                    promotionId, request.ItemId, request.ItemVariantId, request.CategoryCode),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/promotions/{promotionId}", new SalesIdResponse(id));
    }

    private static async Task<IResult> ActivatePromotionAsync(
        Guid promotionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ActivatePromotionCommand(promotionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeactivatePromotionAsync(
        Guid promotionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DeactivatePromotionCommand(promotionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResolvePriceAsync(
        IDispatcher dispatcher,
        ITenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken,
        Guid? itemId = null,
        Guid? itemVariantId = null,
        string? categoryCode = null,
        decimal quantity = 1m,
        Guid? storeId = null,
        DateOnly? onDate = null,
        TimeOnly? atTime = null,
        string currency = "ZAR")
    {
        // The date and time default to now from IClock rather than from DateTime.UtcNow
        // (CONVENTIONS.md §6), which is also what makes a happy-hour promotion testable: a test moves
        // the clock instead of waiting until eight in the evening.
        DateTimeOffset now = clock.UtcNow;

        PriceResolution resolution = await dispatcher
            .QueryAsync(
                new ResolvePriceQuery(new PriceResolutionRequest(
                    itemId,
                    itemVariantId,
                    categoryCode,
                    quantity,
                    storeId ?? tenant.StoreId,
                    onDate ?? DateOnly.FromDateTime(now.UtcDateTime),
                    atTime ?? TimeOnly.FromDateTime(now.UtcDateTime),
                    currency)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PriceResolutionResponse(
            resolution.PriceListId,
            resolution.PriceListCode,
            resolution.PriceListLineId,
            resolution.PricesIncludeTax,
            resolution.UnitPrice.Amount,
            resolution.ExtendedPrice.Amount,
            resolution.DiscountAmount.Amount,
            resolution.NetPayable.Amount,
            resolution.NetPayable.Currency,
            [
                .. resolution.Promotions.Select(promotion => new AppliedPromotionResponse(
                    promotion.PromotionId,
                    promotion.Code,
                    promotion.Name,
                    promotion.Kind.ToString(),
                    promotion.DiscountAmount.Amount,
                    promotion.WasClamped)),
            ],
            resolution.Explanation));
    }

    private static async Task<IResult> RecordPriceOverrideAsync(
        RecordPriceOverrideRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordPriceOverrideCommand(
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    new Money(request.ResolvedUnitPrice, request.Currency),
                    new Money(request.ActualUnitPrice, request.Currency),
                    request.Reason,
                    request.SaleId,
                    request.SaleLineId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/price-overrides/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> ListPriceOverridesAsync(
        IDispatcher dispatcher,
        IClock clock,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? operatorUserId = null,
        int limit = 100)
    {
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<PriceOverrideLog> entries = await dispatcher
            .QueryAsync(
                new ListPriceOverridesQuery(
                    from ?? today.AddDays(-30), to ?? today, operatorUserId, limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PriceOverrideResponse>>(
        [
            .. entries.Select(entry => new PriceOverrideResponse(
                entry.Id,
                entry.SaleId,
                entry.SaleLineId,
                entry.ItemId,
                entry.ItemVariantId,
                entry.OperatorUserId,
                entry.Quantity.Value,
                entry.Quantity.UnitOfMeasure,
                entry.ResolvedUnitPrice.Amount,
                entry.ActualUnitPrice.Amount,
                entry.Variance.Amount,
                entry.ActualUnitPrice.Currency,
                entry.Reason,
                entry.OccurredAt)),
        ]);
    }

    private static async Task<IResult> CreateSalesReturnAsync(
        CreateSalesReturnRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TenderType refundTender = ParseEnum<TenderType>(
            request.RefundTenderType, nameof(request.RefundTenderType), nameof(CreateSalesReturnCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateSalesReturnCommand(request.SaleId, request.Reason, refundTender),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/returns/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> ListReturnsForPeriodAsync(
        IDispatcher dispatcher,
        IClock clock,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 100)
    {
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<SalesReturn> returns = await dispatcher
            .QueryAsync(
                new ListReturnsForPeriodQuery(from ?? today.AddDays(-30), to ?? today, limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SalesReturnResponse>>([.. returns.Select(ToResponse)]);
    }

    private static async Task<IResult> ListSaleReturnsAsync(
        Guid saleId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<SalesReturn> returns = await dispatcher
            .QueryAsync(new ListSaleReturnsQuery(saleId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SalesReturnResponse>>([.. returns.Select(ToResponse)]);
    }

    private static async Task<IResult> GetSalesReturnAsync(
        Guid salesReturnId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SalesReturn salesReturn = await dispatcher
            .QueryAsync(new GetSalesReturnQuery(salesReturnId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(salesReturn));
    }

    private static async Task<IResult> AddSalesReturnLineAsync(
        Guid salesReturnId,
        AddSalesReturnLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddSalesReturnLineCommand(
                    salesReturnId,
                    request.SaleLineId,
                    new Quantity(request.Quantity, request.UnitOfMeasure)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/returns/{salesReturnId}", new SalesIdResponse(id));
    }

    private static async Task<IResult> CompleteSalesReturnAsync(
        Guid salesReturnId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SalesReturnCompletionResult result = await dispatcher
            .SendAsync(new CompleteSalesReturnCommand(salesReturnId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesReturnCompletionResponse(
            result.SalesReturnId,
            result.ReturnNumber,
            result.Net.Amount,
            result.Tax.Amount,
            result.Gross.Amount,
            result.Gross.Currency,
            result.RefundTenderType.ToString(),
            result.StockReturnsRefused));
    }

    private static async Task<IResult> CancelSalesReturnAsync(
        Guid salesReturnId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new CancelSalesReturnCommand(salesReturnId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static PriceListResponse ToResponse(PriceList list)
        => new(
            list.Id,
            list.Code,
            list.Name,
            list.Currency,
            list.Kind.ToString(),
            list.PricesIncludeTax,
            list.Priority,
            list.EffectiveFrom,
            list.EffectiveTo,
            list.IsActive,
            list.StoreId,
            [
                .. list.Lines
                    .OrderBy(line => line.MinimumQuantity)
                    .Select(line => new PriceListLineResponse(
                        line.Id,
                        line.ItemId,
                        line.ItemVariantId,
                        line.UnitPrice.Amount,
                        line.MinimumQuantity)),
            ]);

    private static PromotionResponse ToResponse(Promotion promotion)
        => new(
            promotion.Id,
            promotion.Code,
            promotion.Name,
            promotion.Kind.ToString(),
            promotion.DiscountPercentage,
            promotion.RewardAmount,
            promotion.RewardCurrency,
            promotion.RequiredQuantity,
            promotion.FreeQuantity,
            promotion.EffectiveFrom,
            promotion.EffectiveTo,
            promotion.Days?.ToString(),
            promotion.StartsAt,
            promotion.EndsAt,
            promotion.Priority,
            promotion.IsExclusive,
            promotion.IsActive,
            promotion.StoreId,
            [
                .. promotion.Lines.Select(line => new PromotionLineResponse(
                    line.Id, line.ItemId, line.ItemVariantId, line.CategoryCode)),
            ]);

    private static SalesReturnResponse ToResponse(SalesReturn salesReturn)
        => new(
            salesReturn.Id,
            salesReturn.ReturnNumber,
            salesReturn.SaleId,
            salesReturn.Status.ToString(),
            salesReturn.LocationId,
            salesReturn.CustomerId,
            salesReturn.Currency,
            salesReturn.Reason,
            salesReturn.RefundTenderType.ToString(),
            salesReturn.AuthorisedByUserId,
            salesReturn.Net.Amount,
            salesReturn.Tax.Amount,
            salesReturn.Gross.Amount,
            salesReturn.RaisedAt,
            salesReturn.CompletedAt,
            salesReturn.CancelledAt,
            [
                .. salesReturn.Lines.Select(line => new SalesReturnLineResponse(
                    line.Id,
                    line.SaleLineId,
                    line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.UnitOfMeasure,
                    line.OriginalQuantity.Value,
                    line.PreviouslyReturnedQuantity,
                    line.UnitPrice.Amount,
                    line.TaxCode,
                    line.Net.Amount,
                    line.Tax.Amount,
                    line.Gross.Amount,
                    line.StockReturn.ToString(),
                    line.StockReturnNote)),
            ]);

    /// <summary>
    /// Parses the flags enum a promotion's day restriction is, accepting the comma-separated form
    /// <c>Monday, Wednesday, Friday</c> as well as the named sets <c>Weekdays</c> and <c>Weekend</c>.
    /// </summary>
    private static PromotionDays? ParseDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse(value, ignoreCase: true, out PromotionDays parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            nameof(CreatePromotionCommand),
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Days"] =
                [
                    $"'{value}' is not a set of days. Use one or more of "
                    + $"{string.Join(", ", Enum.GetNames<PromotionDays>())}, comma-separated.",
                ],
            });
    }

    /// <summary>
    /// A monetary reward with no currency is the exact bug §7 rule 4 exists to prevent, so it is
    /// refused at the edge rather than defaulted.
    /// </summary>
    private static string RequireCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency)
            ? throw new ValidationFailedException(
                nameof(CreatePromotionCommand),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Currency"] = ["A promotion with a reward amount must state its currency."],
                })
            : currency;

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

    private static async Task<IResult> CreateQuoteAsync(
        CreateQuoteRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        Guid id = await dispatcher
            .SendAsync(
                new CreateQuoteCommand(
                    request.CustomerId, request.Currency, request.ValidUntil,
                    request.GroupId, request.CompanyId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/quotes/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> AddQuoteLineAsync(
        Guid quoteId,
        AddQuoteLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new AddQuoteLineCommand(
                    quoteId, request.ItemId, request.ItemVariantId,
                    request.Quantity, request.Uom, request.PriceListId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/quotes/{quoteId}", new SalesIdResponse(id));
    }

    private static async Task<IResult> GetQuoteAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Quote quote = await dispatcher
            .QueryAsync(new GetQuoteQuery(quoteId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(quote));
    }

    private static async Task<IResult> IssueQuoteAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new IssueQuoteCommand(quoteId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AcceptQuoteAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new AcceptQuoteCommand(quoteId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RejectQuoteAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RejectQuoteCommand(quoteId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ExpireQuoteAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ExpireQuoteCommand(quoteId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ConvertQuoteToOrderAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new ConvertQuoteToOrderCommand(quoteId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesIdResponse(id));
    }

    private static async Task<IResult> ConvertQuoteToSaleAsync(
        Guid quoteId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new ConvertQuoteToSaleCommand(quoteId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SalesIdResponse(id));
    }

    private static async Task<IResult> ListQuotesAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? status = null,
        Guid? customerId = null)
    {
        QuoteStatus? parsedStatus = status is null
            ? null
            : ParseEnum<QuoteStatus>(status, nameof(status), nameof(ListQuotesQuery));

        IReadOnlyList<Quote> quotes = await dispatcher
            .QueryAsync(new ListQuotesQuery(parsedStatus, customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<QuoteResponse>>([.. quotes.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateInvoiceAsync(
        CreateInvoiceRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        InvoiceSourceType sourceType = ParseEnum<InvoiceSourceType>(
            request.SourceType, nameof(request.SourceType), nameof(CreateInvoiceCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateInvoiceCommand(
                    request.CustomerId,
                    request.Currency,
                    request.SourceDocumentId,
                    sourceType,
                    [.. request.Lines.Select(ToInput)],
                    request.GroupDocumentRef,
                    request.CompanyId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/sales/invoices/{id}", new SalesIdResponse(id));
    }

    private static async Task<IResult> FinalizeInvoiceAsync(
        Guid invoiceId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new FinalizeInvoiceCommand(invoiceId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelInvoiceAsync(
        Guid invoiceId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelInvoiceCommand(invoiceId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetInvoiceAsync(
        Guid invoiceId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Invoice invoice = await dispatcher
            .QueryAsync(new GetInvoiceQuery(invoiceId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(invoice));
    }

    private static async Task<IResult> ListInvoicesAsync(
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken,
        Guid? companyId = null)
    {
        Guid effective = companyId ?? company.CompanyId ?? Guid.Empty;
        if (effective == Guid.Empty)
        {
            throw new ValidationFailedException(
                nameof(ListInvoicesQuery),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["Listing invoices needs its company."],
                });
        }

        IReadOnlyList<Invoice> invoices = await dispatcher
            .QueryAsync(new ListInvoicesQuery(effective), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<InvoiceResponse>>([.. invoices.Select(ToResponse)]);
    }

    private static async Task<IResult> GenerateInvoicesForOrderAsync(
        Guid orderId,
        GenerateInvoicesRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        InvoiceSourceType sourceType = ParseEnum<InvoiceSourceType>(
            request.SourceType, nameof(request.SourceType), nameof(GenerateInvoicesFromOrderCommand));

        IReadOnlyList<Guid> ids = await dispatcher
            .SendAsync(
                new GenerateInvoicesFromOrderCommand(
                    orderId,
                    request.SourceDocumentNumber,
                    sourceType,
                    request.OrderingCompanyId,
                    request.CustomerId,
                    request.Currency,
                    [.. request.Segments.Select(segment => new InvoiceCompanySegment(
                        segment.CompanyId,
                        [.. segment.Lines.Select(ToInput)]))],
                    request.GroupDocumentRef,
                    request.IdempotencyKey,
                    request.InitiatedBy),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/sales/invoices/{ids.First()}", new InvoiceIdsResponse([.. ids]));
    }

    private static async Task<IResult> GetSalesAnalyticsAsync(
        IDispatcher dispatcher,
        ICompanyContext company,
        IClock clock,
        CancellationToken cancellationToken,
        Guid? companyId = null,
        string period = nameof(AnalyticsPeriod.Daily),
        DateOnly? from = null,
        DateOnly? to = null)
    {
        Guid effective = companyId ?? company.CompanyId ?? Guid.Empty;
        if (effective == Guid.Empty)
        {
            throw new ValidationFailedException(
                nameof(GetSalesAnalyticsQuery),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["Company analytics need their company."],
                });
        }

        if (company.CompanyId is { } bound && bound != effective)
        {
            throw AnalyticsRuleException.CompanyNotAuthorized(effective);
        }

        AnalyticsPeriod parsedPeriod = ParseEnum<AnalyticsPeriod>(
            period, nameof(period), nameof(GetSalesAnalyticsQuery));

        IReadOnlyList<SalesAnalytics> result = await dispatcher
            .QueryAsync(
                new GetSalesAnalyticsQuery(
                    effective,
                    parsedPeriod,
                    StartOfDay(from, clock.UtcNow),
                    StartOfDay(to, clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SalesAnalyticsResponse>>([.. result.Select(ToResponse)]);
    }

    private static async Task<IResult> GetGroupAnalyticsAsync(
        IDispatcher dispatcher,
        ITenantContext tenant,
        IPrincipalAccessor principal,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken cancellationToken,
        string period = nameof(AnalyticsPeriod.Daily),
        DateOnly? from = null,
        DateOnly? to = null)
    {
        // The route gate carries sales.analytics.view; the group scope additionally needs
        // registry.analytics.view, checked here because the endpoint cannot know which applies
        // until it runs — the same shape as availability's groupScope (ADR-137's escape hatch).
        Guid userId = ParseUserId(principal.Principal);
        bool allowed = await permissions.HasPermissionAsync(
                userId, tenant.StoreId, RegistryPermissions.GroupAnalyticsView, cancellationToken)
            .ConfigureAwait(false);

        if (!allowed)
        {
            throw SalesForbiddenException.GroupAnalyticsNotPermitted();
        }

        AnalyticsPeriod parsedPeriod = ParseEnum<AnalyticsPeriod>(
            period, nameof(period), nameof(GetGroupAnalyticsQuery));

        IReadOnlyList<SalesAnalytics> result = await dispatcher
            .QueryAsync(
                new GetGroupAnalyticsQuery(
                    parsedPeriod,
                    StartOfDay(from, clock.UtcNow),
                    StartOfDay(to, clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SalesAnalyticsResponse>>([.. result.Select(ToResponse)]);
    }

    private static async Task<IResult> RebuildAnalyticsAsync(
        RebuildAnalyticsRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new RebuildAnalyticsCommand(
                    request.CompanyId,
                    new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    new DateTimeOffset(request.To.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Accepted("/api/v1/sales/analytics");
    }

    private static InvoiceLineInput ToInput(CreateInvoiceLineRequest line) => new(
        line.ItemId,
        line.ItemVariantId,
        line.Quantity,
        line.Uom,
        line.UnitPrice,
        line.DiscountAmount,
        line.TaxAmount,
        line.PackSizeDescription,
        line.PriceListId,
        line.SourceLineId);

    private static QuoteResponse ToResponse(Quote quote) => new(
        quote.Id,
        quote.QuoteNumber,
        quote.CustomerId,
        quote.Currency,
        quote.Status.ToString(),
        quote.ValidUntil,
        quote.Net.Amount,
        quote.Tax.Amount,
        quote.Gross.Amount,
        quote.GroupId,
        [
            .. quote.Lines.Select(line => new QuoteLineResponse(
                line.Id,
                line.ItemId,
                line.ItemVariantId,
                line.QuantityValue,
                line.QuantityUom,
                line.UnitPrice.Amount,
                line.DiscountAmount.Amount,
                line.TaxAmount.Amount,
                line.Net.Amount,
                line.PackSizeDescription,
                line.PromotionsSummary)),
        ]);

    private static InvoiceResponse ToResponse(Invoice invoice) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.CompanyId ?? Guid.Empty,
        invoice.SourceDocumentRef,
        invoice.SourceDocumentType.ToString(),
        invoice.CustomerId,
        invoice.Status.ToString(),
        invoice.Net.Amount,
        invoice.Tax.Amount,
        invoice.Gross.Amount,
        invoice.PostedAt,
        invoice.GroupDocumentRef,
        [
            .. invoice.Lines.Select(line => new InvoiceLineResponse(
                line.Id,
                line.ItemId,
                line.ItemVariantId,
                line.QuantityValue,
                line.QuantityUom,
                line.UnitPrice.Amount,
                line.DiscountAmount.Amount,
                line.TaxAmount.Amount,
                line.Net.Amount,
                line.PackSizeDescription)),
        ]);

    private static SalesAnalyticsResponse ToResponse(SalesAnalytics analytics) => new(
        analytics.CompanyId ?? Guid.Empty,
        analytics.Period.ToString(),
        analytics.PeriodStart,
        analytics.PeriodEnd,
        analytics.CategoryCode,
        analytics.Channel,
        analytics.Revenue.Amount,
        analytics.CostOfSale.Amount,
        analytics.Margin.Amount,
        analytics.TaxLiability.Amount,
        analytics.OrderCount,
        analytics.LineCount,
        analytics.AsAt,
        analytics.IsStale);

    private static DateTimeOffset StartOfDay(DateOnly? day, DateTimeOffset now)
    {
        DateOnly effective = day ?? DateOnly.FromDateTime(now.UtcDateTime);
        return new DateTimeOffset(effective.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static Guid ParseUserId(string principal)
    {
        const string prefix = "user:";
        if (principal.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(principal[prefix.Length..], out Guid userId)
            && userId != Guid.Empty)
        {
            return userId;
        }

        throw SalesForbiddenException.GroupAnalyticsNotPermitted();
    }

    /// <summary>
    /// Binds the acting company for this request scope when the caller named one explicitly.
    /// </summary>
    /// <remarks>
    /// The interim selection mechanism until per-request company middleware lands (the Stage 06c
    /// follow-up): a request that names a different company than the scope already holds is
    /// refused loudly rather than silently switching ledgers mid-call.
    /// </remarks>
    private static void BindCompany(ICompanyContext company, Guid? companyId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (companyId is null || companyId == Guid.Empty)
        {
            return;
        }

        if (company.CompanyId is { } bound && bound != companyId)
        {
            throw new ValidationFailedException(
                nameof(companyId),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["The request names a different company than the scope already holds."],
                });
        }

        if (company.CompanyId is null)
        {
            company.SetCompany(companyId.Value);
        }
    }
}
