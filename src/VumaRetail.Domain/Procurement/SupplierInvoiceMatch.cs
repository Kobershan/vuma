using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// The three-way match: what was ordered, what arrived, and what the supplier is charging for,
/// compared line by line before anybody pays.
/// </summary>
/// <remarks>
/// <para>
/// <b>A match is a document, not a boolean</b> (ADR-082). It is written whatever its verdict — a
/// <see cref="ThreeWayMatchStatus.Blocked"/> match is stored, readable and queryable, because "why did
/// we not pay this invoice" is a question somebody asks three weeks later and a rejected comparison
/// that left no row cannot answer it. The blocked ones are also the only evidence a supplier is
/// systematically over-billing.
/// </para>
/// <para>
/// <b>Releasing is what raises the financial event.</b> Building a match compares; releasing accepts.
/// Keeping the two apart is what makes a supervisor's judgement on a within-tolerance variance a
/// recorded act rather than an implicit side effect of running the comparison. Released, it freezes
/// (§7 rule 7) — a correction is a supplier credit note and a new match.
/// </para>
/// <para>
/// <b>No GL account, and no AP invoice.</b> §7 rule 12: this raises
/// <c>procurement.invoice.matched</c> with <c>Net</c>, <c>Tax</c> and <c>Gross</c>, and Stage 07's
/// posting rules engine decides that the GRNI clearing account is debited and trade creditors credited.
/// The <c>ApInvoice</c> aggregate, its ageing and its payment remain Finance's; procurement hands over
/// a matched fact, not a posting instruction (ADR-085).
/// </para>
/// <para>
/// The tolerances are snapshotted onto the document. Tenant policy changes, and a match that showed a
/// different verdict when re-read under this month's tolerance would make the audit trail worthless.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class SupplierInvoiceMatch : Entity
{
    private readonly List<SupplierInvoiceMatchLine> _lines = [];

    private SupplierInvoiceMatch(
        Guid tenantId,
        Guid? storeId,
        Guid purchaseOrderId,
        Guid partnerId,
        string supplierInvoiceNumber,
        DateOnly invoiceDate,
        string currency,
        Money claimedNet,
        Money claimedTax,
        Money claimedGross,
        decimal pricetolerancePercentage,
        Money priceToleranceFloor,
        DateTimeOffset matchedAt)
        : base(tenantId, storeId)
    {
        PurchaseOrderId = purchaseOrderId;
        PartnerId = partnerId;
        SupplierInvoiceNumber = supplierInvoiceNumber;
        InvoiceDate = invoiceDate;
        Currency = currency;
        ClaimedNet = claimedNet;
        ClaimedTax = claimedTax;
        ClaimedGross = claimedGross;
        PriceTolerancePercentage = pricetolerancePercentage;
        PriceToleranceFloor = priceToleranceFloor;
        MatchedAt = matchedAt;
        Status = ThreeWayMatchStatus.Matched;
        Variances = ThreeWayMatchVarianceKind.None;
        MatchedNet = Money.Zero(currency);
        PriceVariance = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SupplierInvoiceMatch()
    {
    }

    /// <summary>The order being matched against.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>The supplier. A bare id into <c>partners</c>.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The supplier's own invoice number — the thing Finance and the supplier both quote.</summary>
    public string SupplierInvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The date on the supplier's invoice.</summary>
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>The currency, bound from the order (business rule 4).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>What the supplier is claiming, excluding tax.</summary>
    public Money ClaimedNet { get; private set; }

    /// <summary>The tax the supplier is claiming.</summary>
    public Money ClaimedTax { get; private set; }

    /// <summary>What the supplier wants paid — <see cref="ClaimedNet"/> plus <see cref="ClaimedTax"/>.</summary>
    public Money ClaimedGross { get; private set; }

    /// <summary>What the order's own costs say the invoiced quantities are worth, excluding tax.</summary>
    public Money MatchedNet { get; private set; }

    /// <summary>The claim less what the order supports. Positive means the supplier is charging more.</summary>
    public Money PriceVariance { get; private set; }

    /// <summary>The percentage tolerance that was applied, snapshotted.</summary>
    public decimal PriceTolerancePercentage { get; private set; }

    /// <summary>
    /// The absolute per-line floor, snapshotted. A variance inside <em>either</em> the percentage or
    /// this passes — without a floor, a 2% tolerance on a R3.00 line refuses a six-cent difference, and
    /// nobody is going to hold up a delivery over six cents.
    /// </summary>
    public Money PriceToleranceFloor { get; private set; }

    /// <summary>The verdict.</summary>
    public ThreeWayMatchStatus Status { get; private set; }

    /// <summary>Which comparisons failed, when any did.</summary>
    public ThreeWayMatchVarianceKind Variances { get; private set; }

    /// <summary>When the comparison was run, UTC.</summary>
    public DateTimeOffset MatchedAt { get; private set; }

    /// <summary>Who released it for payment, or <c>null</c>.</summary>
    public Guid? ReleasedByUserId { get; private set; }

    /// <summary>When it was released, or <c>null</c>.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>The GL journal the release posted, when Finance was wired.</summary>
    public Guid? JournalId { get; private set; }

    /// <summary>The comparison, line by line.</summary>
    public IReadOnlyList<SupplierInvoiceMatchLine> Lines => _lines;

    /// <summary>True once released and frozen.</summary>
    public bool IsReleased => ReleasedAt is not null;

    /// <summary>True when the verdict permits payment (business rule 13).</summary>
    public bool IsPayable => Status
        is ThreeWayMatchStatus.Matched
        or ThreeWayMatchStatus.MatchedWithinTolerance;

    /// <summary>Opens a match against an order for one supplier invoice.</summary>
    /// <param name="order">The order being matched against.</param>
    /// <param name="supplierInvoiceNumber">The supplier's own invoice number.</param>
    /// <param name="invoiceDate">The date on it.</param>
    /// <param name="claimedNet">What the supplier is claiming, excluding tax.</param>
    /// <param name="claimedTax">The tax being claimed.</param>
    /// <param name="priceTolerancePercentage">The percentage tolerance to apply. 0–100.</param>
    /// <param name="priceToleranceFloor">The absolute per-line floor. Not negative.</param>
    /// <param name="matchedAt">When the comparison is being run, UTC.</param>
    /// <exception cref="ProcurementRuleException">
    /// The claim is in another currency, an amount is negative, or a tolerance is out of range.
    /// </exception>
    public static SupplierInvoiceMatch Open(
        PurchaseOrder order,
        string supplierInvoiceNumber,
        DateOnly invoiceDate,
        Money claimedNet,
        Money claimedTax,
        decimal priceTolerancePercentage,
        Money priceToleranceFloor,
        DateTimeOffset matchedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierInvoiceNumber);

        ProcurementLineRules.RequireSameCurrency(order.Currency, claimedNet);
        ProcurementLineRules.RequireSameCurrency(order.Currency, claimedTax);
        ProcurementLineRules.RequireSameCurrency(order.Currency, priceToleranceFloor);
        ProcurementLineRules.RequireNonNegative(claimedNet);
        ProcurementLineRules.RequireNonNegative(claimedTax);
        ProcurementLineRules.RequireNonNegative(priceToleranceFloor);

        if (priceTolerancePercentage is < 0m or > 100m)
        {
            throw ProcurementRuleException.ToleranceOutOfRange();
        }

        return new SupplierInvoiceMatch(
            order.TenantId,
            order.StoreId,
            order.Id,
            order.PartnerId,
            supplierInvoiceNumber.Trim(),
            invoiceDate,
            order.Currency,
            claimedNet,
            claimedTax,
            claimedNet + claimedTax,
            priceTolerancePercentage,
            priceToleranceFloor,
            matchedAt);
    }

    /// <summary>Compares one invoiced line against what was ordered and what was received.</summary>
    /// <param name="orderLine">The order line the supplier is invoicing for.</param>
    /// <param name="invoicedQuantity">How much this invoice claims. Positive.</param>
    /// <param name="invoicedUnitCost">What the supplier is charging per unit.</param>
    /// <param name="receivedQuantity">How much has actually been received against that order line.</param>
    /// <param name="previouslyInvoicedQuantity">How much earlier released matches already claimed.</param>
    /// <returns>The new comparison line.</returns>
    /// <exception cref="ProcurementRuleException">
    /// The match is released, the line is already on this document, or the money is wrong.
    /// </exception>
    public SupplierInvoiceMatchLine AddLine(
        PurchaseOrderLine orderLine,
        Quantity invoicedQuantity,
        Money invoicedUnitCost,
        Quantity receivedQuantity,
        Quantity previouslyInvoicedQuantity)
    {
        ArgumentNullException.ThrowIfNull(orderLine);

        EnsureNotReleased();

        if (orderLine.PurchaseOrderId != PurchaseOrderId)
        {
            throw new ProcurementNotFoundException("purchase order line on this order", orderLine.Id);
        }

        if (_lines.Exists(line => line.PurchaseOrderLineId == orderLine.Id))
        {
            throw ProcurementRuleException.DuplicateOrderLineReference("match");
        }

        ProcurementLineRules.RequireSameCurrency(Currency, invoicedUnitCost);

        SupplierInvoiceMatchLine created = SupplierInvoiceMatchLine.Compare(
            TenantId,
            StoreId,
            Id,
            orderLine,
            invoicedQuantity,
            invoicedUnitCost,
            receivedQuantity,
            previouslyInvoicedQuantity,
            PriceTolerancePercentage,
            PriceToleranceFloor);

        _lines.Add(created);
        Recalculate();

        return created;
    }

    /// <summary>
    /// Records that the supplier invoiced for something this order does not have a line for.
    /// </summary>
    /// <param name="description">What the supplier called it.</param>
    /// <param name="invoicedQuantity">How much they claim.</param>
    /// <param name="invoicedUnitCost">What they are charging.</param>
    /// <returns>The new comparison line, always blocked.</returns>
    /// <remarks>
    /// A line the order does not have is not an error to throw on — it is the most interesting thing an
    /// invoice can contain, and refusing to record it means the only trace is in whoever tried to enter
    /// it. The document blocks; the row says why.
    /// </remarks>
    public SupplierInvoiceMatchLine AddUnknownLine(
        string description, Quantity invoicedQuantity, Money invoicedUnitCost)
    {
        EnsureNotReleased();

        ProcurementLineRules.RequireSameCurrency(Currency, invoicedUnitCost);

        SupplierInvoiceMatchLine created = SupplierInvoiceMatchLine.Unknown(
            TenantId, StoreId, Id, description, invoicedQuantity, invoicedUnitCost);

        _lines.Add(created);
        Recalculate();

        return created;
    }

    /// <summary>
    /// Releases the match for payment. This is the act that raises the financial event; the caller
    /// posts it.
    /// </summary>
    /// <param name="releasedByUserId">Who released it.</param>
    /// <param name="releasedAt">When, UTC.</param>
    /// <exception cref="ProcurementRuleException">It is blocked, has no lines, or is already released.</exception>
    public void Release(Guid releasedByUserId, DateTimeOffset releasedAt)
    {
        EnsureNotReleased();

        if (_lines.Count == 0)
        {
            throw ProcurementRuleException.MatchHasNoLines();
        }

        if (!IsPayable)
        {
            throw ProcurementRuleException.BlockedMatchCannotBeReleased(Status);
        }

        ReleasedByUserId = releasedByUserId;
        ReleasedAt = releasedAt;
    }

    /// <summary>Records the GL journal the release produced.</summary>
    /// <param name="journalId">The journal.</param>
    public void RecordJournal(Guid journalId) => JournalId = journalId;

    /// <summary>Finds a line on this match.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="ProcurementNotFoundException">No such line on this match.</exception>
    public SupplierInvoiceMatchLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new ProcurementNotFoundException("supplier invoice match line", lineId);

    private void EnsureNotReleased()
    {
        if (IsReleased)
        {
            throw ProcurementRuleException.MatchAlreadyReleased();
        }
    }

    /// <summary>
    /// Rolls the lines' verdicts up into the document's. The worst line wins: one blocked line blocks
    /// the invoice, because an invoice is paid or it is not.
    /// </summary>
    private void Recalculate()
    {
        Money matchedNet = Money.Zero(Currency);
        ThreeWayMatchVarianceKind variances = ThreeWayMatchVarianceKind.None;
        ThreeWayMatchStatus status = ThreeWayMatchStatus.Matched;

        foreach (SupplierInvoiceMatchLine line in _lines)
        {
            matchedNet += line.OrderedValue;
            variances |= line.Variances;

            if (line.Status > status)
            {
                status = line.Status;
            }
        }

        MatchedNet = matchedNet;
        Variances = variances;
        Status = status;

        // The document-level variance is the claim against what the order supports, not the sum of the
        // per-line variances. They are the same number when every invoiced line is on the order, and
        // they differ exactly when the supplier has added something — which is the case worth showing.
        PriceVariance = ClaimedNet - matchedNet;
    }
}
