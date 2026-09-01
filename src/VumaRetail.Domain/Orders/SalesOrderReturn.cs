using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Orders;

/// <summary>
/// Goods that were actually fulfilled off a <see cref="SalesOrder"/> and are coming back. A new
/// document, never an edit of the order (<c>CLAUDE.md</c> §7 rule 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Never the same document as Stage 10's <c>SalesReturn</c> (business rule 6, ADR-085's precedent
/// applied to a document series).</b> A <c>SalesReturn</c>-shaped document reverses a Stage 09
/// till <c>Sale</c>; this reverses a <see cref="SalesOrder"/> line that was fulfilled but never rung up as
/// a <c>Sale</c> at all. <see cref="ReturnNumber"/> draws from ADR-065's own <c>ORT</c> series,
/// deliberately not Stage 10's <c>RTN</c>, so an audit trail can never confuse the two.
/// </para>
/// <para>
/// The aggregate root, for the same reason Stage 10's <c>SalesReturn</c> is one: the invariant that nothing
/// comes back beyond what actually shipped (business rule 6) cannot be broken by writing to a child line
/// directly.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesOrderReturn : Entity
{
    private readonly List<SalesOrderReturnLine> _lines = [];

    private SalesOrderReturn(
        Guid tenantId,
        Guid? storeId,
        Guid salesOrderId,
        string returnNumber,
        string currency,
        string reason,
        Guid authorisedByUserId,
        DateTimeOffset raisedAt)
        : base(tenantId, storeId)
    {
        SalesOrderId = salesOrderId;
        ReturnNumber = returnNumber;
        Reason = reason;
        AuthorisedByUserId = authorisedByUserId;
        RaisedAt = raisedAt;
        Status = SalesOrderReturnStatus.Open;
        RefundStatus = OrderPaymentStatus.Unpaid;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesOrderReturn()
    {
    }

    /// <summary>The order this return is against. A bare id, never a foreign key.</summary>
    public Guid SalesOrderId { get; private set; }

    /// <summary>The human-readable number on the credit document. ADR-065's sequence, series <c>ORT</c>.</summary>
    public string ReturnNumber { get; private set; } = string.Empty;

    /// <summary>Why the goods came back, in the operator's words.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Who authorised it.</summary>
    public Guid AuthorisedByUserId { get; private set; }

    /// <summary>Where the return stands.</summary>
    public SalesOrderReturnStatus Status { get; private set; }

    /// <summary>How the refund was, or will be, given back. Same shape as <see cref="OrderPaymentStatus"/> — a flag, not a mechanism.</summary>
    public OrderPaymentStatus RefundStatus { get; private set; }

    /// <summary>The refund excluding tax — the sum of its lines.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax coming back — the sum of its lines.</summary>
    public Money Tax { get; private set; }

    /// <summary>What the customer gets back — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>When the return was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When it completed, or <c>null</c>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>What is coming back.</summary>
    public IReadOnlyList<SalesOrderReturnLine> Lines => _lines;

    /// <summary>True while lines may still be added.</summary>
    public bool IsOpen => Status is SalesOrderReturnStatus.Open;

    /// <summary>Raises an open return against an order.</summary>
    /// <param name="salesOrderId">The order.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="currency">The order's currency.</param>
    /// <param name="returnNumber">The credit document number.</param>
    /// <param name="reason">Why the goods came back.</param>
    /// <param name="authorisedByUserId">Who authorised it.</param>
    /// <param name="raisedAt">When, UTC.</param>
    public static SalesOrderReturn Raise(
        Guid salesOrderId,
        Guid tenantId,
        Guid? storeId,
        string currency,
        string returnNumber,
        string reason,
        Guid authorisedByUserId,
        DateTimeOffset raisedAt)
    {
        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("A return must name the order it is against.", nameof(salesOrderId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(returnNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (authorisedByUserId == Guid.Empty)
        {
            throw new ArgumentException("A return must name who authorised it.", nameof(authorisedByUserId));
        }

        return new SalesOrderReturn(
            tenantId, storeId, salesOrderId, returnNumber.Trim(), currency, reason.Trim(), authorisedByUserId, raisedAt);
    }

    /// <summary>Puts one of the order's fulfilled lines onto the return.</summary>
    /// <param name="orderedLine">The original order line, from the order this return names.</param>
    /// <param name="quantity">How much is coming back.</param>
    /// <param name="fulfilledQuantity">How much of <paramref name="orderedLine"/> was actually fulfilled.</param>
    /// <param name="previouslyReturned">How much of <paramref name="orderedLine"/> has already come back on earlier return documents.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="OrdersRuleException">
    /// The return is not open, the quantity is not positive, the same line is already on this document,
    /// or the cumulative quantity would exceed what was fulfilled.
    /// </exception>
    public SalesOrderReturnLine AddLine(
        SalesOrderLine orderedLine, Quantity quantity, Quantity fulfilledQuantity, decimal previouslyReturned)
    {
        ArgumentNullException.ThrowIfNull(orderedLine);
        EnsureOpen();

        if (orderedLine.SalesOrderId != SalesOrderId)
        {
            throw new OrdersNotFoundException("order line on this order", orderedLine.Id);
        }

        if (_lines.Exists(line => line.SalesOrderLineId == orderedLine.Id))
        {
            throw OrdersRuleException.DuplicateReturnLine();
        }

        SalesOrderReturnLine created = SalesOrderReturnLine.ForFulfilledLine(
            TenantId, StoreId, Id, orderedLine, quantity, fulfilledQuantity, previouslyReturned);

        _lines.Add(created);
        RecalculateTotals();

        return created;
    }

    /// <summary>Closes the return: the goods are back in stock and the refund is due.</summary>
    /// <param name="completedAt">When, UTC.</param>
    /// <exception cref="OrdersRuleException">The return is not open, or has no lines.</exception>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureOpen();

        if (_lines.Count == 0)
        {
            throw OrdersRuleException.ReturnHasNoLines();
        }

        CompletedAt = completedAt;
        Status = SalesOrderReturnStatus.Completed;
    }

    /// <summary>Records how the refund was given back.</summary>
    /// <param name="refundStatus">The refund status.</param>
    public void RecordRefund(OrderPaymentStatus refundStatus) => RefundStatus = refundStatus;

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw OrdersRuleException.UnexpectedReturnStatus(Status);
        }
    }

    private void RecalculateTotals()
    {
        Money net = Money.Zero(Net.Currency);
        Money tax = Money.Zero(Tax.Currency);

        foreach (SalesOrderReturnLine line in _lines)
        {
            net += line.Net;
            tax += line.Tax;
        }

        Net = net;
        Tax = tax;
        Gross = net + tax;
    }
}
