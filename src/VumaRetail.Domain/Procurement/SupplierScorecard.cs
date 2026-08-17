using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// What one supplier actually did over one closed period — orders placed, lines delivered on time,
/// quantity short, quantity rejected, and what they over-billed by.
/// </summary>
/// <remarks>
/// <para>
/// <b>A snapshot, never a live figure</b> (ADR-084). A rating recomputed on read changes when nobody
/// did anything — a late delivery landing today would silently restate last quarter — and a rating that
/// moves on its own is a rating nobody trusts enough to act on. A period is snapshotted once
/// (<c>ux_supplier_scorecards_partner_period</c>), after it has closed, and the row is the record.
/// </para>
/// <para>
/// <b>Counts, then percentages.</b> The raw counters are stored alongside the derived rates because the
/// rates alone are unreadable: "80% on time" over five deliveries and over five hundred are different
/// facts, and a buyer negotiating with the supplier needs the denominator in front of them. The
/// percentages are computed once, here, so every consumer sees the same rounding.
/// </para>
/// <para>
/// The <see cref="OverallRating"/> is a deliberately simple, documented average of the three service
/// measures rather than a weighted model. A weighting is a commercial policy, it differs by category,
/// and inventing one inside a domain entity would be exactly the sort of unilateral cross-cutting
/// decision <c>AGENTS.md</c> says a single stage does not get to make. The three components are stored,
/// so a later stage can weight them however a tenant wants without restating this row.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class SupplierScorecard : Entity
{
    private SupplierScorecard(
        Guid tenantId,
        Guid? storeId,
        Guid partnerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string currency,
        int ordersPlaced,
        int linesOrdered,
        int linesDelivered,
        int linesDeliveredOnTime,
        int linesWithRejections,
        decimal quantityOrdered,
        decimal quantityReceived,
        decimal quantityRejected,
        Money purchaseValue,
        Money priceVariance,
        DateTimeOffset snapshottedAt)
        : base(tenantId, storeId)
    {
        PartnerId = partnerId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Currency = currency;
        OrdersPlaced = ordersPlaced;
        LinesOrdered = linesOrdered;
        LinesDelivered = linesDelivered;
        LinesDeliveredOnTime = linesDeliveredOnTime;
        LinesWithRejections = linesWithRejections;
        QuantityOrdered = quantityOrdered;
        QuantityReceived = quantityReceived;
        QuantityRejected = quantityRejected;
        PurchaseValue = purchaseValue;
        PriceVariance = priceVariance;
        SnapshottedAt = snapshottedAt;

        OnTimeDeliveryRate = Rate(linesDeliveredOnTime, linesDelivered);
        FillRate = Rate(quantityReceived, quantityOrdered);
        QualityRate = quantityReceived + quantityRejected == 0m
            ? 100m
            : Rate(quantityReceived, quantityReceived + quantityRejected);
        OverallRating = decimal.Round(
            (OnTimeDeliveryRate + FillRate + QualityRate) / 3m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SupplierScorecard()
    {
    }

    /// <summary>The supplier. A bare id into <c>partners</c>.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The period's first day, inclusive.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>The period's last day, inclusive.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>The currency the money figures are in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>How many orders were issued to this supplier in the period.</summary>
    public int OrdersPlaced { get; private set; }

    /// <summary>How many order lines those orders carried.</summary>
    public int LinesOrdered { get; private set; }

    /// <summary>How many of them had anything delivered against them.</summary>
    public int LinesDelivered { get; private set; }

    /// <summary>How many arrived on or before the order's expected date (business rule 16).</summary>
    public int LinesDeliveredOnTime { get; private set; }

    /// <summary>How many deliveries had something turned away.</summary>
    public int LinesWithRejections { get; private set; }

    /// <summary>The quantity ordered, summed across units of measure as a bare decimal.</summary>
    /// <remarks>
    /// A bare <see cref="decimal"/> rather than a <see cref="Quantity"/>, deliberately and unusually. A
    /// scorecard spans every line the supplier delivered, and those lines are in kilograms and cases and
    /// eaches; a <see cref="Quantity"/> would refuse to add them, correctly. What the fill rate needs is
    /// a ratio of like to like — received against ordered, on the same set of lines — and that ratio is
    /// well defined even when the underlying units are not comparable, because the numerator and the
    /// denominator are built line by line from the same lines. The absolute totals are stored so the
    /// ratio can be audited, not so anybody can read them as a quantity.
    /// </remarks>
    public decimal QuantityOrdered { get; private set; }

    /// <summary>The quantity accepted into stock.</summary>
    public decimal QuantityReceived { get; private set; }

    /// <summary>The quantity turned away.</summary>
    public decimal QuantityRejected { get; private set; }

    /// <summary>What was bought from this supplier in the period, at order cost, excluding tax.</summary>
    public Money PurchaseValue { get; private set; }

    /// <summary>What their released invoices over-billed by, net of under-billing.</summary>
    public Money PriceVariance { get; private set; }

    /// <summary>Delivered-on-time lines as a percentage of delivered lines. 0–100.</summary>
    public decimal OnTimeDeliveryRate { get; private set; }

    /// <summary>Received quantity as a percentage of ordered quantity. 0–100.</summary>
    public decimal FillRate { get; private set; }

    /// <summary>Accepted quantity as a percentage of everything that arrived. 0–100.</summary>
    public decimal QualityRate { get; private set; }

    /// <summary>The plain average of the three rates. See the class remarks on why it is not weighted.</summary>
    public decimal OverallRating { get; private set; }

    /// <summary>When the snapshot was taken, UTC.</summary>
    public DateTimeOffset SnapshottedAt { get; private set; }

    /// <summary>Takes a scorecard snapshot for one supplier over one closed period.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store, or <c>null</c> for a tenant-wide scorecard.</param>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="periodStart">The period's first day.</param>
    /// <param name="periodEnd">The period's last day.</param>
    /// <param name="today">Today, so the closed-period rule can be checked.</param>
    /// <param name="figures">The counted figures.</param>
    /// <param name="snapshottedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">
    /// The period is inverted, or it has not closed yet (business rule 15).
    /// </exception>
    public static SupplierScorecard Snapshot(
        Guid tenantId,
        Guid? storeId,
        Guid partnerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly today,
        SupplierScorecardFigures figures,
        DateTimeOffset snapshottedAt)
    {
        ArgumentNullException.ThrowIfNull(figures);

        if (periodEnd < periodStart)
        {
            throw ProcurementRuleException.ScorecardPeriodInverted();
        }

        if (periodEnd >= today)
        {
            throw ProcurementRuleException.ScorecardPeriodNotClosed(periodEnd, today);
        }

        return new SupplierScorecard(
            tenantId,
            storeId,
            partnerId,
            periodStart,
            periodEnd,
            figures.PurchaseValue.Currency,
            figures.OrdersPlaced,
            figures.LinesOrdered,
            figures.LinesDelivered,
            figures.LinesDeliveredOnTime,
            figures.LinesWithRejections,
            figures.QuantityOrdered,
            figures.QuantityReceived,
            figures.QuantityRejected,
            figures.PurchaseValue,
            figures.PriceVariance,
            snapshottedAt);
    }

    /// <summary>
    /// A percentage, at two places, with an empty denominator reading 100 rather than zero or null.
    /// </summary>
    /// <remarks>
    /// A supplier who was sent nothing has not failed to deliver it. Zero would libel them on a report
    /// and drag a blended rating down for a period in which they did nothing wrong; null would push the
    /// same decision onto every consumer, one of which would eventually get it wrong.
    /// </remarks>
    private static decimal Rate(decimal numerator, decimal denominator)
        => denominator == 0m
            ? 100m
            : decimal.Round(numerator / denominator * 100m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The counted figures a <see cref="SupplierScorecard"/> is built from, before any rate is derived.
/// </summary>
/// <param name="OrdersPlaced">Orders issued to the supplier in the period.</param>
/// <param name="LinesOrdered">Order lines those orders carried.</param>
/// <param name="LinesDelivered">Lines with anything delivered against them.</param>
/// <param name="LinesDeliveredOnTime">Lines delivered on or before the order's expected date.</param>
/// <param name="LinesWithRejections">Deliveries with something turned away.</param>
/// <param name="QuantityOrdered">Quantity ordered. See <see cref="SupplierScorecard.QuantityOrdered"/> on the unit question.</param>
/// <param name="QuantityReceived">Quantity accepted into stock.</param>
/// <param name="QuantityRejected">Quantity turned away.</param>
/// <param name="PurchaseValue">What was bought, at order cost, excluding tax.</param>
/// <param name="PriceVariance">What released invoices over-billed by, net.</param>
public sealed record SupplierScorecardFigures(
    int OrdersPlaced,
    int LinesOrdered,
    int LinesDelivered,
    int LinesDeliveredOnTime,
    int LinesWithRejections,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal QuantityRejected,
    Money PurchaseValue,
    Money PriceVariance);
