using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry.Trading;

/// <summary>
/// One till basket spanning one or more companies (Stage 09b, ADR-125).
/// </summary>
/// <remarks>
/// A registry record: it coordinates, it never posts. Each company's sale, invoice and
/// receipt are written in that company's own database by the completion saga (TASK-09B-002).
/// Totals are projections over lines — there is no "set total" operation, the same rule
/// every ledger in Vuma follows. Money is the till's currency throughout (§4.13's lesson:
/// a line in another currency is refused, never converted here).
/// </remarks>
public sealed class TradingSession
{
    private readonly List<TradingSessionSegment> _segments = [];

    private TradingSession()
    {
    }

    /// <summary>The session's identity, minted by the caller (offline-safe UUID v7).</summary>
    public Guid Id { get; private set; }

    /// <summary>The owning tenant.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The till's own company. Lines for other companies form sister segments.</summary>
    public Guid SessionCompanyId { get; private set; }

    /// <summary>Human number, series <c>TS</c>.</summary>
    public string SessionNumber { get; private set; } = string.Empty;

    /// <summary>The premises the shared till stands on.</summary>
    public Guid PremisesId { get; private set; }

    /// <summary>The shared terminal.</summary>
    public Guid TerminalId { get; private set; }

    /// <summary>The cashier.</summary>
    public Guid CashierUserId { get; private set; }

    /// <summary>The customer, when identified. Bare uuid into partners, never a foreign key.</summary>
    public Guid? CustomerGroupPartnerId { get; private set; }

    /// <summary>ISO 4217 code for every amount on this session.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Caller-supplied idempotency key. Replays return the existing session.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public TradingSessionStatus Status { get; private set; }

    /// <summary>The captured tender type (Cash, Card, … — Stage 09's tender vocabulary).</summary>
    public string? TenderType { get; private set; }

    /// <summary>The captured tender amount's value. Null until tendered (ADR-067: EF Core 9
    /// cannot map an optional complex property, so the pair is stored plain and exposed
    /// through <see cref="TenderAmount"/>).</summary>
    public decimal? TenderAmountValue { get; private set; }

    /// <summary>The captured tender amount's currency, paired with <see cref="TenderAmountValue"/>.</summary>
    public string? TenderAmountCurrency { get; private set; }

    /// <summary>The captured tender amount. Null until tendered.</summary>
    public Money? TenderAmount => TenderAmountValue is { } amount
        ? new Money(amount, TenderAmountCurrency!)
        : null;

    /// <summary>Tender reference (card authorisation code, voucher serial). Null until tendered.</summary>
    public string? TenderReference { get; private set; }

    /// <summary>Why the last completion attempt failed. Null unless <see cref="TradingSessionStatus.CompletionFailed"/>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Posted invoices that could not be auto-credited when compensation ran (ADR-145),
    /// as JSON. A plain string column: EF Core's change tracker cannot snapshot a
    /// <c>List&lt;string&gt;</c> value conversion (it casts the stored array back to the list
    /// type and throws), so the domain keeps JSON and exposes the list computed.
    /// </summary>
    public string UnwoundInvoicesJson { get; private set; } = "[]";

    /// <summary>Posted invoices that could not be auto-credited when compensation ran.</summary>
    public IReadOnlyList<string> UnwoundInvoiceNumbers =>
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(UnwoundInvoicesJson) ?? [];

    /// <summary>When the session opened, UTC.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When the tender was captured, UTC. Null until tendered.</summary>
    public DateTimeOffset? TenderedAt { get; private set; }

    /// <summary>When every segment posted, UTC. Null until completed.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the session was voided, UTC. Null unless voided.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>Why the session was voided. Required when it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>One segment per company, in first-line order.</summary>
    public IReadOnlyList<TradingSessionSegment> Segments => _segments;

    /// <summary>Basket gross — the sum of rounded segment totals, never the rounding of a sum (ADR-125).</summary>
    public Money Gross => _segments
        .Where(segment => !segment.IsRemoved)
        .Select(segment => segment.Gross)
        .Aggregate(Money.Zero(Currency), (sum, gross) => sum + gross);

    /// <summary>True while lines may still be added or voided.</summary>
    public bool IsOpen => Status is TradingSessionStatus.Open;

    /// <summary>Opens a session at a shared till.</summary>
    public static TradingSession Open(
        Guid id,
        Guid tenantId,
        Guid sessionCompanyId,
        string sessionNumber,
        Guid premisesId,
        Guid terminalId,
        Guid cashierUserId,
        string currency,
        string idempotencyKey,
        DateTimeOffset openedAt,
        Guid? customerGroupPartnerId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A session must have an identity minted by its caller.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A session must belong to a tenant.", nameof(tenantId));
        }

        if (sessionCompanyId == Guid.Empty)
        {
            throw new ArgumentException("A session must name the till's own company.", nameof(sessionCompanyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (premisesId == Guid.Empty)
        {
            throw new ArgumentException("A session must name its premises.", nameof(premisesId));
        }

        if (terminalId == Guid.Empty)
        {
            throw new ArgumentException("A session must name its terminal.", nameof(terminalId));
        }

        if (cashierUserId == Guid.Empty)
        {
            throw new ArgumentException("A session must name its cashier.", nameof(cashierUserId));
        }

        return new TradingSession
        {
            Id = id,
            TenantId = tenantId,
            SessionCompanyId = sessionCompanyId,
            SessionNumber = sessionNumber.Trim(),
            PremisesId = premisesId,
            TerminalId = terminalId,
            CashierUserId = cashierUserId,
            CustomerGroupPartnerId = customerGroupPartnerId,
            Currency = currency.Trim().ToUpperInvariant(),
            IdempotencyKey = idempotencyKey.Trim(),
            Status = TradingSessionStatus.Open,
            OpenedAt = openedAt,
        };
    }

    /// <summary>
    /// Appends a priced line to its company's segment, creating the segment on first use.
    /// The caller resolved the company and checked the link — this method records the outcome.
    /// </summary>
    public TradingSessionLine AddLine(
        Guid lineId,
        Guid companyId,
        string barcode,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money taxAmount,
        Money net,
        string packSizeDescription,
        Guid? priceListId,
        DateTimeOffset addedAt)
    {
        EnsureOpen("have lines added");

        if (!string.Equals(unitPrice.Currency, Currency, StringComparison.Ordinal))
        {
            throw TradingSessionException.CurrencyMismatch(Currency, unitPrice.Currency);
        }

        TradingSessionSegment segment = _segments.FirstOrDefault(s => s.CompanyId == companyId && !s.IsRemoved)
            ?? CreateSegment(companyId);

        return segment.AddLine(
            lineId, TenantId, Id, barcode, itemId, itemVariantId, description,
            quantityValue, quantityUom, unitPrice, discountAmount, taxCode,
            taxAmount, net, packSizeDescription, priceListId, Currency, addedAt);
    }

    /// <summary>Voids one line. A segment left empty is removed, so a refused sister line leaves no trace.</summary>
    public void VoidLine(Guid lineId, DateTimeOffset voidedAt)
    {
        EnsureOpen("have lines voided");

        foreach (TradingSessionSegment segment in _segments.Where(s => !s.IsRemoved))
        {
            if (segment.TryVoidLine(lineId, voidedAt))
            {
                if (!segment.LiveLines.Any())
                {
                    segment.MarkRemoved();
                }

                return;
            }
        }

        throw new TradingSessionException(
            "TRADING_LINE_NOT_FOUND", $"No live line {lineId} on session {SessionNumber}.");
    }

    /// <summary>Captures the one tender and fixes the default proportional allocation.</summary>
    public void CaptureTender(string tenderType, Money amount, string? reference, DateTimeOffset capturedAt)
    {
        if (Status is not TradingSessionStatus.Open and not TradingSessionStatus.CompletionFailed)
        {
            throw TradingSessionException.IllegalTransition(Status, "capture a tender");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tenderType);

        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
        {
            throw TradingSessionException.CurrencyMismatch(Currency, amount.Currency);
        }

        if (!_segments.Any(s => !s.IsRemoved && s.LiveLines.Any()))
        {
            throw new TradingSessionException(
                "TRADING_EMPTY_BASKET", "A tender cannot be captured against an empty basket.");
        }

        if (amount.Amount < Gross.Amount)
        {
            throw TradingSessionException.TenderNotCovered(amount.Amount, Gross.Amount, Currency);
        }

        TenderAllocationResult allocation = TenderAllocator.AllocateDefault(
            amount, LiveSegments().Select(s => (s.CompanyId, s.Gross)).ToList());

        ApplyTender(tenderType.Trim(), amount, reference?.Trim(), capturedAt, allocation);
        Status = TradingSessionStatus.Tendered;
        FailureReason = null;
    }

    /// <summary>Replaces the allocation with the cashier's exact split.</summary>
    public void OverrideAllocation(IReadOnlyList<(Guid CompanyId, Money Amount)> overrides)
    {
        if (Status is not TradingSessionStatus.Tendered and not TradingSessionStatus.CompletionFailed)
        {
            throw TradingSessionException.IllegalTransition(Status, "override the tender allocation");
        }

        ArgumentNullException.ThrowIfNull(overrides);
        Money tender = TenderAmount
            ?? throw new InvalidOperationException("A tendered session always carries its tender.");

        TenderAllocationResult allocation = TenderAllocator.AllocateOverride(
            tender, LiveSegments().Select(s => (s.CompanyId, s.Gross)).ToList(), overrides);

        ApplyAllocation(allocation);
    }

    /// <summary>Marks the saga running. Called by the completion service, not the till.</summary>
    public void MarkCompleting()
    {
        if (Status is not TradingSessionStatus.Tendered and not TradingSessionStatus.CompletionFailed)
        {
            throw TradingSessionException.IllegalTransition(Status, "start completing");
        }

        Status = TradingSessionStatus.Completing;
    }

    /// <summary>Marks every segment posted with its invoice and sale. Called by the completion service.</summary>
    public void MarkCompleted(
        IReadOnlyList<(Guid CompanyId, Guid SaleId, Guid InvoiceId, string InvoiceNumber)> posted,
        DateTimeOffset completedAt)
    {
        if (Status is not TradingSessionStatus.Completing)
        {
            throw TradingSessionException.IllegalTransition(Status, "complete");
        }

        ArgumentNullException.ThrowIfNull(posted);

        foreach ((Guid companyId, Guid saleId, Guid invoiceId, string invoiceNumber) in posted)
        {
            Segment(companyId).MarkPosted(saleId, invoiceId, invoiceNumber);
        }

        Status = TradingSessionStatus.Completed;
        CompletedAt = completedAt;
        FailureReason = null;
    }

    /// <summary>
    /// Returns the session to tendered-after-failure with the reason and any posted invoices
    /// compensation could not unwind (ADR-145).
    /// </summary>
    public void MarkCompletionFailed(string reason, IReadOnlyList<string> unwoundInvoiceNumbers)
    {
        if (Status is not TradingSessionStatus.Completing)
        {
            throw TradingSessionException.IllegalTransition(Status, "record a completion failure");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = TradingSessionStatus.CompletionFailed;
        FailureReason = reason.Trim();
        UnwoundInvoicesJson = System.Text.Json.JsonSerializer.Serialize(
            unwoundInvoiceNumbers ?? []);
    }

    /// <summary>Abandons the session. The caller releases the session's holds (no goods moved).</summary>
    public void Void(string reason, DateTimeOffset voidedAt)
    {
        if (Status is TradingSessionStatus.Completed or TradingSessionStatus.Voided)
        {
            throw TradingSessionException.IllegalTransition(Status, "be voided");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = TradingSessionStatus.Voided;
        VoidReason = reason.Trim();
        VoidedAt = voidedAt;
    }

    private IEnumerable<TradingSessionSegment> LiveSegments()
        => _segments.Where(segment => !segment.IsRemoved && segment.LiveLines.Any());

    private TradingSessionSegment Segment(Guid companyId)
        => _segments.FirstOrDefault(s => s.CompanyId == companyId && !s.IsRemoved)
            ?? throw new TradingSessionException(
                "TRADING_SEGMENT_NOT_FOUND", $"Company {companyId} has no segment on session {SessionNumber}.");

    private TradingSessionSegment CreateSegment(Guid companyId)
    {
        var segment = new TradingSessionSegment
        {
            Id = UuidV7.NewGuid(),
            TenantId = TenantId,
            SessionId = Id,
            CompanyId = companyId,
        };
        _segments.Add(segment);
        return segment;
    }

    private void ApplyTender(
        string tenderType, Money amount, string? reference, DateTimeOffset capturedAt,
        TenderAllocationResult allocation)
    {
        TenderType = tenderType;
        TenderAmountValue = amount.Amount;
        TenderAmountCurrency = amount.Currency;
        TenderReference = reference;
        TenderedAt = capturedAt;
        ApplyAllocation(allocation);
    }

    private void ApplyAllocation(TenderAllocationResult allocation)
    {
        foreach ((Guid companyId, Money share) in allocation.Allocations)
        {
            Segment(companyId).SetAllocation(share, allocation.Basis);
        }
    }

    private void EnsureOpen(string attempted)
    {
        if (!IsOpen)
        {
            throw TradingSessionException.IllegalTransition(Status, attempted);
        }
    }
}
