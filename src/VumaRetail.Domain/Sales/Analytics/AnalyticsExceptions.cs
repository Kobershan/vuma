using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Analytics;

/// <summary>Something analytics-owned was asked for that does not exist.</summary>
/// <param name="what">What was being looked for.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class AnalyticsNotFoundException(string what, Guid id)
    : DomainException("ANALYTICS_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>A sales analytics business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class AnalyticsRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>An aggregation was asked for in a bucket this read model does not keep.</summary>
    /// <param name="period">The bucket that was asked for.</param>
    public static AnalyticsRuleException PeriodNotSupported(AnalyticsPeriod period)
        => new("ANALYTICS_PERIOD_NOT_SUPPORTED", $"Analytics period {period} is not supported.");

    /// <summary>Analytics were asked for a company the caller may not see.</summary>
    /// <param name="companyId">The company.</param>
    public static AnalyticsRuleException CompanyNotAuthorized(Guid companyId)
        => new("ANALYTICS_COMPANY_NOT_AUTHORIZED", $"The user is not authorized to view analytics for company {companyId}.");
}
