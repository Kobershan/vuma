using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// The document that takes goods and money back. A new document, never an edit of the sale.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a document and not a negative sale line.</b> <c>CLAUDE.md</c> §7 rule 7 says a
/// completed financial document is amended by a new document, and Stage 09 made that structural rather
/// than advisory: <c>ck_sale_lines_quantity_positive</c> is on <c>pos.sale_lines</c> precisely so that
/// nobody could later slide a negative line into a sale a customer is holding a receipt for. This
/// aggregate is the other half of that decision. The original sale is read and never written.
/// </para>
/// <para>
/// <b>The money comes from the original line, not from today.</b> Every line's unit price is copied off
/// the <see cref="SaleLine"/> being returned and its tax is derived pro-rata from the tax that was
/// stored at ring-up (ADR-075). A customer who bought on special gets the special back, and yesterday's
/// receipt is not restated by a rate change that happened since — which is the whole reason Stage 09
/// stored the tax on the line instead of deriving it from a document total.
/// </para>
/// <para>
/// The aggregate root: lines are only reachable through the return, so the invariant that matters —
/// nothing comes back that did not go out (business rule 5) — cannot be broken by writing to a child.
/// The cumulative check needs to see every earlier return against the same sale line, which the
/// aggregate cannot know on its own; <see cref="AddLine"/> therefore takes that figure as an argument
/// and the handler supplies it from the repository. The database carries the same rule as a check
/// constraint over the snapshot — see <see cref="SalesReturnLine.PreviouslyReturnedQuantity"/>.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesReturn : Entity
{
    private readonly List<SalesReturnLine> _lines = [];

    private SalesReturn(
        Guid id,
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        string returnNumber,
        Guid locationId,
        Guid? customerId,
        string currency,
        string reason,
        TenderType refundTenderType,
        Guid authorisedByUserId,
        DateTimeOffset raisedAt)
        : base(id, tenantId, storeId)
    {
        SaleId = saleId;
        ReturnNumber = returnNumber;
        LocationId = locationId;
        CustomerId = customerId;
        Currency = currency;
        Reason = reason;
        RefundTenderType = refundTenderType;
        AuthorisedByUserId = authorisedByUserId;
        RaisedAt = raisedAt;
        Status = SalesReturnStatus.Draft;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesReturn()
    {
    }

    /// <summary>The completed sale this return is against. A bare id into <c>pos</c>, never a foreign key.</summary>
    public Guid SaleId { get; private set; }

    /// <summary>The human-readable number on the credit slip. ADR-065's sequence, series <c>RTN</c>.</summary>
    public string ReturnNumber { get; private set; } = string.Empty;

    /// <summary>The stock location the goods come back into — the original sale's.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The customer, when the original sale identified one.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The currency, bound from the original sale (business rule 10).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Why the goods came back, in the operator's words.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>How the refund was given back.</summary>
    public TenderType RefundTenderType { get; private set; }

    /// <summary>Who authorised it. A return is the classic shrinkage route, so this is never inferred.</summary>
    public Guid AuthorisedByUserId { get; private set; }

    /// <summary>Where the return stands.</summary>
    public SalesReturnStatus Status { get; private set; }

    /// <summary>The refund excluding tax — the sum of its lines.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax coming back — the sum of its lines, each derived from the original line's stored tax.</summary>
    public Money Tax { get; private set; }

    /// <summary>What the customer gets back — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>When the return was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When it completed, or <c>null</c>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When it was cancelled, or <c>null</c>.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>What is coming back.</summary>
    public IReadOnlyList<SalesReturnLine> Lines => _lines;

    /// <summary>True while lines may still be added.</summary>
    public bool IsDraft => Status is SalesReturnStatus.Draft;

    /// <summary>Raises a return against a completed sale.</summary>
    /// <param name="sale">The sale being returned against. Read, never written.</param>
    /// <param name="returnNumber">The credit slip number.</param>
    /// <param name="reason">Why the goods came back.</param>
    /// <param name="refundTenderType">How the refund is given back.</param>
    /// <param name="authorisedByUserId">Who authorised it.</param>
    /// <param name="raisedAt">When, UTC.</param>
    /// <param name="id">
    /// The return's identity, or <c>null</c> to mint one here. A caller that already sent this create
    /// once supplies the id it used the first time, which makes a dropped-connection retry idempotent.
    /// </param>
    /// <exception cref="SalesRuleException">The sale never completed (business rule 7).</exception>
    public static SalesReturn Raise(
        Sale sale,
        string returnNumber,
        string reason,
        TenderType refundTenderType,
        Guid authorisedByUserId,
        DateTimeOffset raisedAt,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        // Business rule 7. A voided sale took no money and moved no goods, so there is nothing to give
        // back; an open or parked one is corrected by voiding a line, which costs the customer nothing.
        if (sale.Status is not SaleStatus.Completed)
        {
            throw SalesRuleException.OnlyCompletedSalesCanBeReturned(sale.Status.ToString());
        }

        return new SalesReturn(
            id ?? UuidV7.NewGuid(),
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            returnNumber.Trim(),
            sale.LocationId,
            sale.CustomerId,
            sale.Currency,
            reason.Trim(),
            refundTenderType,
            authorisedByUserId,
            raisedAt);
    }

    /// <summary>Puts one of the original sale's lines onto the return.</summary>
    /// <param name="soldLine">The original line, from the sale this return names.</param>
    /// <param name="quantity">How much is coming back. Positive, and never more than is left.</param>
    /// <param name="previouslyReturned">
    /// How much of <paramref name="soldLine"/> has already come back on <em>earlier</em> return
    /// documents. Supplied by the caller because the aggregate cannot see its siblings; the handler
    /// reads it from <c>ISalesReturnRepository.SumReturnedQuantityAsync</c>.
    /// </param>
    /// <returns>The new line.</returns>
    /// <exception cref="SalesRuleException">
    /// The return is not a draft, the line belongs to another sale, the quantity is not positive, the
    /// same line is already on this document, or the cumulative quantity would exceed what was sold.
    /// </exception>
    public SalesReturnLine AddLine(SaleLine soldLine, Quantity quantity, decimal previouslyReturned)
    {
        ArgumentNullException.ThrowIfNull(soldLine);
        EnsureDraft();

        if (soldLine.SaleId != SaleId)
        {
            throw new SalesNotFoundException("sale line on this sale", soldLine.Id);
        }

        if (_lines.Exists(line => line.SaleLineId == soldLine.Id))
        {
            // Two rows for one original line is exactly how an over-return slips past a per-row check:
            // each row passes on its own and the pair does not.
            throw SalesRuleException.DuplicateReturnLine();
        }

        if (quantity.Value <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        // Business rule 5, in the aggregate. The database carries the same arithmetic as a check
        // constraint over the two snapshotted columns, so a row written around this method still cannot
        // claim more than was sold.
        if (previouslyReturned + quantity.Value > soldLine.Quantity.Value)
        {
            throw SalesRuleException.ReturnExceedsQuantitySold(
                soldLine.Quantity.Value, previouslyReturned, quantity.Value);
        }

        SalesReturnLine created = SalesReturnLine.ForSoldLine(
            TenantId, StoreId, Id, soldLine, quantity, previouslyReturned);

        _lines.Add(created);
        RecalculateTotals();

        return created;
    }

    /// <summary>
    /// Closes the return: the goods are the shop's again and the money is the customer's.
    /// </summary>
    /// <param name="completedAt">When, UTC.</param>
    /// <exception cref="SalesRuleException">The return is not a draft, or has no lines.</exception>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw SalesRuleException.ReturnHasNoLines();
        }

        CompletedAt = completedAt;
        Status = SalesReturnStatus.Completed;
    }

    /// <summary>Abandons a draft return. Nothing was refunded and nothing moved.</summary>
    /// <param name="cancelledAt">When, UTC.</param>
    /// <exception cref="SalesRuleException">The return is not a draft.</exception>
    public void Cancel(DateTimeOffset cancelledAt)
    {
        EnsureDraft();

        CancelledAt = cancelledAt;
        Status = SalesReturnStatus.Cancelled;
    }

    /// <summary>Finds a line on this return.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="SalesNotFoundException">No such line on this return.</exception>
    public SalesReturnLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new SalesNotFoundException("sales return line", lineId);

    /// <summary>Refuses to proceed unless the return is still a draft.</summary>
    /// <exception cref="SalesRuleException">The return is completed or cancelled.</exception>
    public void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw SalesRuleException.UnexpectedReturnStatus(Status);
        }
    }

    private void RecalculateTotals()
    {
        Money net = Money.Zero(Currency);
        Money tax = Money.Zero(Currency);
        Money gross = Money.Zero(Currency);

        foreach (SalesReturnLine line in _lines)
        {
            net += line.Net;
            tax += line.Tax;
            gross += line.Gross;
        }

        Net = net;
        Tax = tax;
        Gross = gross;
    }
}

/// <summary>Where a <see cref="SalesReturn"/> stands.</summary>
public enum SalesReturnStatus
{
    /// <summary>Being built at the counter. Lines may be added.</summary>
    Draft = 0,

    /// <summary>Refunded, stock put back, financial event raised. Frozen.</summary>
    Completed = 1,

    /// <summary>Abandoned before anything moved.</summary>
    Cancelled = 2,
}
