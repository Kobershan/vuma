using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// A customer invoice — the AR sub-ledger's own document, reconciling to the AR control account.
/// </summary>
/// <remarks>
/// <para>
/// Not <see cref="IImmutableRecord"/>: unlike a <see cref="Journal"/>, an invoice has a real draft
/// phase (lines may be added and changed) and a real post-posting lifecycle (its
/// <see cref="OutstandingBalance"/> legitimately falls as receipts allocate against it). Immutability
/// after posting is enforced here, in the entity, by refusing any line change once
/// <see cref="Status"/> leaves <see cref="DocumentStatus.Draft"/> — not by the persistence-layer
/// blanket guard, which is for records with no legitimate mutation at all.
/// </para>
/// <para>
/// References its customer by <see cref="PartnerId"/> only. Stage 06 (master data) has not been
/// built yet; see the Scope note in <c>docs/stages/STAGE-07-finance.md</c>.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ArInvoice : Entity
{
    private readonly List<ArInvoiceLine> _lines = [];

    private ArInvoice(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ArInvoice()
    {
    }

    /// <summary>The customer this invoice is billed to.</summary>
    public PartnerId PartnerId { get; private set; }

    /// <summary>The per-tenant sequential invoice number.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The date on the invoice, used for ageing and period assignment.</summary>
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

    /// <summary>What remains unpaid. Equals <see cref="Total"/> at posting and falls as receipts allocate.</summary>
    public Money OutstandingBalance { get; private set; }

    /// <summary>The invoice's lines. Fixed once <see cref="Status"/> leaves <see cref="DocumentStatus.Draft"/>.</summary>
    public IReadOnlyList<ArInvoiceLine> Lines => _lines;

    /// <summary>Opens a new draft invoice.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store this invoice was raised from.</param>
    /// <param name="partnerId">The customer.</param>
    /// <param name="invoiceNumber">The per-tenant sequential invoice number.</param>
    /// <param name="invoiceDate">The date on the invoice.</param>
    /// <param name="dueDate">When payment is due.</param>
    /// <param name="currency">The ISO 4217 currency for the whole document.</param>
    public static ArInvoice Draft(
        Guid tenantId,
        Guid? storeId,
        PartnerId partnerId,
        string invoiceNumber,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new ArInvoice(tenantId, storeId)
        {
            PartnerId = partnerId,
            InvoiceNumber = invoiceNumber.Trim(),
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = DocumentStatus.Draft,
            Total = Money.Zero(currency),
            OutstandingBalance = Money.Zero(currency),
        };
    }

    /// <summary>Adds a line to the draft.</summary>
    /// <param name="description">What is being billed.</param>
    /// <param name="netAmount">The line's amount before tax.</param>
    /// <param name="taxCode">The <see cref="TaxRule"/> code applied, or empty for none.</param>
    /// <param name="taxAmount">The tax on this line.</param>
    /// <exception cref="DocumentNotDraftException">The invoice is no longer a draft.</exception>
    public void AddLine(string description, Money netAmount, string taxCode, Money taxAmount)
    {
        if (Status is not DocumentStatus.Draft)
        {
            throw new DocumentNotDraftException(nameof(ArInvoice), Id);
        }

        _lines.Add(ArInvoiceLine.Create(
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
            throw new DocumentNotDraftException(nameof(ArInvoice), Id);
        }

        if (_lines.Count == 0)
        {
            throw new DocumentHasNoLinesException(nameof(ArInvoice));
        }

        Total = _lines.Aggregate(Money.Zero(Currency), (sum, line) => sum + line.GrossAmount);
        OutstandingBalance = Total;
        JournalId = journalId;
        Status = DocumentStatus.Posted;
    }

    /// <summary>Reduces the outstanding balance as a receipt allocates against this invoice.</summary>
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
