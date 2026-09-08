using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Quotes;

/// <summary>
/// A non-binding price promise: a priced basket with an expiry (ADR-074). A promise of price,
///
/// never a promise of stock — a quote reserves nothing unless it explicitly invokes Stage 08c's
/// reservation ledger, and by default it holds nothing at all.
/// </summary>
/// <remarks>
/// Prices, taxes and pack sizes are snapshotted onto each <see cref="QuoteLine"/> at creation, so a
/// mid-process price-list change never reprices a live quote. Lifecycle:
/// <c>Draft → Issued → Accepted → Converted</c>, with <c>Rejected</c> and <c>Expired</c> as the other
/// terminal states.
/// </remarks>
/// <remarks>
/// Deliberately not <see cref="IImmutableRecord"/> — like <c>Sale</c>, a quote is mutable while it
/// is being worked and frozen by its status afterwards. The status guards (<see cref="EnsureDraft"/>
/// and the terminal states) are the immutability contract, enforced by the aggregate rather than
/// by the persistence guard, which cannot tell a legal Draft → Issued transition from vandalism.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Quote : Entity
{
    private readonly List<QuoteLine> _lines = [];

    private Quote(
        Guid tenantId,
        Guid? storeId,
        string quoteNumber,
        Guid customerId,
        string currency,
        QuoteStatus status,
        DateTimeOffset validUntil,
        string? groupId)
        : base(tenantId, storeId)
    {
        QuoteNumber = quoteNumber;
        CustomerId = customerId;
        Currency = currency;
        Status = status;
        ValidUntil = validUntil;
        GroupId = groupId;
        Net = Money.Zero(currency);
        Tax = Money.Zero(currency);
        Gross = Money.Zero(currency);
    }

    private Quote()
    {
    }

    /// <summary>The human-readable number. ADR-065's sequence, series <c>QTE</c>, per company.</summary>
    public string QuoteNumber { get; private set; } = string.Empty;

    /// <summary>The customer the price is promised to.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The ISO 4217 currency every line is denominated in. One quote, one currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Where the quote stands in its lifecycle.</summary>
    public QuoteStatus Status { get; private set; }

    /// <summary>When the promise lapses, UTC. Acceptance after this instant is refused.</summary>
    public DateTimeOffset ValidUntil { get; private set; }

    /// <summary>The trading-group document reference, when the quote spans companies.</summary>
    public string? GroupId { get; private set; }

    /// <summary>The sum of the lines' nets. A reported figure, recomputed as lines are added.</summary>
    public Money Net { get; private set; }

    /// <summary>The sum of the lines' stored taxes (ADR-075). Never recomputed from the total.</summary>
    public Money Tax { get; private set; }

    /// <summary>Net plus tax. What the customer was promised.</summary>
    public Money Gross { get; private set; }

    /// <summary>The snapshotted lines.</summary>
    public IReadOnlyList<QuoteLine> Lines => _lines;

    /// <summary>Opens a draft quote.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the quote belongs to one.</param>
    /// <param name="quoteNumber">The next number in the company's <c>QTE</c> series.</param>
    /// <param name="customerId">The customer the price is promised to.</param>
    /// <param name="currency">The ISO 4217 currency.</param>
    /// <param name="validUntil">When the promise lapses, UTC.</param>
    /// <param name="groupId">The trading-group document reference, if any.</param>
    /// <param name="companyId">The owning company, stamped for exports and projections.</param>
    public static Quote Create(
        Guid tenantId,
        Guid? storeId,
        string quoteNumber,
        Guid customerId,
        string currency,
        DateTimeOffset validUntil,
        string? groupId = null,
        Guid? companyId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A quote must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(quoteNumber);

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A quote must have a customer.", nameof(customerId));
        }

        var quote = new Quote(
            tenantId, storeId, quoteNumber.Trim(), customerId, currency,
            QuoteStatus.Draft, validUntil, groupId);

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            quote.AssignCompany(companyId.Value);
        }

        return quote;
    }

    /// <summary>Adds a snapshotted line to a draft, recomputing the totals from the stored lines.</summary>
    /// <param name="line">The line, already priced, taxed and pack-snapped.</param>
    public void AddLine(QuoteLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureDraft();
        _lines.Add(line);
        RecalculateTotals();
    }

    /// <summary>Locks the prices and hands the quote to the customer.</summary>
    /// <param name="now">The instant of issue, UTC, from <c>IClock</c> — never <c>DateTimeOffset.UtcNow</c>.</param>
    public void Issue(DateTimeOffset now)
    {
        if (Status != QuoteStatus.Draft)
        {
            throw QuotesRuleException.InvalidTransition(Status, QuoteStatus.Issued);
        }

        if (_lines.Count == 0)
        {
            throw QuotesRuleException.NoLines();
        }

        if (now > ValidUntil)
        {
            throw QuotesRuleException.QuoteExpired();
        }

        Status = QuoteStatus.Issued;
    }

    /// <summary>Records the customer's yes, inside the validity window.</summary>
    /// <param name="now">The instant of acceptance, UTC, from <c>IClock</c>.</param>
    public void Accept(DateTimeOffset now)
    {
        if (Status != QuoteStatus.Issued)
        {
            throw QuotesRuleException.InvalidTransition(Status, QuoteStatus.Accepted);
        }

        if (now > ValidUntil)
        {
            throw QuotesRuleException.QuoteExpired();
        }

        Status = QuoteStatus.Accepted;
    }

    /// <summary>Records the customer's no.</summary>
    public void Reject()
    {
        if (Status != QuoteStatus.Issued)
        {
            throw QuotesRuleException.InvalidTransition(Status, QuoteStatus.Rejected);
        }

        Status = QuoteStatus.Rejected;
    }

    /// <summary>Withdraws the promise before or at its lapse. Terminal states stay put.</summary>
    public void Expire()
    {
        if (Status is QuoteStatus.Expired or QuoteStatus.Rejected or QuoteStatus.Accepted or QuoteStatus.Converted)
        {
            throw QuotesRuleException.InvalidTransition(Status, QuoteStatus.Expired);
        }

        Status = QuoteStatus.Expired;
    }

    /// <summary>Converts an accepted quote into an order or a sale. The quote itself never trades.</summary>
    /// <remarks>
    /// Conversion is a marker, not a mechanism: Stage 14 (orders) or Stage 09 (sales) builds the real
    /// document from this quote's snapshots. Marking it here is what stops one acceptance becoming two
    /// orders.
    /// </remarks>
    public void MarkConverted()
    {
        if (Status != QuoteStatus.Accepted)
        {
            throw QuotesRuleException.InvalidTransition(Status, QuoteStatus.Converted);
        }

        Status = QuoteStatus.Converted;
    }

    /// <summary>Throws unless the quote is still a draft.</summary>
    public void EnsureDraft()
    {
        if (Status != QuoteStatus.Draft)
        {
            throw QuotesRuleException.QuoteNotDraft(Status);
        }
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
