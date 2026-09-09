using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning.Queries;

/// <summary>One forecast snapshot, as read back out.</summary>
public sealed record ForecastSnapshot(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid LocationId,
    DateOnly ForecastPeriod,
    ForecastMethod ForecastMethod,
    decimal Quantity,
    decimal Mape,
    decimal? Bias,
    string Version,
    DateTimeOffset GeneratedAt,
    Guid? GeneratedBy);

/// <summary>A page of forecast snapshots, newest period first.</summary>
public sealed record ListForecastsQuery(
    Guid? CompanyId, ForecastMethod? ForecastMethod, int? Limit, string? After)
    : IQuery<PageResult<ForecastSnapshot>>;

/// <summary>Reads the page.</summary>
public sealed class ListForecastsQueryHandler(IDemandForecastRepository forecasts)
    : IQueryHandler<ListForecastsQuery, PageResult<ForecastSnapshot>>
{
    /// <inheritdoc />
    public async Task<PageResult<ForecastSnapshot>> HandleAsync(
        ListForecastsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<DemandForecast> items, bool hasMore) = await forecasts
            .ListPageAsync(
                query.CompanyId, query.ForecastMethod, KeysetCursor.TryDecode(query.After), limit,
                cancellationToken)
            .ConfigureAwait(false);

        string? cursor = hasMore && items.Count > 0
            ? new KeysetCursor(
                items[^1].ForecastPeriod.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                items[^1].Id).Encode()
            : null;

        return new PageResult<ForecastSnapshot>(
            [.. items.Select(GetForecastQueryHandler.ToSnapshot)], cursor, hasMore);
    }
}

/// <summary>Reads one forecast snapshot by id.</summary>
public sealed record GetForecastQuery(Guid ForecastId) : IQuery<ForecastSnapshot>;

/// <summary>Reads the snapshot.</summary>
public sealed class GetForecastQueryHandler(IDemandForecastRepository forecasts)
    : IQueryHandler<GetForecastQuery, ForecastSnapshot>
{
    /// <inheritdoc />
    public async Task<ForecastSnapshot> HandleAsync(
        GetForecastQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        DemandForecast forecast = await forecasts
            .FindAsync(query.ForecastId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("demand forecast", query.ForecastId);

        return ToSnapshot(forecast);
    }

    internal static ForecastSnapshot ToSnapshot(DemandForecast forecast) => new(
        forecast.Id,
        forecast.ItemId,
        forecast.ItemVariantId,
        forecast.LocationId,
        forecast.ForecastPeriod,
        forecast.ForecastMethod,
        forecast.Quantity,
        forecast.Mape,
        forecast.Bias,
        forecast.Version,
        forecast.GeneratedAt,
        forecast.GeneratedBy);
}

/// <summary>One demand history row, as read back out.</summary>
public sealed record DemandHistorySnapshot(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalQuantity,
    string Uom,
    DateTimeOffset GeneratedAt);

/// <summary>Lists one SKU/location history series in a window, oldest first.</summary>
public sealed record ListDemandHistoryQuery(
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    DateOnly From,
    DateOnly To) : IQuery<IReadOnlyList<DemandHistorySnapshot>>;

/// <summary>Reads the series.</summary>
public sealed class ListDemandHistoryQueryHandler(IDemandHistoryRepository history)
    : IQueryHandler<ListDemandHistoryQuery, IReadOnlyList<DemandHistorySnapshot>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DemandHistorySnapshot>> HandleAsync(
        ListDemandHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<DemandHistory> rows = await history
            .ListSeriesAsync(
                query.CompanyId, query.LocationId, query.ItemId, query.ItemVariantId,
                query.From, query.To, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToSnapshot)];
    }

    internal static DemandHistorySnapshot ToSnapshot(DemandHistory row) => new(
        row.Id,
        row.LocationId,
        row.ItemId,
        row.ItemVariantId,
        row.PeriodStart,
        row.PeriodEnd,
        row.TotalQuantity,
        row.Uom,
        row.GeneratedAt);
}

/// <summary>One parameter row, as read back out.</summary>
public sealed record ReplenishmentParameterSnapshot(
    Guid Id,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    ForecastMethod ForecastMethod,
    decimal ServiceLevelPercent,
    int LeadTimeDays,
    int ReviewPeriodDays,
    string Uom,
    decimal? SafetyStock,
    decimal? ReorderPoint,
    bool? SafetyLowConfidence);

/// <summary>Lists every parameter row with its latest safety calculation.</summary>
public sealed record ListParametersQuery : IQuery<IReadOnlyList<ReplenishmentParameterSnapshot>>;

/// <summary>Reads the parameters.</summary>
public sealed class ListParametersQueryHandler(
    IReplenishmentParameterRepository parameters,
    ISafetyStockCalculationRepository safety) : IQueryHandler<ListParametersQuery, IReadOnlyList<ReplenishmentParameterSnapshot>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentParameterSnapshot>> HandleAsync(
        ListParametersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ReplenishmentParameter> all = await parameters
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshots = new List<ReplenishmentParameterSnapshot>(all.Count);

        foreach (ReplenishmentParameter parameter in all)
        {
            SafetyStockCalculation? calc = parameter.CompanyId is { } companyId
                ? await safety
                    .LatestAsync(companyId, parameter.LocationId, parameter.ItemId, parameter.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            snapshots.Add(new ReplenishmentParameterSnapshot(
                parameter.Id,
                parameter.CompanyId ?? Guid.Empty,
                parameter.LocationId,
                parameter.ItemId,
                parameter.ItemVariantId,
                parameter.ForecastMethod,
                parameter.ServiceLevelPercent,
                parameter.LeadTimeDays,
                parameter.ReviewPeriodDays,
                parameter.Uom,
                calc?.SafetyStock,
                calc?.ReorderPoint,
                calc?.LowConfidence));
        }

        return snapshots;
    }
}

/// <summary>Open-to-buy status for a month: planned, committed live, remaining.</summary>
public sealed record OpenToBuyStatus(
    Guid CompanyId,
    int Year,
    int Month,
    decimal Planned,
    decimal Committed,
    decimal Remaining,
    bool OverCommitted,
    string Currency,
    DateTimeOffset AsAt);

/// <summary>Reads a month's open-to-buy status.</summary>
public sealed record GetOpenToBuyStatusQuery(Guid CompanyId, int Year, int Month)
    : IQuery<OpenToBuyStatus?>;

/// <summary>Reads the status. <c>null</c> when no budget is set — no budget, no verdict.</summary>
public sealed class GetOpenToBuyStatusQueryHandler(
    IOpenToBuyBudgetRepository budgets,
    IOtbCommitmentReader commitments,
    IClock clock) : IQueryHandler<GetOpenToBuyStatusQuery, OpenToBuyStatus?>
{
    /// <inheritdoc />
    public async Task<OpenToBuyStatus?> HandleAsync(
        GetOpenToBuyStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        OpenToBuyBudget? budget = await budgets
            .FindAsync(query.CompanyId, query.Year, query.Month, null, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            return null;
        }

        DateTimeOffset now = clock.UtcNow;

        OtbCommitments committed = await commitments
            .ReadCommittedAsync(budget.Currency, now, cancellationToken)
            .ConfigureAwait(false);

        decimal remaining = budget.PlannedAmount - committed.Committed;

        return new OpenToBuyStatus(
            query.CompanyId,
            query.Year,
            query.Month,
            budget.PlannedAmount,
            committed.Committed,
            remaining,
            remaining < 0m,
            budget.Currency,
            now);
    }
}

/// <summary>One suggestion, as read back out.</summary>
public sealed record ReplenishmentSuggestionSnapshot(
    Guid Id,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal SuggestedQuantity,
    decimal? AcceptedQuantity,
    string Uom,
    SuggestionReason Reason,
    SuggestionSource Source,
    Guid? SourceCompanyId,
    Guid? SourceLocationId,
    SuggestionStatus Status,
    Guid? DownstreamDocumentId,
    bool OverOpenToBuy,
    DateTimeOffset RaisedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Lists open suggestions.</summary>
public sealed record ListSuggestionsQuery : IQuery<IReadOnlyList<ReplenishmentSuggestionSnapshot>>;

/// <summary>Reads the open suggestions.</summary>
public sealed class ListSuggestionsQueryHandler(IReplenishmentSuggestionRepository suggestions)
    : IQueryHandler<ListSuggestionsQuery, IReadOnlyList<ReplenishmentSuggestionSnapshot>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentSuggestionSnapshot>> HandleAsync(
        ListSuggestionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ReplenishmentSuggestion> open = await suggestions
            .ListOpenAsync(null, 200, cancellationToken)
            .ConfigureAwait(false);

        return [.. open.Select(ToSnapshot)];
    }

    internal static ReplenishmentSuggestionSnapshot ToSnapshot(ReplenishmentSuggestion suggestion) => new(
        suggestion.Id,
        suggestion.CompanyId ?? Guid.Empty,
        suggestion.LocationId,
        suggestion.ItemId,
        suggestion.ItemVariantId,
        suggestion.SuggestedQuantity,
        suggestion.AcceptedQuantity,
        suggestion.Uom,
        suggestion.Reason,
        suggestion.Source,
        suggestion.SourceCompanyId,
        suggestion.SourceLocationId,
        suggestion.Status,
        suggestion.DownstreamDocumentId,
        suggestion.OverOpenToBuy,
        suggestion.RaisedAt,
        suggestion.ExpiresAt);
}

/// <summary>One markdown plan line, as read back out.</summary>
public sealed record MarkdownPlanLineSnapshot(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal CurrentPrice,
    decimal ProposedDiscountPercent,
    string Currency,
    string? AbcXyz,
    decimal SellThroughPercent,
    decimal DaysOfSupply,
    Guid? PromotionId);

/// <summary>One markdown plan with its lines, as read back out.</summary>
public sealed record MarkdownPlanSnapshot(
    Guid Id,
    string Code,
    string Reason,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Version,
    MarkdownPlanStatus Status,
    Guid? ApprovalRequestId,
    Guid? PromotionId,
    IReadOnlyList<MarkdownPlanLineSnapshot> Lines);

/// <summary>Reads one markdown plan by id.</summary>
public sealed record GetMarkdownPlanQuery(Guid PlanId) : IQuery<MarkdownPlanSnapshot>;

/// <summary>Reads the plan.</summary>
public sealed class GetMarkdownPlanQueryHandler(IMarkdownPlanRepository plans)
    : IQueryHandler<GetMarkdownPlanQuery, MarkdownPlanSnapshot>
{
    /// <inheritdoc />
    public async Task<MarkdownPlanSnapshot> HandleAsync(
        GetMarkdownPlanQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        MarkdownPlan plan = await plans
            .FindAsync(query.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", query.PlanId);

        return ToSnapshot(plan);
    }

    internal static MarkdownPlanSnapshot ToSnapshot(MarkdownPlan plan) => new(
        plan.Id,
        plan.Code,
        plan.Reason,
        plan.EffectiveFrom,
        plan.EffectiveTo,
        plan.Version,
        plan.Status,
        plan.ApprovalRequestId,
        plan.PromotionId,
        [.. plan.Lines.Select(line => new MarkdownPlanLineSnapshot(
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
