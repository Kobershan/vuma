namespace VumaRetail.Domain.Sales.Invoices;

/// <summary>The lifecycle of an invoice. Posted is immutable (ADR-012).</summary>
public enum InvoiceStatus
{
    /// <summary>Being built. Lines may be added; nothing is owed yet.</summary>
    Draft = 0,

    /// <summary>Finalized and posted to the ledger. Immutable — correct via credit note (Stage 10).</summary>
    Posted = 1,

    /// <summary>Abandoned before posting. Nothing was owed and nothing moved.</summary>
    Cancelled = 2
}
