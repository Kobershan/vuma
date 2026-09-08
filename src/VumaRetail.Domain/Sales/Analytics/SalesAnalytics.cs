using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Analytics;

/// <summary>
/// A company-scoped sales aggregation: revenue, cost, margin and tax per period, channel and
/// category. A read model (<c>NodeLocal</c>) rebuilt from posted invoices — planning information,
/// never a commit input (ADR-119).
/// </summary>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.StoreWins)]
public sealed class SalesAnalytics : Entity
{
    private SalesAnalytics()
    {
    }

    private SalesAnalytics(
        Guid tenantId,
        Guid? storeId,
        AnalyticsPeriod period,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string? categoryCode,
        string channel,
        string currency)
        : base(tenantId, storeId)
    {
        Period = period;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        CategoryCode = categoryCode;
        Channel = channel;
        Currency = currency;
        Revenue = Money.Zero(currency);
        CostOfSale = Money.Zero(currency);
        Margin = Money.Zero(currency);
        TaxLiability = Money.Zero(currency);
        OrderCount = 0;
        LineCount = 0;
    }

    /// <summary>Which bucket this row aggregates.</summary>
    public AnalyticsPeriod Period { get; private set; }

    /// <summary>The bucket's start, UTC.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>The bucket's end, UTC.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>The product category, or <c>null</c> for the all-categories row.</summary>
    public string? CategoryCode { get; private set; }

    /// <summary>The sales channel, e.g. <c>Till</c>, <c>Order</c>.</summary>
    public string Channel { get; private set; } = string.Empty;

    /// <summary>The ISO 4217 currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Total invoiced revenue in the bucket.</summary>
    public Money Revenue { get; private set; }

    /// <summary>Total cost of what was sold, where the cost is known.</summary>
    public Money CostOfSale { get; private set; }

    /// <summary>Revenue less cost of sale.</summary>
    public Money Margin { get; private set; }

    /// <summary>Total output tax declared in the bucket.</summary>
    public Money TaxLiability { get; private set; }

    /// <summary>How many documents the bucket aggregates.</summary>
    public int OrderCount { get; private set; }

    /// <summary>How many lines the bucket aggregates.</summary>
    public int LineCount { get; private set; }

    /// <summary>When the row was computed, UTC. Every group figure crossing an API carries this (ADR-119).</summary>
    public DateTimeOffset AsAt { get; private set; }

    /// <summary>True when a contributor is behind and the row is older than it should be. Shown, never hidden.</summary>
    public bool IsStale { get; private set; }

    /// <summary>Opens an empty aggregation row for one company, bucket, channel and category.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="companyId">The company being aggregated. Required — analytics never cross companies silently.</param>
    /// <param name="period">Which bucket.</param>
    /// <param name="periodStart">The bucket's start, UTC.</param>
    /// <param name="periodEnd">The bucket's end, UTC.</param>
    /// <param name="categoryCode">The category, or <c>null</c> for all categories.</param>
    /// <param name="channel">The sales channel.</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    public static SalesAnalytics Create(
        Guid tenantId,
        Guid? storeId,
        Guid companyId,
        AnalyticsPeriod period,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string? categoryCode,
        string channel,
        string currency)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Analytics must belong to a tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Analytics must belong to a company.", nameof(companyId));
        }

        var row = new SalesAnalytics(
            tenantId, storeId, period, periodStart, periodEnd, categoryCode, channel, currency);
        row.AssignCompany(companyId);

        return row;
    }

    /// <summary>Replaces the bucket's figures with a fresh computation.</summary>
    /// <param name="revenue">Total invoiced revenue.</param>
    /// <param name="costOfSale">Total cost of sale.</param>
    /// <param name="taxLiability">Total output tax.</param>
    /// <param name="orderCount">How many documents were aggregated.</param>
    /// <param name="lineCount">How many lines were aggregated.</param>
    /// <param name="asAt">When the figures were computed, UTC.</param>
    /// <param name="isStale">True when a contributor is behind.</param>
    public void Aggregate(
        Money revenue,
        Money costOfSale,
        Money taxLiability,
        int orderCount,
        int lineCount,
        DateTimeOffset asAt,
        bool isStale = false)
    {
        Revenue = revenue;
        CostOfSale = costOfSale;
        Margin = revenue - costOfSale;
        TaxLiability = taxLiability;
        OrderCount = orderCount;
        LineCount = lineCount;
        AsAt = asAt;
        IsStale = isStale;
    }
}
