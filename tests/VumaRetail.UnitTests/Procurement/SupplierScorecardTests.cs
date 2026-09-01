using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.UnitTests.Procurement;

/// <summary>
/// The scorecard's rates and the one rule that keeps them meaningful: a snapshot is taken once, over a
/// period that has finished.
/// </summary>
/// <remarks>
/// The empty-denominator cases are the ones that matter. A supplier who was sent nothing has not failed
/// to deliver it, and a rate of zero would libel them on a report and drag a blended rating down for a
/// period in which they did nothing wrong.
/// </remarks>
public sealed class SupplierScorecardTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly DateOnly PeriodStart = new(2026, 7, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 7, 31);
    private static readonly DateOnly Today = new(2026, 8, 16);
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_perfect_period_rates_a_hundred_across_the_board()
    {
        SupplierScorecard scorecard = Snapshot(Figures(
            ordersPlaced: 4,
            linesOrdered: 12,
            linesDelivered: 12,
            linesOnTime: 12,
            linesWithRejections: 0,
            quantityOrdered: 1_000m,
            quantityReceived: 1_000m,
            quantityRejected: 0m));

        scorecard.OnTimeDeliveryRate.Should().Be(100m);
        scorecard.FillRate.Should().Be(100m);
        scorecard.QualityRate.Should().Be(100m);
        scorecard.OverallRating.Should().Be(100m);
    }

    [Fact]
    public void Late_and_short_and_rejected_each_move_their_own_rate()
    {
        // 9 of 12 lines on time; 900 of 1,000 received; 40 of the 940 that arrived turned away.
        SupplierScorecard scorecard = Snapshot(Figures(
            ordersPlaced: 4,
            linesOrdered: 12,
            linesDelivered: 12,
            linesOnTime: 9,
            linesWithRejections: 2,
            quantityOrdered: 1_000m,
            quantityReceived: 900m,
            quantityRejected: 40m));

        scorecard.OnTimeDeliveryRate.Should().Be(75m);
        scorecard.FillRate.Should().Be(90m);

        // Quality is against everything that arrived, not against what was ordered — a supplier who was
        // short is not also guilty of sending the missing units in bad condition.
        scorecard.QualityRate.Should().Be(95.74m);
        scorecard.OverallRating.Should().Be(86.91m);
    }

    [Fact]
    public void A_supplier_sent_nothing_is_not_rated_at_zero()
    {
        // Business rule 15's corner. Zero would be a libel and null would push the decision onto every
        // consumer, one of which would eventually get it wrong.
        SupplierScorecard scorecard = Snapshot(Figures(
            ordersPlaced: 0,
            linesOrdered: 0,
            linesDelivered: 0,
            linesOnTime: 0,
            linesWithRejections: 0,
            quantityOrdered: 0m,
            quantityReceived: 0m,
            quantityRejected: 0m));

        scorecard.OnTimeDeliveryRate.Should().Be(100m);
        scorecard.FillRate.Should().Be(100m);
        scorecard.QualityRate.Should().Be(100m);
        scorecard.OverallRating.Should().Be(100m);
        scorecard.OrdersPlaced.Should().Be(0);
    }

    [Fact]
    public void A_supplier_who_delivered_nothing_they_were_sent_rates_zero()
    {
        // The other side of the same corner: an empty denominator is not the same as a zero numerator.
        SupplierScorecard scorecard = Snapshot(Figures(
            ordersPlaced: 3,
            linesOrdered: 8,
            linesDelivered: 0,
            linesOnTime: 0,
            linesWithRejections: 0,
            quantityOrdered: 500m,
            quantityReceived: 0m,
            quantityRejected: 0m));

        // Nothing was delivered, so nothing was delivered on time — but the denominator is zero, and the
        // fill rate is the measure that actually catches this.
        scorecard.OnTimeDeliveryRate.Should().Be(100m);
        scorecard.FillRate.Should().Be(0m);
        scorecard.QualityRate.Should().Be(100m);
    }

    [Fact]
    public void A_period_that_has_not_closed_cannot_be_snapshotted()
    {
        // ADR-084. A snapshot taken mid-period says something different every time it is read, which is
        // not a rating.
        Action open = () => SupplierScorecard.Snapshot(
            TenantId,
            StoreId,
            PartnerId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            Today,
            Figures(1, 1, 1, 1, 0, 10m, 10m, 0m),
            Now);

        open.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_SCORECARD_PERIOD_NOT_CLOSED");
    }

    [Fact]
    public void A_period_ending_today_has_not_closed_yet()
    {
        // Today is still running. Snapshotting it at 09:30 counts a morning's deliveries as a month's.
        Action endsToday = () => SupplierScorecard.Snapshot(
            TenantId, TenantId, PartnerId, PeriodStart, Today, Today,
            Figures(1, 1, 1, 1, 0, 10m, 10m, 0m), Now);

        endsToday.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_SCORECARD_PERIOD_NOT_CLOSED");
    }

    [Fact]
    public void An_inverted_period_is_refused()
    {
        Action inverted = () => SupplierScorecard.Snapshot(
            TenantId, StoreId, PartnerId, PeriodEnd, PeriodStart, Today,
            Figures(1, 1, 1, 1, 0, 10m, 10m, 0m), Now);

        inverted.Should().Throw<ProcurementRuleException>()
            .Which.Code.Should().Be("PROCUREMENT_SCORECARD_PERIOD_INVERTED");
    }

    [Fact]
    public void The_money_figures_keep_the_currency_they_were_counted_in()
    {
        SupplierScorecard scorecard = Snapshot(Figures(
            2, 5, 5, 5, 0, 100m, 100m, 0m,
            purchaseValue: new Money(48_500m, "ZAR"),
            priceVariance: new Money(212.50m, "ZAR")));

        scorecard.Currency.Should().Be("ZAR");
        scorecard.PurchaseValue.Amount.Should().Be(48_500m);
        scorecard.PriceVariance.Amount.Should().Be(212.50m);
    }

    private static SupplierScorecard Snapshot(SupplierScorecardFigures figures)
        => SupplierScorecard.Snapshot(
            TenantId, StoreId, PartnerId, PeriodStart, PeriodEnd, Today, figures, Now);

    private static SupplierScorecardFigures Figures(
        int ordersPlaced,
        int linesOrdered,
        int linesDelivered,
        int linesOnTime,
        int linesWithRejections,
        decimal quantityOrdered,
        decimal quantityReceived,
        decimal quantityRejected,
        Money? purchaseValue = null,
        Money? priceVariance = null)
        => new(
            ordersPlaced,
            linesOrdered,
            linesDelivered,
            linesOnTime,
            linesWithRejections,
            quantityOrdered,
            quantityReceived,
            quantityRejected,
            purchaseValue ?? Money.Zero("ZAR"),
            priceVariance ?? Money.Zero("ZAR"));
}
