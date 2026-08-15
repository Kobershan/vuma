using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>One line of an <see cref="ArInvoice"/>.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ArInvoiceLine : Entity
{
    private ArInvoiceLine(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ArInvoiceLine()
    {
    }

    /// <summary>The invoice this line belongs to.</summary>
    public Guid ArInvoiceId { get; private set; }

    /// <summary>The line's position on the printed invoice.</summary>
    public int LineNumber { get; private set; }

    /// <summary>What is being billed.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The line's amount before tax.</summary>
    public Money NetAmount { get; private set; }

    /// <summary>The <see cref="TaxRule"/> code applied, or empty for none.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>The tax on this line.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Net plus tax.</summary>
    public Money GrossAmount { get; private set; }

    /// <summary>Builds one line. Internal — use <see cref="ArInvoice.AddLine"/>.</summary>
    internal static ArInvoiceLine Create(
        Guid tenantId,
        Guid arInvoiceId,
        int lineNumber,
        string description,
        Money netAmount,
        string taxCode,
        Money taxAmount,
        Money grossAmount)
        => new(tenantId)
        {
            ArInvoiceId = arInvoiceId,
            LineNumber = lineNumber,
            Description = description?.Trim() ?? string.Empty,
            NetAmount = netAmount,
            TaxCode = taxCode?.Trim() ?? string.Empty,
            TaxAmount = taxAmount,
            GrossAmount = grossAmount,
        };
}
