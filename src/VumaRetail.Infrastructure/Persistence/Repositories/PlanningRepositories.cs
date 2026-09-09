using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Planning;
using VumaRetail.Domain.Planning;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the planning repositories (Stage 15).</summary>
public sealed class DemandHistoryRepository(VumaRetailDbContext context) : IDemandHistoryRepository
{
    /// <inheritdoc />
    public Task<DemandHistory?> FindAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly periodStart,
        CancellationToken cancellationToken = default)
        => context.DemandHistories.FirstOrDefaultAsync(row =>
            row.CompanyId == companyId
            && row.LocationId == locationId
            && row.ItemId == itemId
            && row.ItemVariantId == itemVariantId
            && row.PeriodStart == periodStart, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemandHistory>> ListSeriesAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
        => await context.DemandHistories.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.LocationId == locationId
                && row.ItemId == itemId
                && row.ItemVariantId == itemVariantId
                && row.PeriodStart >= from
                && row.PeriodStart <= to)
            .OrderBy(row => row.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemandHistory>> ListWindowAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await context.DemandHistories.AsNoTracking()
            .Where(row => row.PeriodStart >= from && row.PeriodStart <= to)
            .OrderBy(row => row.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemandHistory>> ListLatestBeforeAsync(
        DateOnly to, int limit, CancellationToken cancellationToken = default)
        => await context.DemandHistories.AsNoTracking()
            .Where(row => row.PeriodStart <= to)
            .OrderByDescending(row => row.PeriodStart)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(DemandHistory row) => context.DemandHistories.Add(row);
}

/// <summary>EF Core implementation of the forecast snapshot store. Snapshots are append-only.</summary>
public sealed class DemandForecastRepository(VumaRetailDbContext context) : IDemandForecastRepository
{
    /// <inheritdoc />
    public Task<DemandForecast?> FindAsync(Guid forecastId, CancellationToken cancellationToken = default)
        => context.DemandForecasts.FirstOrDefaultAsync(forecast => forecast.Id == forecastId, cancellationToken);

    /// <inheritdoc />
    public Task<DemandForecast?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly forecastPeriod,
        CancellationToken cancellationToken = default)
        => LatestQuery(companyId, locationId, itemId, itemVariantId)
            .Where(forecast => forecast.ForecastPeriod == forecastPeriod)
            .OrderByDescending(forecast => forecast.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<DemandForecast?> LatestBeforeAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly onOrBefore,
        CancellationToken cancellationToken = default)
        => LatestQuery(companyId, locationId, itemId, itemVariantId)
            .Where(forecast => forecast.ForecastPeriod <= onOrBefore)
            .OrderByDescending(forecast => forecast.ForecastPeriod)
            .ThenByDescending(forecast => forecast.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemandForecast>> ListVersionsAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly forecastPeriod,
        CancellationToken cancellationToken = default)
        => await LatestQuery(companyId, locationId, itemId, itemVariantId)
            .Where(forecast => forecast.ForecastPeriod == forecastPeriod)
            .OrderBy(forecast => forecast.GeneratedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<DemandForecast> Forecasts, bool HasMore)> ListPageAsync(
        Guid? companyId,
        ForecastMethod? method,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<DemandForecast> query = context.DemandForecasts.AsNoTracking();

        if (companyId is { } company)
        {
            query = query.Where(forecast => forecast.CompanyId == company);
        }

        if (method is { } filtered)
        {
            query = query.Where(forecast => forecast.ForecastMethod == filtered);
        }

        if (after is { } cursor
            && DateOnly.TryParseExact(cursor.SortKey, "yyyy-MM-dd", out DateOnly period))
        {
            query = query.Where(forecast => forecast.ForecastPeriod < period
                || (forecast.ForecastPeriod == period && forecast.Id.CompareTo(cursor.Id) < 0));
        }

        List<DemandForecast> page = await query
            .OrderByDescending(forecast => forecast.ForecastPeriod)
            .ThenByDescending(forecast => forecast.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return (page, hasMore);
    }

    /// <inheritdoc />
    public void Add(DemandForecast forecast) => context.DemandForecasts.Add(forecast);

    private IQueryable<DemandForecast> LatestQuery(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId)
        => context.DemandForecasts.AsNoTracking().Where(forecast =>
            forecast.CompanyId == companyId
            && forecast.LocationId == locationId
            && forecast.ItemId == itemId
            && forecast.ItemVariantId == itemVariantId);
}

/// <summary>EF Core implementation of the replenishment parameter store.</summary>
public sealed class ReplenishmentParameterRepository(VumaRetailDbContext context)
    : IReplenishmentParameterRepository
{
    /// <inheritdoc />
    public Task<ReplenishmentParameter?> FindAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.ReplenishmentParameters.FirstOrDefaultAsync(parameter =>
            parameter.CompanyId == companyId
            && parameter.LocationId == locationId
            && parameter.ItemId == itemId
            && parameter.ItemVariantId == itemVariantId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentParameter>> ListAllAsync(
        CancellationToken cancellationToken = default)
        => await context.ReplenishmentParameters.AsNoTracking()
            .OrderBy(parameter => parameter.LocationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ReplenishmentParameter parameters) => context.ReplenishmentParameters.Add(parameters);
}

/// <summary>EF Core implementation of the classification snapshot store. Append-only.</summary>
public sealed class AbcXyzClassificationRepository(VumaRetailDbContext context)
    : IAbcXyzClassificationRepository
{
    /// <inheritdoc />
    public Task<AbcXyzClassification?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.AbcXyzClassifications.AsNoTracking()
            .Where(classification => classification.CompanyId == companyId
                && classification.LocationId == locationId
                && classification.ItemId == itemId
                && classification.ItemVariantId == itemVariantId)
            .OrderByDescending(classification => classification.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(AbcXyzClassification classification) => context.AbcXyzClassifications.Add(classification);
}

/// <summary>EF Core implementation of the safety-stock calculation store. Append-only audit.</summary>
public sealed class SafetyStockCalculationRepository(VumaRetailDbContext context)
    : ISafetyStockCalculationRepository
{
    /// <inheritdoc />
    public Task<SafetyStockCalculation?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.SafetyStockCalculations.AsNoTracking()
            .Where(calculation => calculation.CompanyId == companyId
                && calculation.LocationId == locationId
                && calculation.ItemId == itemId
                && calculation.ItemVariantId == itemVariantId)
            .OrderByDescending(calculation => calculation.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(SafetyStockCalculation calculation) => context.SafetyStockCalculations.Add(calculation);
}

/// <summary>EF Core implementation of the open-to-buy budget store.</summary>
public sealed class OpenToBuyBudgetRepository(VumaRetailDbContext context) : IOpenToBuyBudgetRepository
{
    /// <inheritdoc />
    public Task<OpenToBuyBudget?> FindAsync(
        Guid companyId, int year, int month, string? categoryCode,
        CancellationToken cancellationToken = default)
        => context.OpenToBuyBudgets.FirstOrDefaultAsync(budget =>
            budget.CompanyId == companyId
            && budget.Year == year
            && budget.Month == month
            && budget.CategoryCode == categoryCode, cancellationToken);

    /// <inheritdoc />
    public void Add(OpenToBuyBudget budget) => context.OpenToBuyBudgets.Add(budget);
}

/// <summary>EF Core implementation of the replenishment suggestion store.</summary>
public sealed class ReplenishmentSuggestionRepository(VumaRetailDbContext context)
    : IReplenishmentSuggestionRepository
{
    /// <inheritdoc />
    public Task<ReplenishmentSuggestion?> FindAsync(Guid suggestionId, CancellationToken cancellationToken = default)
        => context.ReplenishmentSuggestions
            .FirstOrDefaultAsync(suggestion => suggestion.Id == suggestionId, cancellationToken);

    /// <inheritdoc />
    public Task<ReplenishmentSuggestion?> FindOpenByKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default)
        => context.ReplenishmentSuggestions.FirstOrDefaultAsync(suggestion =>
            suggestion.IdempotencyKey == idempotencyKey
            && suggestion.Status == SuggestionStatus.Open, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentSuggestion>> ListOpenAsync(
        DateTimeOffset? expiredBefore, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<ReplenishmentSuggestion> query = context.ReplenishmentSuggestions
            .Where(suggestion => suggestion.Status == SuggestionStatus.Open);

        if (expiredBefore is { } before)
        {
            query = query.Where(suggestion => suggestion.ExpiresAt <= before);
        }

        return await query
            .OrderBy(suggestion => suggestion.RaisedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(ReplenishmentSuggestion suggestion) => context.ReplenishmentSuggestions.Add(suggestion);
}

/// <summary>EF Core implementation of the markdown plan store. Lines travel with the aggregate.</summary>
public sealed class MarkdownPlanRepository(VumaRetailDbContext context) : IMarkdownPlanRepository
{
    /// <inheritdoc />
    public Task<MarkdownPlan?> FindAsync(Guid planId, CancellationToken cancellationToken = default)
        => context.MarkdownPlans
            .Include(plan => plan.Lines)
            .FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    /// <inheritdoc />
    public Task<MarkdownPlan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => context.MarkdownPlans
            .Include(plan => plan.Lines)
            .FirstOrDefaultAsync(plan => plan.Code == code, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> HasLivePlanForSkuAsync(
        Guid companyId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
        => await context.MarkdownPlans.AsNoTracking()
            .Where(plan => plan.CompanyId == companyId
                && (plan.Status == MarkdownPlanStatus.Draft
                    || plan.Status == MarkdownPlanStatus.PendingApproval
                    || plan.Status == MarkdownPlanStatus.Approved
                    || plan.Status == MarkdownPlanStatus.Active))
            .SelectMany(plan => plan.Lines)
            .AnyAsync(line => line.ItemId == itemId && line.ItemVariantId == itemVariantId, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarkdownPlan>> ListDueForActivationAsync(
        DateOnly today, int limit, CancellationToken cancellationToken = default)
        => await context.MarkdownPlans
            .Include(plan => plan.Lines)
            .Where(plan => plan.Status == MarkdownPlanStatus.Approved && plan.EffectiveFrom <= today)
            .OrderBy(plan => plan.EffectiveFrom)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(MarkdownPlan plan) => context.MarkdownPlans.Add(plan);
}
