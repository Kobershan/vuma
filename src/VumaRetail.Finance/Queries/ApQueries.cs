using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Queries;

/// <summary>One open supplier invoice, as read for a listing.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on the supplier's invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The invoice's currency.</param>
/// <param name="Total">The invoice total including tax.</param>
/// <param name="OutstandingBalance">What remains unpaid.</param>
/// <param name="Status">Where the invoice sits in its lifecycle.</param>
public sealed record ApInvoiceView(
    Guid Id, Guid PartnerId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate,
    string Currency, decimal Total, decimal OutstandingBalance, DocumentStatus Status);

/// <summary>Lists every AP invoice that still has a balance outstanding.</summary>
public sealed record ListOpenApInvoicesQuery : IQuery<IReadOnlyList<ApInvoiceView>>;

/// <summary>Handles <see cref="ListOpenApInvoicesQuery"/>.</summary>
/// <param name="invoices">The AP sub-ledger.</param>
public sealed class ListOpenApInvoicesQueryHandler(IApInvoiceRepository invoices)
    : IQueryHandler<ListOpenApInvoicesQuery, IReadOnlyList<ApInvoiceView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApInvoiceView>> HandleAsync(
        ListOpenApInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApInvoice> open = await invoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        return [.. open.Select(i => new ApInvoiceView(
            i.Id, i.PartnerId.Value, i.SupplierInvoiceNumber, i.InvoiceDate, i.DueDate,
            i.Currency, i.Total.Amount, i.OutstandingBalance.Amount, i.Status))];
    }
}

/// <summary>The AP ageing report as of a date. Shares its bucket shape with <see cref="AgeingRow"/>.</summary>
/// <param name="AsOf">The date to age against.</param>
public sealed record GetApAgeingQuery(DateOnly AsOf) : IQuery<IReadOnlyList<AgeingRow>>;

/// <summary>Handles <see cref="GetApAgeingQuery"/>.</summary>
/// <param name="invoices">The AP sub-ledger.</param>
public sealed class GetApAgeingQueryHandler(IApInvoiceRepository invoices)
    : IQueryHandler<GetApAgeingQuery, IReadOnlyList<AgeingRow>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AgeingRow>> HandleAsync(
        GetApAgeingQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApInvoice> open = await invoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        return AgeingBucketer.Bucket(
            open.Select(i => (i.PartnerId.Value, i.DueDate, i.OutstandingBalance.Amount)), query.AsOf);
    }
}
