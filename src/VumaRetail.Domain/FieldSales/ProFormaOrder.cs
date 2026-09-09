using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// A rep's proposed order: quotation-shaped, expiring, snapshotted — posting nothing and
/// reserving nothing until management approves it (ADR-107).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProFormaOrder : Entity
{
    private readonly List<ProFormaOrderLine> _lines = [];

    private ProFormaOrder(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ProFormaOrder()
    {
    }

    /// <summary>Human number, series <c>PF-</c>, per company.</summary>
    public string ProFormaNumber { get; private set; } = string.Empty;

    /// <summary>The capturing rep.</summary>
    public Guid RepId { get; private set; }


    /// <summary>The customer. Bare uuid into partners, never a foreign key.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>Delivery address, when the rep captured one. Null means click &amp; collect.</summary>
    public Address? DeliveryAddress { get; private set; }

    /// <summary>ISO 4217 code for every amount on this document.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public ProFormaStatus Status { get; private set; }

    /// <summary>Caller-minted idempotency key. Replays return the existing document.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>When the proposal lapses if unapproved. Default +7 days.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The Stage 05 approval request. Set on submit.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>The registry credit hold the approval saga took. Set before reserving.</summary>
    public Guid? CreditHoldId { get; private set; }

    /// <summary>The sales order approval created. Set on conversion.</summary>
    public Guid? ConvertedOrderId { get; private set; }

    /// <summary>Why it was rejected, amended or withdrawn. Required when it was.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>Repriced total minus quoted total, once approval re-prices. Null before.
    /// Stored plain (ADR-067); the currency is always the document's.</summary>
    public decimal? RepriceDeltaAmount { get; private set; }

    /// <summary>Repriced total minus quoted total, once approval re-prices. Null before.</summary>
    public Money? RepriceDelta => RepriceDeltaAmount is { } amount ? new Money(amount, Currency) : null;

    /// <summary>When captured, UTC.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>When submitted, UTC. Null until submitted.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When decided, UTC. Null until decided.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Every line ever captured, in order.</summary>
    public IReadOnlyList<ProFormaOrderLine> Lines => _lines;

    /// <summary>Quoted gross — the sum of live line grosses.</summary>
    public Money Gross => _lines
        .Aggregate(Money.Zero(Currency), (sum, line) => sum + line.Gross);

    /// <summary>Captures a pro forma. Posts nothing, reserves nothing.</summary>
    public static ProFormaOrder Capture(
        Guid tenantId,
        Guid? storeId,
        string proFormaNumber,
        Guid repId,
        Guid companyId,
        Guid partnerId,
        string currency,
        string idempotencyKey,
        DateTimeOffset capturedAt,
        DateTimeOffset? expiresAt = null,
        Address? deliveryAddress = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A pro forma must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(proFormaNumber);

        if (repId == Guid.Empty || companyId == Guid.Empty || partnerId == Guid.Empty)
        {
            throw new ArgumentException("A pro forma names its rep, company and customer.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return new ProFormaOrder(tenantId, storeId)
        {
            ProFormaNumber = proFormaNumber.Trim(),
            RepId = repId,
            CompanyId = companyId,
            PartnerId = partnerId,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = ProFormaStatus.Draft,
            IdempotencyKey = idempotencyKey.Trim(),
            CapturedAt = capturedAt,
            ExpiresAt = expiresAt ?? capturedAt.AddDays(7),
            DeliveryAddress = deliveryAddress,
        };
    }

    /// <summary>Adds a snapshotted line. Draft only.</summary>
    public ProFormaOrderLine AddLine(
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money taxAmount,
        Money net,
        string packSizeDescription,
        Guid? priceListId,
        string promotionsSummary,
        Money availableAtCapture,
        DateTimeOffset availabilityAsAt)
    {
        EnsureDraft("have lines added");

        var line = ProFormaOrderLine.Create(
            TenantId, StoreId, CompanyId, Id, itemId, itemVariantId, quantityValue, quantityUom,
            unitPrice, discountAmount, taxCode, taxAmount, net, packSizeDescription,
            priceListId, promotionsSummary, availableAtCapture, availabilityAsAt, Currency);
        _lines.Add(line);
        return line;
    }

    /// <summary>Submits for approval. Draft (or amended) with lines only.</summary>
    public void Submit(DateTimeOffset submittedAt)
    {
        if (Status is not ProFormaStatus.Draft and not ProFormaStatus.Amended)
        {
            throw FieldSalesException.IllegalTransition(Status, "be submitted");
        }

        if (_lines.Count == 0)
        {
            throw new FieldSalesException("PROFORMA_NO_LINES", "A pro forma with no lines cannot be submitted.");
        }

        Status = ProFormaStatus.Submitted;
        SubmittedAt = submittedAt;
    }

    /// <summary>Records the raised approval request.</summary>
    public void RecordApproval(Guid requestId)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "record an approval request on");
        }

        ApprovalRequestId = requestId == Guid.Empty
            ? throw new ArgumentException("An approval request id is required.", nameof(requestId))
            : requestId;
    }

    /// <summary>Marks management's approval, with the reprice delta (zero when prices held).</summary>
    public void MarkApproved(Money repriceDelta, DateTimeOffset decidedAt)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "be approved");
        }

        Status = ProFormaStatus.Approved;
        RepriceDeltaAmount = repriceDelta.Amount;
        DecidedAt = decidedAt;
    }

    /// <summary>Records the saga's credit hold.</summary>
    public void RecordCreditHold(Guid holdId)
    {
        CreditHoldId = holdId == Guid.Empty
            ? throw new ArgumentException("A hold id is required.", nameof(holdId))
            : holdId;
    }

    /// <summary>Returns an approved pro forma to submitted after a saga failure, with the reason.</summary>
    public void MarkApprovalFailed(string reason)
    {
        if (Status is not ProFormaStatus.Approved)
        {
            throw FieldSalesException.IllegalTransition(Status, "record an approval failure on");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ProFormaStatus.Submitted;
        DecisionReason = reason.Trim();
        CreditHoldId = null;
    }

    /// <summary>Converts into the created order. Approved only.</summary>
    public void MarkConverted(Guid orderId)
    {
        if (Status is not ProFormaStatus.Approved)
        {
            throw FieldSalesException.IllegalTransition(Status, "be converted");
        }

        ConvertedOrderId = orderId == Guid.Empty
            ? throw new ArgumentException("An order id is required.", nameof(orderId))
            : orderId;
        Status = ProFormaStatus.Converted;
    }

    /// <summary>Rejects with a reason. A rep is never left guessing why.</summary>
    public void Reject(string reason, DateTimeOffset decidedAt)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "be rejected");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ProFormaStatus.Rejected;
        DecisionReason = reason.Trim();
        DecidedAt = decidedAt;
    }

    /// <summary>Returns to the rep for amendment, with the reason.</summary>
    /// <remarks>
    /// The amendment re-confirms the document, so expiry refreshes to a fresh 7 days — an
    /// amended-then-resubmitted proposal is never approved on a lapsed clock.
    /// </remarks>
    public void ReturnForAmendment(string reason, DateTimeOffset now)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "be returned for amendment");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ProFormaStatus.Amended;
        DecisionReason = reason.Trim();
        ExpiresAt = now.AddDays(7);
    }

    /// <summary>Withdraws before a decision. Draft or submitted only.</summary>
    public void Withdraw(string reason)
    {
        if (Status is not ProFormaStatus.Draft
            and not ProFormaStatus.Submitted
            and not ProFormaStatus.Amended)
        {
            throw FieldSalesException.IllegalTransition(Status, "be withdrawn");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ProFormaStatus.Withdrawn;
        DecisionReason = reason.Trim();
    }

    /// <summary>Lapses an undecided document. Terminal, honest, automatic.</summary>
    public void Expire()
    {
        if (Status is ProFormaStatus.Converted or ProFormaStatus.Rejected
            or ProFormaStatus.Withdrawn or ProFormaStatus.Expired)
        {
            return;
        }

        Status = ProFormaStatus.Expired;
    }

    /// <summary>True when past expiry and still undecided.</summary>
    public bool IsExpired(DateTimeOffset now)
        => now >= ExpiresAt
            && Status is ProFormaStatus.Draft or ProFormaStatus.Submitted or ProFormaStatus.Amended;

    private void EnsureDraft(string attempted)
    {
        if (Status is not ProFormaStatus.Draft and not ProFormaStatus.Amended)
        {
            throw FieldSalesException.IllegalTransition(Status, attempted);
        }
    }
}
