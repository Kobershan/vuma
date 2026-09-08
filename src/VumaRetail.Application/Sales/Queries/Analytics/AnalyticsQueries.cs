using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales.Analytics;
#pragma warning disable CS1591

namespace VumaRetail.Application.Sales.Queries;

public sealed record GetSalesAnalyticsQuery(
    Guid CompanyId,
    AnalyticsPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To) : IQuery<IReadOnlyList<SalesAnalytics>>;

public sealed record GetGroupAnalyticsQuery(
    AnalyticsPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To) : IQuery<IReadOnlyList<SalesAnalytics>>;

public sealed class GetSalesAnalyticsQueryHandler(ISalesAnalyticsRepository analytics)
    : IQueryHandler<GetSalesAnalyticsQuery, IReadOnlyList<SalesAnalytics>>
{
    public async Task<IReadOnlyList<SalesAnalytics>> HandleAsync(
        GetSalesAnalyticsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await analytics.GetByCompanyAsync(
            query.CompanyId, query.Period, query.From, query.To, cancellationToken);
    }
}

public sealed class GetGroupAnalyticsQueryHandler(ISalesAnalyticsRepository analytics)
    : IQueryHandler<GetGroupAnalyticsQuery, IReadOnlyList<SalesAnalytics>>
{
    public async Task<IReadOnlyList<SalesAnalytics>> HandleAsync(
        GetGroupAnalyticsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await analytics.GetGroupAsync(query.Period, query.From, query.To, cancellationToken);
    }
}
