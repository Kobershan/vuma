using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// One transaction at the till, from the first item scanned to the change handed back.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of this module: lines and tenders are only ever reached through a sale, so the
/// invariant that matters — the totals are the sum of the live lines, and the sale is paid for before
/// it completes — cannot be broken by writing to a child directly.
/// </para>
/// <para>
/// <b>Its id may be minted by the caller.</b> UUID v7 was chosen partly because a terminal must be
/// able to create a row with no round trip (ADR-004), and a till that rang a sale up while the network
/// was down has already printed a number on the customer's slip. Replaying that sale under the id it
/// already has makes the replay idempotent by primary key rather than by a deduplication table
/// somebody has to remember to check.
/// </para>
/// <para>
/// Not <see cref="IImmutableRecord"/> — a sale is mutable while it is open and frozen afterwards, so
/// immutability is a domain rule (<see cref="EnsureOpen"/>) rather than a framework guarantee, exactly
/// as <c>StocktakeSession</c> and <see cref="TillSession"/> do it. Its <see cref="SaleTender"/>
/// children <em>are</em> immutable records, because each one is a completed movement of money.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Sale : Entity
{
    private readonly List<SaleLine> _lines = [];
    private readonly List<SaleTender> _tenders = [];

    private Sale(
        Guid id,
        Guid tenantId,
        Guid? storeId,
        string saleNumber,
        Guid tillSessionId,
        Guid terminalId,
        Guid operatorUserId,
        Guid locationId,
        Guid? customerId,
        string currency,
        DateTimeOffset openedAt)
        : base(id, tenantId, storeId)
    {
        SaleNumber = saleNumber;
        TillSessionId = tillSessionId;
        TerminalId = terminalId;
        OperatorUserId = operatorUserId;
        LocationId = locationId;
        CustomerId = customerId;
        Currency = currency;
        OpenedAt = openedAt;
        Status = SaleStatus.Open;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
        AmountTendered = Money.Zero(currency);
        ChangeGiven = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Sale()
    {
    }

    /// <summary>The human-readable number printed on the receipt. ADR-065's sequence, series <c>SALE</c>.</summary>
    public string SaleNumber { get; private set; } = string.Empty;

    /// <summary>The shift this sale was rung up on.</summary>
    public Guid TillSessionId { get; private set; }

    /// <summary>The terminal it was rung up at.</summary>
    public Guid TerminalId { get; private set; }

    /// <summary>The operator who rang it up.</summary>
    public Guid OperatorUserId { get; private set; }

    /// <summary>The stock location the goods leave. A bare id into <c>inventory</c>, never a foreign key.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The customer, when one was identified. A bare id into <c>partners</c>, never a foreign key.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The currency the whole sale is denominated in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Where the sale stands.</summary>
    public SaleStatus Status { get; private set; }

    /// <summary>The sale excluding tax — the sum of its live lines.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax on the sale — the sum of its live lines.</summary>
    public Money Tax { get; private set; }

    /// <summary>What the customer owes — the sum of its live lines.</summary>
    public Money Gross { get; private set; }

    /// <summary>Everything taken across every tender.</summary>
    public Money AmountTendered { get; private set; }

    /// <summary>Change handed back, in cash. Zero until the sale completes.</summary>
    public Money ChangeGiven { get; private set; }

    /// <summary>When the first item was scanned, UTC.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When the sale completed, or <c>null</c>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the sale was abandoned, or <c>null</c>.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>Why it was abandoned. Required when it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>Every line ever rung up, voided ones included. Ordered by line number.</summary>
    public IReadOnlyList<SaleLine> Lines => _lines;

    /// <summary>Every tender captured, in the order the money was taken.</summary>
    public IReadOnlyList<SaleTender> Tenders => _tenders;

    /// <summary>The lines that count — everything not voided.</summary>
    public IEnumerable<SaleLine> LiveLines => _lines.Where(line => !line.IsVoided);

    /// <summary>What is still owed. Zero or negative once the sale is covered.</summary>
    public Money AmountOutstanding => Gross - AmountTendered;

    /// <summary>True while lines and tenders may still be added.</summary>
    public bool IsOpen => Status is SaleStatus.Open;

    /// <summary>The cash this sale contributed to the drawer — cash taken, less the change handed back.</summary>
    /// <remarks>
    /// The one figure the cash-up is derived from, computed here rather than in the query that closes a
    /// session so that "what did this sale put in the drawer" has exactly one definition. A sale that
    /// was not completed contributed nothing, whatever tenders it may be carrying.
    /// </remarks>
    public Money CashContribution => Status is SaleStatus.Completed
        ? _tenders.Where(tender => tender.IsCash)
            .Aggregate(Money.Zero(Currency), (running, tender) => running + tender.Amount) - ChangeGiven
        : Money.Zero(Currency);

    /// <summary>Opens a sale at a terminal.</summary>
    /// <param name="id">
    /// The sale's identity, minted by the caller. Expected to be a UUID v7; a terminal generates it
    /// offline so the replay of an offline sale is idempotent by primary key.
    /// </param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the terminal stands in.</param>
    /// <param name="saleNumber">The receipt number.</param>
    /// <param name="session">The open till session it is rung up on.</param>
    /// <param name="operatorUserId">The operator.</param>
    /// <param name="locationId">The stock location the goods leave.</param>
    /// <param name="customerId">The customer, if one was identified.</param>
    /// <param name="currency">The currency the sale is denominated in.</param>
    /// <param name="openedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">The session is closed.</exception>
    public static Sale Open(
        Guid id,
        Guid tenantId,
        Guid? storeId,
        string saleNumber,
        TillSession session,
        Guid operatorUserId,
        Guid locationId,
        Guid? customerId,
        string currency,
        DateTimeOffset openedAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(saleNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("A sale must have an identity minted by its caller.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A sale must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A sale must name the stock location the goods leave.", nameof(locationId));
        }

        session.EnsureOpen();

        return new Sale(
            id,
            tenantId,
            storeId,
            saleNumber.Trim(),
            session.Id,
            session.TerminalId,
            operatorUserId,
            locationId,
            customerId,
            currency.Trim().ToUpperInvariant(),
            openedAt);
    }

    /// <summary>The next line number to assign. Never reused, including after a void.</summary>
    public int NextLineNumber => _lines.Count + 1;

    /// <summary>Refuses to proceed unless the sale is still open.</summary>
    /// <exception cref="PosRuleException">The sale is parked, completed or voided.</exception>
    public void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw PosRuleException.SaleIsNotOpen(Status);
        }
    }

    /// <summary>Rings a line up and refreshes the totals.</summary>
    /// <param name="line">The line, built by <see cref="SaleLine.Ring"/> against this sale's id.</param>
    /// <exception cref="PosRuleException">The sale is not open, or the line is in another currency.</exception>
    public void AddLine(SaleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureOpen();
        EnsureSameCurrency(line.Gross.Currency);

        _lines.Add(line);
        RecalculateTotals();
    }

    /// <summary>Takes a line off the sale and refreshes the totals.</summary>
    /// <param name="lineId">The line to void.</param>
    /// <param name="voidedAt">When, UTC.</param>
    /// <exception cref="PosNotFoundException">No such line on this sale.</exception>
    /// <exception cref="PosRuleException">The sale is not open, or the line is already voided.</exception>
    public void VoidLine(Guid lineId, DateTimeOffset voidedAt)
    {
        EnsureOpen();

        SaleLine line = _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new PosNotFoundException("sale line", lineId);

        line.Void(voidedAt);
        RecalculateTotals();
    }

    /// <summary>Captures a payment against the sale.</summary>
    /// <param name="tender">The tender, built by <see cref="SaleTender.Capture"/> against this sale's id.</param>
    /// <exception cref="PosRuleException">The sale is not open, or the tender is in another currency.</exception>
    public void AddTender(SaleTender tender)
    {
        ArgumentNullException.ThrowIfNull(tender);
        EnsureOpen();
        EnsureSameCurrency(tender.Amount.Currency);

        _tenders.Add(tender);
        RecalculateTotals();
    }

    /// <summary>
    /// Closes the sale: the goods are the customer's, the money is the shop's, and the record is frozen.
    /// </summary>
    /// <param name="completedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">
    /// The sale is not open, has no live lines, is not covered by its tenders, or would owe more change
    /// than it took in cash.
    /// </exception>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureOpen();

        if (!LiveLines.Any())
        {
            throw PosRuleException.SaleHasNoLines();
        }

        if (AmountTendered.Amount < Gross.Amount)
        {
            throw PosRuleException.SaleNotFullyTendered(Gross, AmountTendered);
        }

        Money change = AmountTendered - Gross;

        if (!change.IsZero)
        {
            // Change comes out of the drawer, so it can only ever be given against cash. Overpaying by
            // card and asking for the difference back is a cash advance, which a till may not do.
            Money cash = _tenders.Where(tender => tender.IsCash)
                .Aggregate(Money.Zero(Currency), (running, tender) => running + tender.Amount);

            if (change.Amount > cash.Amount)
            {
                throw PosRuleException.ChangeExceedsCashTendered(change, cash);
            }
        }

        ChangeGiven = change;
        CompletedAt = completedAt;
        Status = SaleStatus.Completed;
    }

    /// <summary>Sets the sale aside so the terminal can serve the next customer.</summary>
    /// <exception cref="PosRuleException">The sale is not open.</exception>
    public void Park()
    {
        EnsureOpen();
        Status = SaleStatus.Parked;
    }

    /// <summary>Brings a parked sale back to the screen.</summary>
    /// <exception cref="PosRuleException">The sale is not parked.</exception>
    public void Resume()
    {
        if (Status is not SaleStatus.Parked)
        {
            throw PosRuleException.UnexpectedSaleStatus(Status);
        }

        Status = SaleStatus.Open;
    }

    /// <summary>Abandons the sale.</summary>
    /// <param name="reason">Why. Recorded, because an abandoned sale is what shrinkage looks like from the outside.</param>
    /// <param name="voidedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">The sale has already completed or is already voided.</exception>
    public void Void(string reason, DateTimeOffset voidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is SaleStatus.Completed)
        {
            throw PosRuleException.CompletedSaleCannotBeVoided();
        }

        if (Status is SaleStatus.Voided)
        {
            throw PosRuleException.UnexpectedSaleStatus(Status);
        }

        VoidReason = reason.Trim();
        VoidedAt = voidedAt;
        Status = SaleStatus.Voided;
    }

    /// <summary>Finds a line on this sale.</summary>
    /// <param name="lineId">The line.</param>
    /// <exception cref="PosNotFoundException">No such line on this sale.</exception>
    public SaleLine RequireLine(Guid lineId)
        => _lines.Find(candidate => candidate.Id == lineId)
            ?? throw new PosNotFoundException("sale line", lineId);

    private void RecalculateTotals()
    {
        Money net = Money.Zero(Currency);
        Money tax = Money.Zero(Currency);
        Money gross = Money.Zero(Currency);

        foreach (SaleLine line in LiveLines)
        {
            net += line.Net;
            tax += line.Tax;
            gross += line.Gross;
        }

        Net = net;
        Tax = tax;
        Gross = gross;

        AmountTendered = _tenders.Aggregate(
            Money.Zero(Currency), (running, tender) => running + tender.Amount);
    }

    private void EnsureSameCurrency(string currency)
    {
        if (!string.Equals(currency, Currency, StringComparison.Ordinal))
        {
            throw PosRuleException.CurrencyMismatch(Currency, currency);
        }
    }
}

/// <summary>Where a <see cref="Sale"/> stands.</summary>
public enum SaleStatus
{
    /// <summary>On the screen. Lines and tenders may be added.</summary>
    Open = 0,

    /// <summary>Set aside so the terminal can serve somebody else. Resumable.</summary>
    Parked = 1,

    /// <summary>Paid for and closed. Frozen — corrected by a Stage 10 return, never an edit.</summary>
    Completed = 2,

    /// <summary>Abandoned before it was paid for. Frozen, with a recorded reason.</summary>
    Voided = 3,
}
