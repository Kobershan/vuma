using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement;

/// <summary>
/// Folds one supplier's orders, deliveries and released invoices over a closed period into the
/// counted figures a scorecard is built from.
/// </summary>
/// <remarks>
/// <para>
/// The counting is here rather than in the entity because it spans four aggregates, and an entity that
/// reached across three repositories to construct itself would be a repository with a constructor. The
/// entity takes the finished figures and derives the rates, which is arithmetic it can do on its own.
/// </para>
/// <para>
/// <b>What "in the period" means, and why it is the order's expected date.</b> An order issued in
/// March for April delivery is April's performance, not March's — the supplier's job was to deliver on
/// the promised date, and filing it under the month somebody typed the order in would judge them for a
/// window they were never asked to hit.
/// </para>
/// </remarks>
public interface ISupplierScorecardCalculator
{
    /// <summary>Counts one supplier's performance over a period.</summary>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="periodStart">The period's first day, inclusive.</param>
    /// <param name="periodEnd">The period's last day, inclusive.</param>
    /// <param name="currency">The currency to report the money figures in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The counted figures. A supplier with no orders in the period comes back as zeroes rather than
    /// <c>null</c> — "we sent them nothing" is a fact worth recording, and it is not the same as "we
    /// have no data".
    /// </returns>
    Task<SupplierScorecardFigures> CalculateAsync(
        Guid partnerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string currency,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISupplierScorecardCalculator" />
/// <param name="orders">The orders issued to the supplier in the period.</param>
/// <param name="receipts">What actually arrived against them.</param>
/// <param name="matches">What their released invoices claimed.</param>
public sealed class SupplierScorecardCalculator(
    IPurchaseOrderRepository orders,
    IGoodsReceiptRepository receipts,
    ISupplierInvoiceMatchRepository matches) : ISupplierScorecardCalculator
{
    /// <inheritdoc />
    public async Task<SupplierScorecardFigures> CalculateAsync(
        Guid partnerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string currency,
        CancellationToken cancellationToken = default)
    {
        Money zero = Money.Zero(currency);

        IReadOnlyList<PurchaseOrder> inPeriod = await orders
            .ListForSupplierPeriodAsync(partnerId, periodStart, periodEnd, cancellationToken)
            .ConfigureAwait(false);

        // A cancelled order is not a delivery the supplier failed to make. Counting one would mean a
        // buyer could tank a supplier's rating by raising and withdrawing orders.
        List<PurchaseOrder> counted = [.. inPeriod.Where(order
            => order.Status is not (PurchaseOrderStatus.Draft
                or PurchaseOrderStatus.Approved
                or PurchaseOrderStatus.Cancelled))];

        if (counted.Count == 0)
        {
            return new SupplierScorecardFigures(0, 0, 0, 0, 0, 0m, 0m, 0m, zero, zero);
        }

        IReadOnlyList<GoodsReceipt> delivered = await receipts
            .ListForOrdersAsync([.. counted.Select(order => order.Id)], cancellationToken)
            .ConfigureAwait(false);

        ScorecardTally tally = TallyDeliveries(counted, delivered);

        Money purchaseValue = zero;

        foreach (PurchaseOrder order in counted)
        {
            // Only orders denominated in the reporting currency contribute to the money figures. Adding a
            // dollar order into a rand total needs a rate and a rate date, which is Finance's job — and a
            // scorecard that silently added them would be reporting a number that is not any currency.
            if (string.Equals(order.Currency, currency, StringComparison.Ordinal))
            {
                purchaseValue += order.Net;
            }
        }

        Money priceVariance = await SumPriceVarianceAsync(counted, currency, cancellationToken)
            .ConfigureAwait(false);

        return new SupplierScorecardFigures(
            counted.Count,
            tally.LinesOrdered,
            tally.LinesDelivered,
            tally.LinesOnTime,
            tally.LinesWithRejections,
            tally.QuantityOrdered,
            tally.QuantityReceived,
            tally.QuantityRejected,
            purchaseValue,
            priceVariance);
    }

    /// <summary>
    /// Walks the orders and their deliveries once, counting lines and quantities together so the two
    /// cannot drift apart.
    /// </summary>
    private static ScorecardTally TallyDeliveries(
        IReadOnlyList<PurchaseOrder> counted, IReadOnlyList<GoodsReceipt> delivered)
    {
        Dictionary<Guid, List<GoodsReceiptLine>> byOrderLine = [];

        foreach (GoodsReceipt receipt in delivered.Where(candidate
            => candidate.Status is GoodsReceiptStatus.Completed))
        {
            foreach (GoodsReceiptLine line in receipt.Lines)
            {
                if (!byOrderLine.TryGetValue(line.PurchaseOrderLineId, out List<GoodsReceiptLine>? lines))
                {
                    lines = [];
                    byOrderLine[line.PurchaseOrderLineId] = lines;
                }

                lines.Add(line);
            }
        }

        Dictionary<Guid, DateOnly> expectedByOrder = counted.ToDictionary(
            order => order.Id, order => order.ExpectedAt);

        ScorecardTally tally = new();

        foreach (PurchaseOrder order in counted)
        {
            DateOnly expected = expectedByOrder[order.Id];

            foreach (PurchaseOrderLine orderLine in order.Lines)
            {
                tally.LinesOrdered++;
                tally.QuantityOrdered += orderLine.Quantity.Value;

                if (!byOrderLine.TryGetValue(orderLine.Id, out List<GoodsReceiptLine>? lines))
                {
                    continue;
                }

                tally.LinesDelivered++;

                foreach (GoodsReceiptLine line in lines)
                {
                    tally.QuantityReceived += line.AcceptedQuantity.Value;
                    tally.QuantityRejected += line.RejectedQuantity.Value;
                }

                if (lines.Exists(line => !line.RejectedQuantity.IsZero))
                {
                    tally.LinesWithRejections++;
                }

                // On time means the *first* delivery against the line landed on or before the promised
                // date. A supplier who delivers half on the day and the rest a month late has not
                // delivered on time, and measuring the last receipt instead would say they had — but
                // measuring every receipt separately would punish a split delivery twice for one failure.
                DateOnly firstDelivery = lines.Min(line => DateOnly.FromDateTime(line.CreatedAt.UtcDateTime));

                if (firstDelivery <= expected)
                {
                    tally.LinesOnTime++;
                }
            }
        }

        return tally;
    }

    private async Task<Money> SumPriceVarianceAsync(
        IReadOnlyList<PurchaseOrder> counted, string currency, CancellationToken cancellationToken)
    {
        Money variance = Money.Zero(currency);

        foreach (PurchaseOrder order in counted.Where(order
            => string.Equals(order.Currency, currency, StringComparison.Ordinal)))
        {
            IReadOnlyList<SupplierInvoiceMatch> orderMatches = await matches
                .ListForOrderAsync(order.Id, cancellationToken)
                .ConfigureAwait(false);

            // Released only. A blocked match is a bill nobody paid, and counting its variance would
            // report the supplier as having over-charged by an amount the shop successfully refused.
            foreach (SupplierInvoiceMatch match in orderMatches.Where(candidate => candidate.IsReleased))
            {
                variance += match.PriceVariance;
            }
        }

        return variance;
    }

    /// <summary>The running counters, mutable inside one calculation and never escaping it.</summary>
    private sealed class ScorecardTally
    {
        internal int LinesOrdered { get; set; }

        internal int LinesDelivered { get; set; }

        internal int LinesOnTime { get; set; }

        internal int LinesWithRejections { get; set; }

        internal decimal QuantityOrdered { get; set; }

        internal decimal QuantityReceived { get; set; }

        internal decimal QuantityRejected { get; set; }
    }
}
