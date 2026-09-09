using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning;

/// <summary>One sale-issue movement, as the demand rollup reads it.</summary>
/// <param name="CompanyId">The company that sold.</param>
/// <param name="LocationId">Where it was sold.</param>
/// <param name="ItemId">The item, or <c>null</c> for a variant.</param>
/// <param name="ItemVariantId">The variant, or <c>null</c> for an item.</param>
/// <param name="Quantity">The signed quantity (negative for a sale issue).</param>
/// <param name="Uom">The unit of measure.</param>
/// <param name="OccurredAt">When the sale happened, UTC.</param>
public sealed record SaleIssueRecord(
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom,
    DateTimeOffset OccurredAt);

/// <summary>
/// Reads sale issues out of the authoritative stock ledger. Planning's only window into another
/// module's tables — through this port, never a DbContext.
/// </summary>
public interface IDemandHistorySource
{
    /// <summary>Every sale issue in a window, oldest first.</summary>
    Task<IReadOnlyList<SaleIssueRecord>> ListSaleIssuesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes <see cref="DemandHistory"/> rows.</summary>
public interface IDemandHistoryRepository
{
    /// <summary>Finds one natural-key row, or <c>null</c>.</summary>
    Task<DemandHistory?> FindAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly periodStart,
        CancellationToken cancellationToken = default);

    /// <summary>One SKU/location series in a window, oldest first.</summary>
    Task<IReadOnlyList<DemandHistory>> ListSeriesAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Every row touching a window, for classification and rollup gap-filling.</summary>
    Task<IReadOnlyList<DemandHistory>> ListWindowAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Distinct SKU/locations with any history up to a date — the forecast run's work list.</summary>
    Task<IReadOnlyList<DemandHistory>> ListLatestBeforeAsync(
        DateOnly to, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a new row.</summary>
    void Add(DemandHistory row);
}

/// <summary>Reads and writes <see cref="DemandForecast"/> snapshots.</summary>
public interface IDemandForecastRepository
{
    /// <summary>Finds a forecast by id, or <c>null</c>.</summary>
    Task<DemandForecast?> FindAsync(Guid forecastId, CancellationToken cancellationToken = default);

    /// <summary>The newest version for one SKU/location/period, or <c>null</c>.</summary>
    Task<DemandForecast?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly forecastPeriod,
        CancellationToken cancellationToken = default);

    /// <summary>The newest forecast at or before a date — the replenishment engine's read.</summary>
    Task<DemandForecast?> LatestBeforeAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly onOrBefore,
        CancellationToken cancellationToken = default);

    /// <summary>Every version for one SKU/location/period, oldest first.</summary>
    Task<IReadOnlyList<DemandForecast>> ListVersionsAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId, DateOnly forecastPeriod,
        CancellationToken cancellationToken = default);

    /// <summary>A keyset page of snapshots, newest period first, optionally narrowed.</summary>
    Task<(IReadOnlyList<DemandForecast> Forecasts, bool HasMore)> ListPageAsync(
        Guid? companyId,
        ForecastMethod? method,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new snapshot. Never updates — versions are append-only.</summary>
    void Add(DemandForecast forecast);
}

/// <summary>Reads and writes <see cref="ReplenishmentParameter"/> rows.</summary>
public interface IReplenishmentParameterRepository
{
    /// <summary>Finds parameters for one SKU at one location, or <c>null</c>.</summary>
    Task<ReplenishmentParameter?> FindAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Every parameter row in the tenant — the scheduled runs' work lists.</summary>
    Task<IReadOnlyList<ReplenishmentParameter>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new row.</summary>
    void Add(ReplenishmentParameter parameters);
}

/// <summary>Reads and writes <see cref="AbcXyzClassification"/> snapshots.</summary>
public interface IAbcXyzClassificationRepository
{
    /// <summary>The latest snapshot for one SKU at one location, or <c>null</c>.</summary>
    Task<AbcXyzClassification?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a snapshot. Snapshots are append-only.</summary>
    void Add(AbcXyzClassification classification);
}

/// <summary>Reads and writes <see cref="SafetyStockCalculation"/> rows.</summary>
public interface ISafetyStockCalculationRepository
{
    /// <summary>The latest calculation for one SKU at one location, or <c>null</c>.</summary>
    Task<SafetyStockCalculation?> LatestAsync(
        Guid companyId, Guid locationId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a calculation. Calculations are append-only audit.</summary>
    void Add(SafetyStockCalculation calculation);
}

/// <summary>Reads and writes <see cref="OpenToBuyBudget"/> rows.</summary>
public interface IOpenToBuyBudgetRepository
{
    /// <summary>Finds one month's budget, or <c>null</c>.</summary>
    Task<OpenToBuyBudget?> FindAsync(
        Guid companyId, int year, int month, string? categoryCode,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a budget.</summary>
    void Add(OpenToBuyBudget budget);
}

/// <summary>Reads and writes <see cref="ReplenishmentSuggestion"/> rows.</summary>
public interface IReplenishmentSuggestionRepository
{
    /// <summary>Finds a suggestion by id, or <c>null</c>.</summary>
    Task<ReplenishmentSuggestion?> FindAsync(Guid suggestionId, CancellationToken cancellationToken = default);

    /// <summary>Finds an open suggestion by its run key, or <c>null</c>. Re-runs upsert on this.</summary>
    Task<ReplenishmentSuggestion?> FindOpenByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Open suggestions, optionally only those already expired.</summary>
    Task<IReadOnlyList<ReplenishmentSuggestion>> ListOpenAsync(
        DateTimeOffset? expiredBefore, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a suggestion.</summary>
    void Add(ReplenishmentSuggestion suggestion);
}

/// <summary>Reads and writes <see cref="MarkdownPlan"/> aggregates.</summary>
public interface IMarkdownPlanRepository
{
    /// <summary>Finds a plan with its lines loaded, or <c>null</c>. Lines are not optional.</summary>
    Task<MarkdownPlan?> FindAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>Finds a plan by its code, or <c>null</c>.</summary>
    Task<MarkdownPlan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Whether a live (draft through active) plan already names a SKU.</summary>
    Task<bool> HasLivePlanForSkuAsync(
        Guid companyId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);

    /// <summary>Approved plans whose effective date has arrived — the activation sweep's work list.</summary>
    Task<IReadOnlyList<MarkdownPlan>> ListDueForActivationAsync(
        DateOnly today, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a plan (lines travel with the aggregate).</summary>
    void Add(MarkdownPlan plan);
}
