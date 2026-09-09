using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Planning;

/// <summary>Live procurement commitments against a budget currency.</summary>
/// <param name="Committed">Committed total in the budget currency.</param>
/// <param name="Currency">The budget currency.</param>
/// <param name="DocumentsCount">How many open documents contributed.</param>
/// <param name="AsAt">When the commitments were read, UTC.</param>
public sealed record OtbCommitments(decimal Committed, string Currency, int DocumentsCount, DateTimeOffset AsAt);

/// <summary>Reads live procurement commitments. Cancelled documents never count.</summary>
public interface IOtbCommitmentReader
{
    /// <summary>Sums submitted/approved requisitions and open orders in one currency.</summary>
    Task<OtbCommitments> ReadCommittedAsync(
        string currency, DateTimeOffset asAt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Live open-to-buy commitments (ADR-149): submitted and approved requisitions at their
/// estimated cost, plus approved/issued/partially-received orders at outstanding value.
/// Drafts are intent, cancelled/closed/rejected documents are gone, received goods are stock.
/// Only lines in the budget currency count — converting needs a rate, which is Finance's job.
/// </summary>
public sealed class OtbCommitmentReader(
    IPurchaseRequisitionRepository requisitions,
    IPurchaseOrderRepository orders) : IOtbCommitmentReader
{
    private const int PageSize = 200;

    /// <inheritdoc />
    public async Task<OtbCommitments> ReadCommittedAsync(
        string currency, DateTimeOffset asAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        decimal committed = 0m;
        int documents = 0;

        foreach (PurchaseRequisitionStatus status in
                 new[] { PurchaseRequisitionStatus.Submitted, PurchaseRequisitionStatus.Approved })
        {
            KeysetCursor? after = null;

            while (true)
            {
                (IReadOnlyList<PurchaseRequisition> items, bool hasMore) = await requisitions
                    .ListPageAsync(status, after, PageSize, cancellationToken)
                    .ConfigureAwait(false);

                foreach (PurchaseRequisition requisition in items)
                {
                    decimal lines = 0m;
                    bool counted = false;

                    foreach (var line in requisition.Lines)
                    {
                        if (line.EstimatedUnitCost is { } estimate
                            && string.Equals(estimate.Currency, currency, StringComparison.Ordinal))
                        {
                            lines += estimate.Amount * line.Quantity.Value;
                            counted = true;
                        }
                    }

                    if (counted)
                    {
                        committed += lines;
                        documents++;
                    }
                }

                if (!hasMore || items.Count == 0)
                {
                    break;
                }

                after = CursorAfter(items[^1]);
            }
        }

        foreach (PurchaseOrderStatus status in
                 new[] { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Issued, PurchaseOrderStatus.PartiallyReceived })
        {
            KeysetCursor? after = null;

            while (true)
            {
                (IReadOnlyList<PurchaseOrder> items, bool hasMore) = await orders
                    .ListPageAsync(null, status, after, PageSize, cancellationToken)
                    .ConfigureAwait(false);

                foreach (PurchaseOrder order in items)
                {
                    decimal lines = 0m;
                    bool counted = false;

                    foreach (var line in order.Lines)
                    {
                        if (string.Equals(line.UnitCost.Currency, currency, StringComparison.Ordinal))
                        {
                            decimal outstanding = line.Quantity.Value - line.ReceivedQuantity.Value;

                            if (outstanding > 0m)
                            {
                                lines += line.UnitCost.Amount * outstanding;
                                counted = true;
                            }
                        }
                    }

                    if (counted)
                    {
                        committed += lines;
                        documents++;
                    }
                }

                if (!hasMore || items.Count == 0)
                {
                    break;
                }

                after = CursorAfter(items[^1]);
            }
        }

        return new OtbCommitments(committed, currency, documents, asAt);
    }

    private static KeysetCursor CursorAfter(Domain.Entities.Entity entity)
        => new(entity.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture), entity.Id);
}

