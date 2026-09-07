namespace VumaRetail.Domain.Sales.Invoices;

/// <summary>Which operational fact an invoice documents.</summary>
public enum InvoiceSourceType
{
    /// <summary>A Stage 14 order, possibly one segment of a multi-company split.</summary>
    Order = 0,

    /// <summary>A Stage 09 till sale.</summary>
    Sale = 1,

    /// <summary>A Stage 10c quote, converted.</summary>
    Quote = 2
}
