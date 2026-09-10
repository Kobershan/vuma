using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Invoices;

/// <summary>
/// The legally binding document: what was sold, by which company, with tax and pack sizes frozen
/// at the moment of sale (ADR-075, ADR-112). Immutable once posted (ADR-012).
/// </summary>
/// <remarks>
/// One order, N invoices (ADR-102): when Stage 08c sources an order across companies, each
/// supplying company writes its own invoice entirely inside its own database, sharing only the
/// <see cref="GroupDocumentRef"/>. Each invoice carries its own company's number, postings and AR.
/// </remarks>
/// <remarks>
/// Deliberately not <see cref="IImmutableRecord"/> — like <c>Sale</c>, an invoice is mutable while
/// a draft and frozen by its status afterwards. Posting is one-way and terminal; the
/// <see cref="EnsureDraft"/> guard is the immutability contract.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Invoice : Entity
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice(
        Guid tenantId,
        Guid? storeId,
        string invoiceNumber,
        string sourceDocumentRef,
        InvoiceSourceType sourceType,
        Guid customerId,
        string currency,
        InvoiceStatus status,
        string? groupDocumentRef)
        : base(tenantId, storeId)
    {
        InvoiceNumber = invoiceNumber;
        SourceDocumentRef = sourceDocumentRef;
        SourceDocumentType = sourceType;
        CustomerId = customerId;
        Currency = currency;
        Status = status;
        GroupDocumentRef = groupDocumentRef;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    private Invoice()
    {
    }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>INV</c>, per company.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The order or sale this invoice documents. A bare id, never a cross-schema key.</summary>
    public string SourceDocumentRef { get; private set; } = string.Empty;

    /// <summary>Which kind of operational fact this invoice documents.</summary>
    public InvoiceSourceType SourceDocumentType { get; private set; }

    /// <summary>The customer who owes.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Where the invoice stands. Posted is terminal and immutable.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>The ISO 4217 currency. One invoice, one currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The sum of the lines' nets. A reported figure, recomputed as lines are added.</summary>
    public Money Net { get; private set; }

    /// <summary>The sum of the lines' stored taxes (ADR-075). Never recomputed from the total.</summary>
    public Money Tax { get; private set; }

    /// <summary>Net plus tax. What the customer owes this company.</summary>
    public Money Gross { get; private set; }

    /// <summary>When the invoice posted, UTC. Null until then.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>
    /// The multi-company order this invoice is one segment of, when it is one (ADR-102).
    /// Every segment of the split carries the same reference.
    /// </summary>
    public string? GroupDocumentRef { get; private set; }

    /// <summary>
    /// How the source order settles, inherited at generation (ADR-111). A cash-on-delivery invoice
    /// prints the terms; the dispatch gate itself lives on the order, not here.
    /// </summary>
    public string SettlementTerms { get; private set; } = "Standard";

    /// <summary>The frozen lines.</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>Opens a draft invoice in one company's books.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the invoice belongs to one.</param>
    /// <param name="invoiceNumber">The next number in the company's <c>INV</c> series.</param>
    /// <param name="companyId">The company whose stock, postings and AR this invoice is.</param>
    /// <param name="sourceDocumentRef">The order or sale being documented.</param>
    /// <param name="sourceType">Which kind of document that is.</param>
    /// <param name="customerId">The customer who owes.</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    /// <param name="groupDocumentRef">The split's shared reference, when this is one segment of N.</param>
    /// <param name="settlementTerms">How the source order settles (ADR-111); "Standard" unless set.</param>
    public static Invoice Create(
        Guid tenantId,
        Guid? storeId,
        string invoiceNumber,
        Guid companyId,
        string sourceDocumentRef,
        InvoiceSourceType sourceType,
        Guid customerId,
        string currency,
        string? groupDocumentRef = null,
        string settlementTerms = "Standard")
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must belong to a company.", nameof(companyId));
        }

        var invoice = new Invoice(
            tenantId, storeId, invoiceNumber.Trim(),
            sourceDocumentRef, sourceType, customerId, currency, InvoiceStatus.Draft, groupDocumentRef);
        invoice.AssignCompany(companyId);
        invoice.SettlementTerms = string.IsNullOrWhiteSpace(settlementTerms) ? "Standard" : settlementTerms.Trim();

        return invoice;
    }

    /// <summary>Adds a frozen line to a draft, recomputing the totals from the stored lines.</summary>
    /// <param name="line">The line, already priced, taxed and pack-snapped.</param>
    public void AddLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureDraft();
        _lines.Add(line);
        RecalculateTotals();
    }

    /// <summary>Finalizes the invoice: freezes it and posts it to the ledger. Irreversible.</summary>
    /// <param name="now">The instant of posting, UTC, from <c>IClock</c>.</param>
    public void Post(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw InvoicesRuleException.InvoiceNotDraft(Status);
        }

        if (_lines.Count == 0)
        {
            throw InvoicesRuleException.InvoiceMustHaveLines();
        }

        Status = InvoiceStatus.Posted;
        PostedAt = now;
    }

    /// <summary>Abandons a draft. A posted invoice is never cancelled here — credit-note it (Stage 10).</summary>
    public void Cancel()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw InvoicesRuleException.InvoiceNotDraft(Status);
        }

        Status = InvoiceStatus.Cancelled;
    }

    /// <summary>Throws unless the invoice is still a draft.</summary>
    public void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw InvoicesRuleException.InvoiceNotDraft(Status);
        }
    }

    /// <summary>Tags the invoice as one segment of a multi-company split (ADR-102).</summary>
    /// <param name="groupDocumentRef">The shared reference every segment carries.</param>
    public void SetGroupDocument(string groupDocumentRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupDocumentRef);
        GroupDocumentRef = groupDocumentRef;
    }

    private void RecalculateTotals()
    {
        Money net = Money.Zero(Currency);
        Money tax = Money.Zero(Currency);
        foreach (var line in _lines)
        {
            net += line.Net;
            tax += line.TaxAmount;
        }

        Net = net;
        Tax = tax;
        Gross = net + tax;
    }
}
