using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// One supplier's quote against an RFQ — what they charge, how long they hold it, and how long they
/// take to deliver.
/// </summary>
/// <remarks>
/// <para>
/// Frozen on submission (business rule 2). <see cref="AddLine"/> is the one mutation, and it exists
/// only for the window in which the quote is being transcribed off the supplier's document; once the
/// response is awarded or declined even that closes. Everything else about it — price, validity, lead
/// time — is what the supplier said, and the record of what they said is the reason to keep it.
/// </para>
/// <para>
/// <b>Lead time is days, not a date.</b> A supplier quotes "ten working days from order", not "the
/// fourteenth"; storing the date they happened to mention would make the quote unusable the moment the
/// order slipped a week, and it is the lead time the scorecard eventually measures them against.
/// </para>
/// <para>
/// This is also the shape Stage 21b needs. A connected supplier's quote arrives as a proposal the
/// retailer accepts (§7 rule 18) — authored by the supplier, frozen on submission, awarded by the
/// retailer — which is exactly this type with a different author.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class RfqResponse : Entity
{
    private readonly List<RfqResponseLine> _lines = [];

    private RfqResponse(
        Guid tenantId,
        Guid? storeId,
        Guid rfqId,
        Guid partnerId,
        string currency,
        DateTimeOffset quotedAt,
        DateTimeOffset? validUntil,
        int leadTimeDays,
        string? notes)
        : base(tenantId, storeId)
    {
        RfqId = rfqId;
        PartnerId = partnerId;
        Currency = currency;
        QuotedAt = quotedAt;
        ValidUntil = validUntil;
        LeadTimeDays = leadTimeDays;
        Notes = notes;
        Status = RfqResponseStatus.Submitted;
        Total = Money.Zero(currency);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RfqResponse()
    {
    }

    /// <summary>The RFQ this quote answers.</summary>
    public Guid RfqId { get; private set; }

    /// <summary>The supplier. A bare id into <c>partners</c>, never a foreign key.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The currency this supplier quoted in. Every line on the response is in it.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>When the quote is dated, UTC.</summary>
    public DateTimeOffset QuotedAt { get; private set; }

    /// <summary>How long the supplier will hold the price, or <c>null</c> if they did not say.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <summary>How many days the supplier says delivery takes from order.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>Anything else the supplier said, in their words.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where this quote stands.</summary>
    public RfqResponseStatus Status { get; private set; }

    /// <summary>The quote's total — the sum of its lines. What a buyer actually compares.</summary>
    public Money Total { get; private set; }

    /// <summary>Who awarded it, or <c>null</c>.</summary>
    public Guid? AwardedByUserId { get; private set; }

    /// <summary>When it was decided, either way, or <c>null</c>.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>What the supplier quoted, line by line.</summary>
    public IReadOnlyList<RfqResponseLine> Lines => _lines;

    /// <summary>True once this quote has been decided, either way.</summary>
    public bool IsDecided => Status is not RfqResponseStatus.Submitted;

    /// <summary>
    /// Records a supplier's quote. Prefer <see cref="Rfq.RecordResponse"/>, which enforces the RFQ's
    /// closing date and the one-quote-per-supplier rule.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store sourcing.</param>
    /// <param name="rfqId">The RFQ.</param>
    /// <param name="partnerId">The supplier.</param>
    /// <param name="currency">The currency they quoted in.</param>
    /// <param name="quotedAt">When the quote is dated, UTC.</param>
    /// <param name="validUntil">How long they hold it, or <c>null</c>.</param>
    /// <param name="leadTimeDays">How long delivery takes. Not negative.</param>
    /// <param name="notes">Anything else, or <c>null</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The lead time is negative.</exception>
    public static RfqResponse Submit(
        Guid tenantId,
        Guid? storeId,
        Guid rfqId,
        Guid partnerId,
        string currency,
        DateTimeOffset quotedAt,
        DateTimeOffset? validUntil,
        int leadTimeDays,
        string? notes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leadTimeDays);

        // Money's own constructor is the currency validator; going through Zero rather than trusting the
        // string means a malformed code is refused here rather than at the first line that uses it.
        Money zero = Money.Zero(currency);

        return new RfqResponse(
            tenantId, storeId, rfqId, partnerId, zero.Currency, quotedAt, validUntil, leadTimeDays,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    /// <summary>Puts one quoted price onto this response.</summary>
    /// <param name="rfqLine">The line being quoted for. The caller has already checked it is on this RFQ.</param>
    /// <param name="unitCost">What the supplier charges per unit.</param>
    /// <param name="availableQuantity">
    /// How much they can supply, or <c>null</c> for everything asked for — counted in the RFQ line's own
    /// unit of measure.
    /// </param>
    /// <returns>The new line.</returns>
    /// <exception cref="ProcurementRuleException">
    /// The response is frozen, the line is already quoted, the cost is negative, or the currency differs
    /// from the response's.
    /// </exception>
    public RfqResponseLine AddLine(RfqLine rfqLine, Money unitCost, decimal? availableQuantity)
    {
        ArgumentNullException.ThrowIfNull(rfqLine);

        if (IsDecided)
        {
            throw ProcurementRuleException.ResponseIsFrozen();
        }

        if (rfqLine.RfqId != RfqId)
        {
            throw ProcurementRuleException.ResponseLineNotOnRfq();
        }

        if (_lines.Exists(line => line.RfqLineId == rfqLine.Id))
        {
            throw ProcurementRuleException.DuplicateOrderLineReference("quote");
        }

        ProcurementLineRules.RequireNonNegative(unitCost);
        ProcurementLineRules.RequireSameCurrency(Currency, unitCost);

        RfqResponseLine created = RfqResponseLine.For(
            TenantId, StoreId, Id, rfqLine, unitCost, availableQuantity);

        _lines.Add(created);
        RecalculateTotal();

        return created;
    }

    /// <summary>Marks this quote as the one that won.</summary>
    /// <param name="awardedByUserId">Who decided.</param>
    /// <param name="awardedAt">When, UTC.</param>
    internal void Award(Guid awardedByUserId, DateTimeOffset awardedAt)
    {
        Status = RfqResponseStatus.Awarded;
        AwardedByUserId = awardedByUserId;
        DecidedAt = awardedAt;
    }

    /// <summary>Marks this quote as considered and not chosen.</summary>
    /// <param name="declinedAt">When, UTC.</param>
    internal void Decline(DateTimeOffset declinedAt)
    {
        Status = RfqResponseStatus.Declined;
        DecidedAt = declinedAt;
    }

    private void RecalculateTotal()
    {
        Money total = Money.Zero(Currency);

        foreach (RfqResponseLine line in _lines)
        {
            total += line.ExtendedCost;
        }

        Total = total;
    }
}
