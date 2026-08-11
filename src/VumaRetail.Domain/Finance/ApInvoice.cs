using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// A supplier invoice — the AP sub-ledger's own document, reconciling to the AP control account.
/// </summary>
/// <remarks>The AP mirror of <see cref="ArInvoice"/>; see its remarks for the immutability design.</remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ApInvoice : Entity
{
    private readonly List<ApInvoiceLine> _lines = [];

    private ApInvoice(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApInvoice()
    {
    }

    /// <summary>The supplier this invoice was billed by.</summary>
    public PartnerId PartnerId { get; private set; }

    /// <summary>The supplier's own invoice number, as they issued it.</summary>
    public string SupplierInvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The date on the supplier's invoice, used for ageing and period assignment.</summary>
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>When payment is due.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The invoice's currency. Every line and total shares it.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Where the invoice sits in its lifecycle.</summary>
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;

    /// <summary>The GL journal this invoice posted, once posted.</summary>
    public Guid? JournalId { get; private set; }

    /// <summary>The invoice total including tax. Zero until posted.</summary>
    public Money Total { get; private set; }

    /// <summary>What remains unpaid. Equals <see cref="Total"/> at posting and falls as payments allocate.</summary>
    public Money OutstandingBalance { get; private set; }

    /// <summary>The invoice's lines. Fixed once <see cref="Status"/> leaves <see cref="DocumentStatus.Draft"/>.</summary>
    public IReadOnlyList<ApInvoiceLine> Lines => _lines;

    /// <summary>Opens a new draft invoice.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store this invoice was captured at.</param>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="supplierInvoiceNumber">The supplier's own invoice number.</param>
    /// <param name="invoiceDate">The date on the supplier's invoice.</param>
    /// <param name="dueDate">When payment is due.</param>
    /// <param name="currency">The ISO 4217 currency for the whole document.</param>
    public static ApInvoice Draft(
        Guid tenantId,
        Guid? storeId,
        PartnerId partnerId,
        string supplierInvoiceNumber,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierInvoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new ApInvoice(tenantId, storeId)
        {
            PartnerId = partnerId,
            SupplierInvoiceNumber = supplierInvoiceNumber.Trim(),
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = DocumentStatus.Draft,
            Total = Money.Zero(currency),
            OutstandingBalance = Money.Zero(currency),
        };
    }

    /// <summary>Adds a line to the draft.</summary>
    /// <param name="description">What was billed.</param>
    /// <param name="netAmount">The line's amount before tax.</param>
    /// <param name="taxCode">The <see cref="TaxRule"/> code applied, or empty for none.</param>
    /// <param name="taxAmount">The tax on this line.</param>
    /// <exception cref="DocumentNotDraftException">The invoice is no longer a draft.</exception>
    public void AddLine(string description, Money netAmount, string taxCode, Money taxAmount)
    {
        if (Status is not DocumentStatus.Draft)
        {
            throw new DocumentNotDraftException(nameof(ApInvoice), Id);
        }

        _lines.Add(ApInvoiceLine.Create(
            TenantId, Id, _lines.Count + 1, description, netAmount, taxCode, taxAmount, netAmount + taxAmount));
    }

    /// <summary>
    /// Posts the invoice: freezes its lines, totals them and sets the outstanding balance to the total.
    /// </summary>
    /// <param name="journalId">The GL journal the posting rules engine produced for this invoice.</param>
    /// <exception cref="DocumentNotDraftException">The invoice is not a draft.</exception>
    /// <exception cref="DocumentHasNoLinesException">The invoice has no lines.</exception>
    public void Post(Guid journalId)
    {
        if (Status is not DocumentStatus.Draft)
        {
            throw new DocumentNotDraftException(nameof(ApInvoice), Id);
        }

        if (_lines.Count == 0)
        {
            throw new DocumentHasNoLinesException(nameof(ApInvoice));
        }

        Total = _lines.Aggregate(Money.Zero(Currency), (sum, line) => sum + line.GrossAmount);
        OutstandingBalance = Total;
        JournalId = journalId;
        Status = DocumentStatus.Posted;
    }

    /// <summary>Reduces the outstanding balance as a payment allocates against this invoice.</summary>
    /// <param name="amount">The amount allocated.</param>
    /// <exception cref="OverAllocationException">The amount exceeds what remains outstanding.</exception>
    public void Allocate(Money amount)
    {
        if (amount > OutstandingBalance)
        {
            throw new OverAllocationException(amount, OutstandingBalance);
        }

        OutstandingBalance -= amount;

        if (OutstandingBalance.IsZero)
        {
            Status = DocumentStatus.Settled;
        }
    }
}
