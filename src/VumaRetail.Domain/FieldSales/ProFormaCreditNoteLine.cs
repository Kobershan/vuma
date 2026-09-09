using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>One proposed credit line against one original invoice line.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProFormaCreditNoteLine : Entity
{
    private ProFormaCreditNoteLine(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ProFormaCreditNoteLine()
    {
    }

    /// <summary>The credit proposal.</summary>
    public Guid ProFormaCreditNoteId { get; private set; }

    /// <summary>The original invoice line being credited.</summary>
    public Guid OriginalInvoiceLineId { get; private set; }

    /// <summary>The item, when the line is not a variant. Exactly one of the two is set.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, when the line is one. Exactly one of the two is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much comes back.</summary>
    public decimal QuantityValue { get; private set; }

    /// <summary>Unit of measure.</summary>
    public string QuantityUom { get; private set; } = string.Empty;

    /// <summary>Credited unit price (the invoice's figure, not a new price).</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Credited tax.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Credited net.</summary>
    public Money Net { get; private set; }

    /// <summary>Line gross — net plus tax.</summary>
    public Money Gross => Net + TaxAmount;

    /// <summary>Proposes one credit line.</summary>
    public static ProFormaCreditNoteLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid? companyId,
        Guid creditNoteId,
        Guid originalInvoiceLineId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money taxAmount,
        Money net,
        string currency)
    {
        if (tenantId == Guid.Empty || creditNoteId == Guid.Empty || originalInvoiceLineId == Guid.Empty)
        {
            throw new ArgumentException("A credit line must belong to a tenant, a proposal and an invoice line.");
        }

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw new ArgumentException("A line names exactly one of item or variant.");
        }

        if (quantityValue <= 0m)
        {
            throw new ArgumentException("A line quantity must be positive.", nameof(quantityValue));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(quantityUom);

        foreach (Money amount in new[] { unitPrice, taxAmount, net })
        {
            if (!string.Equals(amount.Currency, currency, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Line amounts must be in {currency}.");
            }
        }

        return new ProFormaCreditNoteLine(tenantId, storeId)
        {
            CompanyId = companyId,
            ProFormaCreditNoteId = creditNoteId,
            OriginalInvoiceLineId = originalInvoiceLineId,
            ItemId = itemId,
            ItemVariantId = itemVariantId,
            QuantityValue = quantityValue,
            QuantityUom = quantityUom.Trim(),
            UnitPrice = unitPrice,
            TaxAmount = taxAmount,
            Net = net,
        };
    }
}
