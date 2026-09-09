using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// A rep's proposed credit note: the same shape against a supplied document, posting nothing
/// until approved — when it becomes a Stage 10 sales return inside the origin invoice's company.
/// There is no cross-company credit note, ever (ADR-128).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProFormaCreditNote : Entity
{
    private readonly List<ProFormaCreditNoteLine> _lines = [];

    private ProFormaCreditNote(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ProFormaCreditNote()
    {
    }

    /// <summary>Human number, series <c>PFC-</c>, per company.</summary>
    public string CreditNoteNumber { get; private set; } = string.Empty;

    /// <summary>The proposing rep.</summary>
    public Guid RepId { get; private set; }


    /// <summary>The original invoice. Bare uuid, never a foreign key.</summary>
    public Guid OriginalInvoiceId { get; private set; }

    /// <summary>The original invoice's number, for the approver and the messages.</summary>
    public string OriginalInvoiceNumber { get; private set; } = string.Empty;

    /// <summary>Reason code (damaged, short-supplied, pricing error, goodwill, other).</summary>
    public string ReasonCode { get; private set; } = string.Empty;

    /// <summary>Free-text reason. Required.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>ISO 4217 code.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Lifecycle state (Converted reads as Applied for credit notes).</summary>
    public ProFormaStatus Status { get; private set; }

    /// <summary>Caller-minted idempotency key.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>When the proposal lapses if unapproved.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The Stage 05 approval request. Set on submit.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>The sales return approval applied. Set on conversion.</summary>
    public Guid? ResultingReturnId { get; private set; }

    /// <summary>Why it was rejected or withdrawn.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>When captured, UTC.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Every proposed line, in order.</summary>
    public IReadOnlyList<ProFormaCreditNoteLine> Lines => _lines;

    /// <summary>Proposed credit gross.</summary>
    public Money Gross => _lines
        .Aggregate(Money.Zero(Currency), (sum, line) => sum + line.Gross);

    /// <summary>Captures a pro forma credit note. Posts nothing.</summary>
    public static ProFormaCreditNote Capture(
        Guid tenantId,
        Guid? storeId,
        string creditNoteNumber,
        Guid repId,
        Guid companyId,
        Guid originalInvoiceId,
        string originalInvoiceNumber,
        string reasonCode,
        string reason,
        string currency,
        string idempotencyKey,
        DateTimeOffset capturedAt,
        DateTimeOffset? expiresAt = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A credit proposal must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(creditNoteNumber);

        if (repId == Guid.Empty || companyId == Guid.Empty || originalInvoiceId == Guid.Empty)
        {
            throw new ArgumentException("A credit proposal names its rep, company and invoice.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(originalInvoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return new ProFormaCreditNote(tenantId, storeId)
        {
            CreditNoteNumber = creditNoteNumber.Trim(),
            RepId = repId,
            CompanyId = companyId,
            OriginalInvoiceId = originalInvoiceId,
            OriginalInvoiceNumber = originalInvoiceNumber.Trim(),
            ReasonCode = reasonCode.Trim(),
            Reason = reason.Trim(),
            Currency = currency.Trim().ToUpperInvariant(),
            Status = ProFormaStatus.Draft,
            IdempotencyKey = idempotencyKey.Trim(),
            CapturedAt = capturedAt,
            ExpiresAt = expiresAt ?? capturedAt.AddDays(7),
        };
    }

    /// <summary>Adds a proposed line against one original invoice line. Draft only.</summary>
    public ProFormaCreditNoteLine AddLine(
        Guid originalInvoiceLineId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money taxAmount,
        Money net)
    {
        if (Status is not ProFormaStatus.Draft and not ProFormaStatus.Amended)
        {
            throw FieldSalesException.IllegalTransition(Status, "have lines added");
        }

        var line = ProFormaCreditNoteLine.Create(
            TenantId, StoreId, CompanyId, Id, originalInvoiceLineId, itemId, itemVariantId,
            quantityValue, quantityUom, unitPrice, taxAmount, net, Currency);
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
            throw new FieldSalesException("PROFORMA_NO_LINES", "A credit proposal with no lines cannot be submitted.");
        }

        Status = ProFormaStatus.Submitted;
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

    /// <summary>Marks approval. The return itself is applied by the saga.</summary>
    public void MarkApproved(DateTimeOffset decidedAt)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "be approved");
        }

        Status = ProFormaStatus.Approved;
    }

    /// <summary>Applies into the created sales return. Approved only.</summary>
    public void MarkApplied(Guid returnId)
    {
        if (Status is not ProFormaStatus.Approved)
        {
            throw FieldSalesException.IllegalTransition(Status, "be applied");
        }

        ResultingReturnId = returnId == Guid.Empty
            ? throw new ArgumentException("A return id is required.", nameof(returnId))
            : returnId;
        Status = ProFormaStatus.Converted;
    }

    /// <summary>Rejects with a reason.</summary>
    public void Reject(string reason)
    {
        if (Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(Status, "be rejected");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ProFormaStatus.Rejected;
        DecisionReason = reason.Trim();
    }

    /// <summary>Withdraws before a decision.</summary>
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

    /// <summary>Lapses an undecided document.</summary>
    public void Expire()
    {
        if (Status is ProFormaStatus.Converted or ProFormaStatus.Rejected or ProFormaStatus.Withdrawn or ProFormaStatus.Expired)
        {
            return;
        }

        Status = ProFormaStatus.Expired;
    }

    /// <summary>True when past expiry and still undecided.</summary>
    public bool IsExpired(DateTimeOffset now)
        => now >= ExpiresAt
            && Status is ProFormaStatus.Draft or ProFormaStatus.Submitted or ProFormaStatus.Amended;
}
