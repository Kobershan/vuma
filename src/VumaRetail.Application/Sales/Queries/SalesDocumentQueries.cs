using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.Application.Sales.Queries;

/// <summary>One quote, with its snapshotted lines.</summary>
/// <param name="QuoteId">The quote.</param>
public sealed record GetQuoteQuery(Guid QuoteId) : IQuery<Quote>;

/// <summary>Reads the quote.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class GetQuoteQueryHandler(
    IQuoteRepository quotes) : IQueryHandler<GetQuoteQuery, Quote>
{
    /// <inheritdoc />
    public async Task<Quote> HandleAsync(GetQuoteQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await quotes.FindAsync(query.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", query.QuoteId);
    }
}

/// <summary>Quotes, optionally narrowed.</summary>
/// <param name="Status">Narrow to one lifecycle status, or <c>null</c> for all.</param>
/// <param name="CustomerId">Narrow to one customer, or <c>null</c> for all.</param>
public sealed record ListQuotesQuery(QuoteStatus? Status = null, Guid? CustomerId = null)
    : IQuery<IReadOnlyList<Quote>>;

/// <summary>Reads the quotes.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class ListQuotesQueryHandler(
    IQuoteRepository quotes) : IQueryHandler<ListQuotesQuery, IReadOnlyList<Quote>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Quote>> HandleAsync(
        ListQuotesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.CustomerId.HasValue)
        {
            IReadOnlyList<Quote> mine =
                await quotes.ListForCustomerAsync(query.CustomerId.Value, cancellationToken).ConfigureAwait(false);
            return query.Status.HasValue
                ? mine.Where(quote => quote.Status == query.Status.Value).ToList()
                : mine;
        }

        return await quotes.ListAsync(query.Status, null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>One invoice, with its frozen lines.</summary>
/// <param name="InvoiceId">The invoice.</param>
/// <remarks>
/// Company-scoped: when the scope holds an acting company and the invoice belongs to another,
/// the answer is "not found" rather than "forbidden" — existence itself must not leak.
/// </remarks>
public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<Invoice>;

/// <summary>Reads the invoice inside the caller's company.</summary>
/// <param name="invoices">Invoice lookup.</param>
/// <param name="company">The ambient acting company.</param>
public sealed class GetInvoiceQueryHandler(
    IInvoiceRepository invoices,
    ICompanyContext company) : IQueryHandler<GetInvoiceQuery, Invoice>
{
    /// <inheritdoc />
    public async Task<Invoice> HandleAsync(GetInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Invoice invoice = await invoices.FindAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvoicesNotFoundException("invoice", query.InvoiceId);

        if (company.CompanyId is { } bound
            && invoice.CompanyId.HasValue
            && invoice.CompanyId.Value != bound)
        {
            throw new InvoicesNotFoundException("invoice", query.InvoiceId);
        }

        return invoice;
    }
}

/// <summary>Invoices for one company, newest first.</summary>
/// <param name="CompanyId">The company. Must agree with the acting company when one is bound.</param>
public sealed record ListInvoicesQuery(Guid CompanyId) : IQuery<IReadOnlyList<Invoice>>;

/// <summary>Reads the company's invoices.</summary>
/// <param name="invoices">Invoice lookup.</param>
/// <param name="company">The ambient acting company.</param>
public sealed class ListInvoicesQueryHandler(
    IInvoiceRepository invoices,
    ICompanyContext company) : IQueryHandler<ListInvoicesQuery, IReadOnlyList<Invoice>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> HandleAsync(
        ListInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.CompanyId == Guid.Empty)
        {
            throw new InvoicesRuleException(
                "INVOICE_COMPANY_REQUIRED", "Listing invoices needs its company.");
        }

        if (company.CompanyId is { } bound && bound != query.CompanyId)
        {
            throw InvoicesRuleException.CompanyNotAuthorized(query.CompanyId);
        }

        return await invoices.ListForCompanyAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
    }
}
