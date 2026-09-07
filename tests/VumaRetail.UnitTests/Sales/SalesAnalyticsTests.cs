using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Analytics;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The analytics read model's contract: company-scoped buckets whose figures are replaced by each
/// fresh computation, stamped with when they were computed and whether a contributor is behind
/// (ADR-119). Planning information, never a commit input.
/// </summary>
public sealed class SalesAnalyticsTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsAt = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_row_is_empty_stamped_with_its_company_and_fresh()
    {
        SalesAnalytics row = Row();

        row.CompanyId.Should().Be(CompanyId);
        row.Period.Should().Be(AnalyticsPeriod.Monthly);
        row.Revenue.Should().Be(new Money(0m, "ZAR"));
        row.Margin.Should().Be(new Money(0m, "ZAR"));
        row.OrderCount.Should().Be(0);
        row.LineCount.Should().Be(0);
        row.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_replaces_the_figures_derives_margin_and_stamps_freshness()
    {
        SalesAnalytics row = Row();

        row.Aggregate(
            new Money(1150m, "ZAR"), new Money(700m, "ZAR"), new Money(150m, "ZAR"),
            10, 32, AsAt);

        row.Revenue.Amount.Should().Be(1150m);
        row.CostOfSale.Amount.Should().Be(700m);
        row.Margin.Amount.Should().Be(450m);
        row.TaxLiability.Amount.Should().Be(150m);
        row.OrderCount.Should().Be(10);
        row.LineCount.Should().Be(32);
        row.AsAt.Should().Be(AsAt);
        row.IsStale.Should().BeFalse();
    }

    [Fact]
    public void A_row_behind_its_contributors_says_so_openly()
    {
        SalesAnalytics row = Row();

        row.Aggregate(
            new Money(1150m, "ZAR"), new Money(700m, "ZAR"), new Money(150m, "ZAR"),
            10, 32, AsAt, isStale: true);

        row.IsStale.Should().BeTrue();
        row.AsAt.Should().Be(AsAt);
    }

    [Fact]
    public void A_row_needs_a_tenant_and_a_company()
    {
        Action noTenant = () => SalesAnalytics.Create(
            Guid.Empty, StoreId, CompanyId, AnalyticsPeriod.Daily,
            PeriodStart, PeriodEnd, null, "Till", "ZAR");
        noTenant.Should().Throw<ArgumentException>();

        Action noCompany = () => SalesAnalytics.Create(
            TenantId, StoreId, Guid.Empty, AnalyticsPeriod.Daily,
            PeriodStart, PeriodEnd, null, "Till", "ZAR");
        noCompany.Should().Throw<ArgumentException>();
    }

    private static SalesAnalytics Row()
    {
        return SalesAnalytics.Create(
            TenantId, StoreId, CompanyId, AnalyticsPeriod.Monthly,
            PeriodStart, PeriodEnd, null, "Till", "ZAR");
    }
}
