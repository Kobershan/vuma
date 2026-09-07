using VumaRetail.Domain.Sales.Analytics;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
#pragma warning disable CS1591

namespace VumaRetail.Application.Sales.Services;

public sealed class AnalyticsService
{
    private readonly ISalesAnalyticsRepository _analytics;
    private readonly IClock _clock;

    public AnalyticsService(ISalesAnalyticsRepository analytics, IClock clock)
    {
        _analytics = analytics;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SalesAnalytics>> GetCompanyAnalyticsAsync(
        Guid companyId, AnalyticsPeriod period, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await _analytics.GetByCompanyAsync(companyId, period, from, to, cancellationToken);
    }

    public async Task<IReadOnlyList<SalesAnalytics>> GetGroupAnalyticsAsync(
        AnalyticsPeriod period, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = await _analytics.GetGroupAsync(period, from, to, cancellationToken);
        foreach (var result in results)
        {
            result.Aggregate(
                result.Revenue, result.CostOfSale, result.TaxLiability,
                result.OrderCount, result.LineCount,
                _clock.UtcNow,
                isStale: true);
        }
        return results;
    }

    public async Task RebuildAsync(
        Guid? companyId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await _analytics.RebuildAsync(companyId, from, to, cancellationToken);
    }
}
