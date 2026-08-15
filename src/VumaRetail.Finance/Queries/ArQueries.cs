using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Queries;

/// <summary>One open customer invoice, as read for a listing.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="PartnerId">The customer.</param>
/// <param name="InvoiceNumber">The invoice number.</param>
/// <param name="InvoiceDate">The date on the invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The invoice's currency.</param>
/// <param name="Total">The invoice total including tax.</param>
/// <param name="OutstandingBalance">What remains unpaid.</param>
/// <param name="Status">Where the invoice sits in its lifecycle.</param>
public sealed record ArInvoiceView(
    Guid Id, Guid PartnerId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate,
    string Currency, decimal Total, decimal OutstandingBalance, DocumentStatus Status);

/// <summary>Lists every AR invoice that still has a balance outstanding.</summary>
public sealed record ListOpenArInvoicesQuery : IQuery<IReadOnlyList<ArInvoiceView>>;

/// <summary>Handles <see cref="ListOpenArInvoicesQuery"/>.</summary>
/// <param name="invoices">The AR sub-ledger.</param>
public sealed class ListOpenArInvoicesQueryHandler(IArInvoiceRepository invoices)
    : Application.Abstractions.IQueryHandler<ListOpenArInvoicesQuery, IReadOnlyList<ArInvoiceView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ArInvoiceView>> HandleAsync(
        ListOpenArInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArInvoice> open = await invoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        return [.. open.Select(i => new ArInvoiceView(
            i.Id, i.PartnerId.Value, i.InvoiceNumber, i.InvoiceDate, i.DueDate,
            i.Currency, i.Total.Amount, i.OutstandingBalance.Amount, i.Status))];
    }
}

/// <summary>One customer's ageing, bucketed by how overdue each open invoice is.</summary>
/// <param name="PartnerId">The customer.</param>
/// <param name="Current">Not yet due.</param>
/// <param name="Days30">1–30 days overdue.</param>
/// <param name="Days60">31–60 days overdue.</param>
/// <param name="Days90">61–90 days overdue.</param>
/// <param name="Days90Plus">More than 90 days overdue.</param>
/// <param name="Total">The sum of every bucket.</param>
public sealed record AgeingRow(
    Guid PartnerId, decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus, decimal Total);

/// <summary>The AR ageing report as of a date.</summary>
/// <param name="AsOf">The date to age against.</param>
public sealed record GetArAgeingQuery(DateOnly AsOf) : IQuery<IReadOnlyList<AgeingRow>>;

/// <summary>Handles <see cref="GetArAgeingQuery"/>.</summary>
/// <param name="invoices">The AR sub-ledger.</param>
public sealed class GetArAgeingQueryHandler(IArInvoiceRepository invoices)
    : Application.Abstractions.IQueryHandler<GetArAgeingQuery, IReadOnlyList<AgeingRow>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AgeingRow>> HandleAsync(
        GetArAgeingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ArInvoice> open = await invoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        return AgeingBucketer.Bucket(
            open.Select(i => (i.PartnerId.Value, i.DueDate, i.OutstandingBalance.Amount)), query.AsOf);
    }
}

/// <summary>Buckets open balances by how many days overdue they are — shared by AR and AP ageing.</summary>
internal static class AgeingBucketer
{
    /// <summary>Groups and buckets a set of (partner, due date, balance) tuples.</summary>
    /// <param name="openBalances">One entry per open document.</param>
    /// <param name="asOf">The date to age against.</param>
    public static IReadOnlyList<AgeingRow> Bucket(
        IEnumerable<(Guid PartnerId, DateOnly DueDate, decimal Balance)> openBalances, DateOnly asOf)
        => [.. openBalances
            .GroupBy(entry => entry.PartnerId)
            .Select(group =>
            {
                decimal current = 0m, days30 = 0m, days60 = 0m, days90 = 0m, days90Plus = 0m;

                foreach ((Guid _, DateOnly dueDate, decimal balance) in group)
                {
                    int daysOverdue = asOf.DayNumber - dueDate.DayNumber;

                    if (daysOverdue <= 0)
                    {
                        current += balance;
                    }
                    else if (daysOverdue <= 30)
                    {
                        days30 += balance;
                    }
                    else if (daysOverdue <= 60)
                    {
                        days60 += balance;
                    }
                    else if (daysOverdue <= 90)
                    {
                        days90 += balance;
                    }
                    else
                    {
                        days90Plus += balance;
                    }
                }

                return new AgeingRow(
                    group.Key, current, days30, days60, days90, days90Plus,
                    current + days30 + days60 + days90 + days90Plus);
            })];
}
