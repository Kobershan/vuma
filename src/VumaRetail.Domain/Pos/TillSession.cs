using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// One cashier's shift at one terminal: the float that went into the drawer, the sales rung up against
/// it, and the count that closes it.
/// </summary>
/// <remarks>
/// <para>
/// The cash-up is the only control a shop has over the drawer, and it works exactly because the
/// expected figure is <em>derived</em> and the counted figure is <em>entered</em>. Nothing here lets a
/// person set the expected amount: <see cref="Close"/> takes the counted cash and the cash the session
/// itself took, and the variance is whatever falls out. A cash-up whose variance can be edited to zero
/// is a form, not a control.
/// </para>
/// <para>
/// Not <see cref="IImmutableRecord"/>: like <c>StocktakeSession</c>, a session has exactly one legal
/// transition, so the framework's "never touch again" guarantee is the wrong fit and immutability
/// after closing is a domain rule instead — <see cref="EnsureOpen"/> guards every mutation.
/// </para>
/// <para>
/// Store-local operational data. The cloud tier observes the result of a shift; it does not
/// participate in one, and two stores never contend over the same drawer.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class TillSession : Entity
{
    private TillSession(
        Guid tenantId,
        Guid? storeId,
        Guid terminalId,
        Guid operatorUserId,
        Money openingFloat,
        DateTimeOffset openedAt)
        : base(tenantId, storeId)
    {
        TerminalId = terminalId;
        OperatorUserId = operatorUserId;
        OpeningFloat = openingFloat;
        Currency = openingFloat.Currency;
        OpenedAt = openedAt;
        Status = TillSessionStatus.Open;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private TillSession()
    {
    }

    /// <summary>The terminal whose drawer this session counts.</summary>
    public Guid TerminalId { get; private set; }

    /// <summary>The operator who opened the session and is accountable for the count.</summary>
    public Guid OperatorUserId { get; private set; }

    /// <summary>The currency the drawer is counted in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The cash placed in the drawer before trading started.</summary>
    public Money OpeningFloat { get; private set; }

    /// <summary>When the session opened, UTC.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>Where the session stands.</summary>
    public TillSessionStatus Status { get; private set; }

    /// <summary>The cash counted out of the drawer at close, or <c>null</c> while the session is open.</summary>
    public Money? CountedCash => Status is TillSessionStatus.Closed
        ? new Money(_countedCashAmount, Currency)
        : null;

    /// <summary>
    /// What the drawer should have held — float plus cash taken less change given — or <c>null</c>
    /// while the session is open.
    /// </summary>
    public Money? ExpectedCash => Status is TillSessionStatus.Closed
        ? new Money(_expectedCashAmount, Currency)
        : null;

    /// <summary>
    /// Counted less expected. Negative is a shortfall, positive is an overage. <c>null</c> while the
    /// session is open.
    /// </summary>
    public Money? Variance => Status is TillSessionStatus.Closed
        ? new Money(_countedCashAmount - _expectedCashAmount, Currency)
        : null;

    /// <summary>When the session closed, or <c>null</c> while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who counted the drawer, or <c>null</c> while the session is open.</summary>
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>An optional note explaining a variance.</summary>
    public string? Note { get; private set; }

    /// <summary>True once the session has been counted and closed.</summary>
    public bool IsClosed => Status is TillSessionStatus.Closed;

    // ADR-067: an optional Money is two plain columns and a computed accessor, never a complex
    // property — EF Core 9 cannot configure a complex property as optional. Both are meaningless
    // until Close runs, and both are read back through the accessors above.
    private decimal _countedCashAmount;
    private decimal _expectedCashAmount;

    /// <summary>Opens a shift at a terminal.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the terminal stands in.</param>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="operatorUserId">The cashier taking the drawer.</param>
    /// <param name="openingFloat">The cash going into the drawer. May be zero; may not be negative.</param>
    /// <param name="openedAt">When the shift started, UTC.</param>
    /// <exception cref="PosRuleException">The float is negative.</exception>
    public static TillSession Open(
        Guid tenantId,
        Guid? storeId,
        Guid terminalId,
        Guid operatorUserId,
        Money openingFloat,
        DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A till session must belong to a tenant.", nameof(tenantId));
        }

        if (terminalId == Guid.Empty)
        {
            throw new ArgumentException("A till session must name a terminal.", nameof(terminalId));
        }

        if (operatorUserId == Guid.Empty)
        {
            throw new ArgumentException("A till session must name the operator who took the drawer.", nameof(operatorUserId));
        }

        if (openingFloat.IsNegative)
        {
            throw PosRuleException.MustBePositive("opening float");
        }

        return new TillSession(tenantId, storeId, terminalId, operatorUserId, openingFloat, openedAt);
    }

    /// <summary>Refuses to proceed once the session is closed. Called before every mutation.</summary>
    /// <exception cref="PosRuleException">The session is already closed.</exception>
    public void EnsureOpen()
    {
        if (IsClosed)
        {
            throw PosRuleException.TillSessionIsClosed();
        }
    }

    /// <summary>
    /// Counts the drawer and closes the shift permanently.
    /// </summary>
    /// <param name="countedCash">What was physically counted out of the drawer.</param>
    /// <param name="cashTaken">
    /// The cash tenders this session's completed sales took, <em>net of change given</em>. Derived by
    /// the caller from the session's own sales — never entered by a person.
    /// </param>
    /// <param name="openSaleCount">How many sales are still open on this session.</param>
    /// <param name="closedByUserId">Who counted it.</param>
    /// <param name="closedAt">When, UTC.</param>
    /// <param name="note">An optional explanation, which a non-zero variance usually wants.</param>
    /// <exception cref="PosRuleException">
    /// The session is closed, sales are still open on it, the counted amount is negative, or a currency
    /// does not match the drawer's.
    /// </exception>
    public void Close(
        Money countedCash,
        Money cashTaken,
        int openSaleCount,
        Guid closedByUserId,
        DateTimeOffset closedAt,
        string? note = null)
    {
        EnsureOpen();

        if (openSaleCount > 0)
        {
            throw PosRuleException.TillSessionHasOpenSales(openSaleCount);
        }

        EnsureSameCurrency(countedCash);
        EnsureSameCurrency(cashTaken);

        if (countedCash.IsNegative)
        {
            throw PosRuleException.MustBePositive("counted cash");
        }

        _countedCashAmount = countedCash.Amount;
        _expectedCashAmount = (OpeningFloat + cashTaken).Amount;
        ClosedByUserId = closedByUserId;
        ClosedAt = closedAt;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = TillSessionStatus.Closed;
    }

    private void EnsureSameCurrency(Money amount)
    {
        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
        {
            throw PosRuleException.CurrencyMismatch(Currency, amount.Currency);
        }
    }
}

/// <summary>Where a <see cref="TillSession"/> stands.</summary>
public enum TillSessionStatus
{
    /// <summary>Trading. Sales may be rung up against it.</summary>
    Open = 0,

    /// <summary>Counted and closed. Never reopened.</summary>
    Closed = 1,
}
