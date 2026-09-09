using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Planning.Commands;
using VumaRetail.Application.Planning.Permissions;
using VumaRetail.Application.Planning.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Planning;
using VumaRetail.Domain.Planning;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Planning;

/// <summary>
/// The <c>planning</c> module's endpoints: demand history, forecasts, parameters, open-to-buy,
/// replenishment suggestions and markdown plans (Stage 15).
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. Reads are keyset-paginated where they page;
/// every write goes through a command handler.
/// </remarks>
public static class PlanningEndpoints
{
    /// <summary>Maps the planning endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaPlanning(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder planning = api.MapGroup("/planning").WithTags("Planning").RequireModule("planning");

        planning.MapGet("/forecasts", ListForecastsAsync)
            .RequirePermission(PlanningPermissions.ForecastView)
            .Produces<PageResponse<ForecastSnapshotResponse>>()
            .WithSummary("Demand forecast snapshots, newest period first.");

        planning.MapGet("/forecasts/{forecastId:guid}", GetForecastAsync)
            .RequirePermission(PlanningPermissions.ForecastView)
            .Produces<ForecastSnapshotResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one forecast snapshot by id.");

        planning.MapGet("/demand-history", ListDemandHistoryAsync)
            .RequirePermission(PlanningPermissions.HistoryView)
            .Produces<IReadOnlyList<DemandHistoryEntryResponse>>()
            .WithSummary("One SKU/location demand series in a window, oldest first.");

        planning.MapGet("/parameters", ListParametersAsync)
            .RequirePermission(PlanningPermissions.ParametersManage)
            .Produces<IReadOnlyList<ReplenishmentParameterResponse>>()
            .WithSummary("Every replenishment parameter row with its latest safety calculation.");

        planning.MapPut("/parameters", UpsertParametersAsync)
            .RequirePermission(PlanningPermissions.ParametersManage)
            .Produces<PlanningIdResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates or updates replenishment parameters for one SKU at one location.");

        planning.MapPut("/open-to-buy", SetOpenToBuyAsync)
            .RequirePermission(PlanningPermissions.OtbManage)
            .Produces<PlanningIdResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sets a month's open-to-buy budget. A warning, never a blocker.");

        planning.MapGet("/open-to-buy", GetOpenToBuyAsync)
            .RequirePermission(PlanningPermissions.OtbManage)
            .Produces<OpenToBuyStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Planned, committed live and remaining for a month. 404 when no budget is set.");

        planning.MapGet("/suggestions", ListSuggestionsAsync)
            .RequirePermission(PlanningPermissions.SuggestionView)
            .Produces<IReadOnlyList<ReplenishmentSuggestionResponse>>()
            .WithSummary("Open replenishment suggestions. Suggestions decide nothing until accepted.");

        planning.MapPost("/runs/replenishment", RunReplenishmentAsync)
            .RequirePermission(PlanningPermissions.SuggestionAccept)
            .Produces<PlanningRunResponse>()
            .WithSummary("Runs replenishment now: expires the lapsed, proposes the needed, reattempts backorders.");

        planning.MapPost("/suggestions/{suggestionId:guid}/accept", AcceptSuggestionAsync)
            .RequirePermission(PlanningPermissions.SuggestionAccept)
            .Produces<PlanningIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Accepts a suggestion, creating exactly one requisition or transfer.");

        planning.MapPost("/suggestions/{suggestionId:guid}/amend-accept", AmendAcceptSuggestionAsync)
            .RequirePermission(PlanningPermissions.SuggestionAccept)
            .Produces<PlanningIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Accepts with an amended quantity. The recommendation is preserved.");

        planning.MapPost("/suggestions/{suggestionId:guid}/reject", RejectSuggestionAsync)
            .RequirePermission(PlanningPermissions.SuggestionAccept)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rejects a suggestion. Creates nothing downstream.");

        planning.MapPost("/markdown-plans", CreateMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownPropose)
            .Produces<PlanningIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a draft markdown plan. Drafts price nothing.");

        planning.MapGet("/markdown-plans/{planId:guid}", GetMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownPropose)
            .Produces<MarkdownPlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one markdown plan with its snapshotted lines.");

        planning.MapPost("/markdown-plans/{planId:guid}/submit", SubmitMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownPropose)
            .Produces<PlanningIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Submits a draft to Stage 05 approval.");

        planning.MapPost("/markdown-plans/{planId:guid}/apply-approval", ApplyMarkdownApprovalAsync)
            .RequirePermission(PlanningPermissions.MarkdownApprove)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Applies Stage 05's verdict. Approval alone prices nothing.");

        planning.MapPost("/markdown-plans/{planId:guid}/activate", ActivateMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownApprove)
            .Produces<IReadOnlyList<PlanningIdResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Activates approved steps as Stage 10 promotions. Only this prices anything.");

        planning.MapPost("/markdown-plans/{planId:guid}/amend", AmendMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownPropose)
            .Produces<PlanningIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Amends by versioning: the old row is kept, a new draft opens.");

        planning.MapPost("/markdown-plans/{planId:guid}/cancel", CancelMarkdownPlanAsync)
            .RequirePermission(PlanningPermissions.MarkdownApprove)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Cancels a plan, retiring any live promotion through Stage 10 first.");

        return endpoints;
    }

    private static async Task<IResult> ListForecastsAsync(
        Guid? companyId,
        string? forecastMethod,
        int? limit,
        string? after,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ForecastMethod? method = string.IsNullOrWhiteSpace(forecastMethod)
            ? null
            : Enum.Parse<ForecastMethod>(forecastMethod, ignoreCase: true);

        PageResult<ForecastSnapshot> page = await dispatcher
            .QueryAsync(new ListForecastsQuery(companyId, method, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<ForecastSnapshotResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> GetForecastAsync(
        Guid forecastId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ForecastSnapshot forecast = await dispatcher
            .QueryAsync(new GetForecastQuery(forecastId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(forecast));
    }

    private static async Task<IResult> ListDemandHistoryAsync(
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        DateOnly from,
        DateOnly to,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DemandHistorySnapshot> rows = await dispatcher
            .QueryAsync(
                new ListDemandHistoryQuery(companyId, locationId, itemId, itemVariantId, from, to),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<DemandHistoryEntryResponse>>(
            [.. rows.Select(row => new DemandHistoryEntryResponse(
                row.Id, row.LocationId, row.ItemId, row.ItemVariantId,
                row.PeriodStart, row.PeriodEnd, row.TotalQuantity, row.Uom, row.GeneratedAt))]);
    }

    private static async Task<IResult> ListParametersAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplenishmentParameterSnapshot> rows = await dispatcher
            .QueryAsync(new ListParametersQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ReplenishmentParameterResponse>>(
            [.. rows.Select(row => new ReplenishmentParameterResponse(
                row.Id, row.CompanyId, row.LocationId, row.ItemId, row.ItemVariantId,
                row.ForecastMethod.ToString(), row.ServiceLevelPercent, row.LeadTimeDays,
                row.ReviewPeriodDays, row.Uom, row.SafetyStock, row.ReorderPoint, row.SafetyLowConfidence))]);
    }

    private static async Task<IResult> UpsertParametersAsync(
        UpsertReplenishmentParametersRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new UpsertReplenishmentParametersCommand(
                    request.CompanyId, request.LocationId, request.ItemId, request.ItemVariantId,
                    Enum.Parse<ForecastMethod>(request.ForecastMethod, ignoreCase: true),
                    request.ServiceLevelPercent, request.LeadTimeDays, request.ReviewPeriodDays, request.Uom),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PlanningIdResponse(id));
    }

    private static async Task<IResult> SetOpenToBuyAsync(
        SetOpenToBuyBudgetRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new SetOpenToBuyBudgetCommand(
                    request.CompanyId, request.Year, request.Month, request.CategoryCode,
                    request.PlannedAmount, request.Currency),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PlanningIdResponse(id));
    }

    private static async Task<IResult> GetOpenToBuyAsync(
        Guid companyId, int year, int month, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        OpenToBuyStatus? status = await dispatcher
            .QueryAsync(new GetOpenToBuyStatusQuery(companyId, year, month), cancellationToken)
            .ConfigureAwait(false);

        return status is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new OpenToBuyStatusResponse(
                status.CompanyId, status.Year, status.Month, status.Planned, status.Committed,
                status.Remaining, status.OverCommitted, status.Currency, status.AsAt));
    }

    private static async Task<IResult> ListSuggestionsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplenishmentSuggestionSnapshot> rows = await dispatcher
            .QueryAsync(new ListSuggestionsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ReplenishmentSuggestionResponse>>(
            [.. rows.Select(ToResponse)]);
    }

    private static async Task<IResult> RunReplenishmentAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        RunReplenishmentOutcome outcome = await dispatcher
            .SendAsync(new RunReplenishmentCommand(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PlanningRunResponse(
            outcome.SuggestionsRaised, outcome.Expired, outcome.Skipped, outcome.BackordersReallocated));
    }

    private static async Task<IResult> AcceptSuggestionAsync(
        Guid suggestionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid downstream = await dispatcher
            .SendAsync(new AcceptReplenishmentSuggestionCommand(suggestionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PlanningIdResponse(downstream));
    }

    private static async Task<IResult> AmendAcceptSuggestionAsync(
        Guid suggestionId,
        AmendAcceptSuggestionRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid downstream = await dispatcher
            .SendAsync(
                new AmendAcceptReplenishmentSuggestionCommand(suggestionId, request.AmendedQuantity),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PlanningIdResponse(downstream));
    }

    private static async Task<IResult> RejectSuggestionAsync(
        Guid suggestionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new RejectReplenishmentSuggestionCommand(suggestionId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateMarkdownPlanAsync(
        CreateMarkdownPlanRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateMarkdownPlanCommand(
                    request.CompanyId, request.Code, request.Reason,
                    request.EffectiveFrom, request.EffectiveTo,
                    [.. request.Lines.Select(line => new MarkdownLineInput(
                        line.ItemId, line.ItemVariantId, line.LocationId, line.ProposedDiscountPercent))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/planning/markdown-plans/{id:D}", new PlanningIdResponse(id));
    }

    private static async Task<IResult> GetMarkdownPlanAsync(
        Guid planId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        MarkdownPlanSnapshot plan = await dispatcher
            .QueryAsync(new GetMarkdownPlanQuery(planId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(plan));
    }

    private static async Task<IResult> SubmitMarkdownPlanAsync(
        Guid planId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid? requestId = await dispatcher
            .SendAsync(new SubmitMarkdownPlanCommand(planId), cancellationToken)
            .ConfigureAwait(false);

        return requestId is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(new PlanningIdResponse(requestId.Value));
    }

    private static async Task<IResult> ApplyMarkdownApprovalAsync(
        Guid planId,
        ApplyMarkdownApprovalRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new ApplyMarkdownApprovalCommand(planId, request.Approved), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ActivateMarkdownPlanAsync(
        Guid planId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> promotions = await dispatcher
            .SendAsync(new ActivateMarkdownPlanCommand(planId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PlanningIdResponse>>(
            [.. promotions.Select(promotionId => new PlanningIdResponse(promotionId))]);
    }

    private static async Task<IResult> AmendMarkdownPlanAsync(
        Guid planId,
        AmendMarkdownPlanRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new AmendMarkdownPlanCommand(planId, request.NewCode), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/planning/markdown-plans/{id:D}", new PlanningIdResponse(id));
    }

    private static async Task<IResult> CancelMarkdownPlanAsync(
        Guid planId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new CancelMarkdownPlanCommand(planId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static ForecastSnapshotResponse ToResponse(ForecastSnapshot forecast) => new(
        forecast.Id,
        forecast.ItemId,
        forecast.ItemVariantId,
        forecast.LocationId,
        forecast.ForecastPeriod,
        forecast.ForecastMethod.ToString(),
        forecast.Quantity,
        forecast.Mape,
        forecast.Bias,
        forecast.Version,
        forecast.GeneratedAt,
        forecast.GeneratedBy);

    private static ReplenishmentSuggestionResponse ToResponse(ReplenishmentSuggestionSnapshot suggestion) => new(
        suggestion.Id,
        suggestion.CompanyId,
        suggestion.LocationId,
        suggestion.ItemId,
        suggestion.ItemVariantId,
        suggestion.SuggestedQuantity,
        suggestion.AcceptedQuantity,
        suggestion.Uom,
        suggestion.Reason.ToString(),
        suggestion.Source.ToString(),
        suggestion.SourceCompanyId,
        suggestion.SourceLocationId,
        suggestion.Status.ToString(),
        suggestion.DownstreamDocumentId,
        suggestion.OverOpenToBuy,
        suggestion.RaisedAt,
        suggestion.ExpiresAt);

    private static MarkdownPlanResponse ToResponse(MarkdownPlanSnapshot plan) => new(
        plan.Id,
        plan.Code,
        plan.Reason,
        plan.EffectiveFrom,
        plan.EffectiveTo,
        plan.Version,
        plan.Status.ToString(),
        plan.ApprovalRequestId,
        plan.PromotionId,
        [.. plan.Lines.Select(line => new MarkdownPlanLineResponse(
            line.Id,
            line.ItemId,
            line.ItemVariantId,
            line.CurrentPrice,
            line.ProposedDiscountPercent,
            line.Currency,
            line.AbcXyz,
            line.SellThroughPercent,
            line.DaysOfSupply,
            line.PromotionId))]);
}
