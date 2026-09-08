namespace VumaRetail.Domain.Sales.Analytics;

/// <summary>The buckets a company-scoped sales read model aggregates into.</summary>
public enum AnalyticsPeriod
{
    /// <summary>One calendar day, store-local.</summary>
    Daily = 0,

    /// <summary>One ISO week.</summary>
    Weekly = 1,

    /// <summary>One calendar month.</summary>
    Monthly = 2,

    /// <summary>First of January to the period end.</summary>
    YearToDate = 3
}
