using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
#pragma warning disable CS1591

namespace VumaRetail.Application.Sales.Services;

public sealed class InvoiceService
{
    private readonly IInvoiceRepository _invoices;

    public InvoiceService(IInvoiceRepository invoices)
    {
        _invoices = invoices;
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesForCompanyAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        return await _invoices.ListForCompanyAsync(companyId, cancellationToken);
    }
}
