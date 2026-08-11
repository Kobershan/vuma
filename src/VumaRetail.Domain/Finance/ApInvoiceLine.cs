using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>One line of an <see cref="ApInvoice"/>.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ApInvoiceLine : Entity
{
    private ApInvoiceLine(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApInvoiceLine()
    {
    }

    /// <summary>The invoice this line belongs to.</summary>
    public Guid ApInvoiceId { get; private set; }

    /// <summary>The line's position on the captured invoice.</summary>
    public int LineNumber { get; private set; }

    /// <summary>What was billed.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The line's amount before tax.</summary>
    public Money NetAmount { get; private set; }

    /// <summary>The <see cref="TaxRule"/> code applied, or empty for none.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>The tax on this line.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Net plus tax.</summary>
    public Money GrossAmount { get; private set; }

    /// <summary>Builds one line. Internal — use <see cref="ApInvoice.AddLine"/>.</summary>
    internal static ApInvoiceLine Create(
        Guid tenantId,
        Guid apInvoiceId,
        int lineNumber,
        string description,
        Money netAmount,
        string taxCode,
        Money taxAmount,
        Money grossAmount)
        => new(tenantId)
        {
            ApInvoiceId = apInvoiceId,
            LineNumber = lineNumber,
            Description = description?.Trim() ?? string.Empty,
            NetAmount = netAmount,
            TaxCode = taxCode?.Trim() ?? string.Empty,
            TaxAmount = taxAmount,
            GrossAmount = grossAmount,
        };
}
