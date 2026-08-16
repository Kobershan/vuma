using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Sales.Permissions;
using VumaRetail.Application.Sales.Queries;
using VumaRetail.Contracts.Sales;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Web.Api;

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

        RouteGroupBuilder priceLists = api.MapGroup("/sales/price-lists").WithTags("Sales");

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

        RouteGroupBuilder promotions = api.MapGroup("/sales/promotions").WithTags("Sales");

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

        RouteGroupBuilder prices = api.MapGroup("/sales/prices").WithTags("Sales");

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

        RouteGroupBuilder overrides = api.MapGroup("/sales/price-overrides").WithTags("Sales");

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

        RouteGroupBuilder returns = api.MapGroup("/sales/returns").WithTags("Sales");

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
}
