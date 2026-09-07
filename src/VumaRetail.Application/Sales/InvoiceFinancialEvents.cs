using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.Application.Sales;

/// <summary>
/// The one financial fact a posted invoice raises: this invoice, in this company, for this net,
/// tax and gross.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §7 rule 12, exactly as <c>SaleTenderedEvent</c> honours it: there is no field a GL
/// account code could go in. The event says a customer now owes the company money and how much of it
/// is tax; whether that debits trade debtors and credits sales and output VAT is a posting rule a
/// tenant owns (ADR-016). Each company's invoice posts through that company's own rules, in that
/// company's own database — a split never posts one journal across companies.
/// </remarks>
/// <param name="TenantId">The tenant the invoice belongs to.</param>
/// <param name="StoreId">The store it was raised at.</param>
/// <param name="CompanyId">The company whose books take it.</param>
/// <param name="InvoiceId">The invoice.</param>
/// <param name="InvoiceNumber">Its number in the company's <c>INV</c> series.</param>
/// <param name="SourceType">Which operational fact it documents.</param>
/// <param name="Net">The sale excluding tax.</param>
/// <param name="Tax">The stored output tax (ADR-075).</param>
/// <param name="Gross">What the customer owes.</param>
/// <param name="OccurredAt">When the invoice posted, UTC.</param>
public sealed record InvoicePostedEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid CompanyId,
    Guid InvoiceId,
    string InvoiceNumber,
    InvoiceSourceType SourceType,
    Money Net,
    Money Tax,
    Money Gross,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where sales raises the financial event a posted invoice produces, for whatever turns it into a
/// balanced journal.
/// </summary>
/// <remarks>
/// The same port shape, and the same two reasons, as <c>ISaleFinancialEventPublisher</c>:
/// <c>VumaRetail.Application</c> may not reference <c>VumaRetail.Finance</c>, and a host wired without
/// a finance module must still be able to invoice rather than fail to build a container.
/// </remarks>
public interface IInvoiceFinancialEventPublisher
{
    /// <summary>Raises one invoice event.</summary>
    /// <param name="invoiceEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(InvoicePostedEvent invoiceEvent, CancellationToken cancellationToken = default);
}
