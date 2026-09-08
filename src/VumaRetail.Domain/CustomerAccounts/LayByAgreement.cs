using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A lay-by agreement: a price promise with a payment plan and held stock. The agreed total is
/// frozen at opening (price protection); the goods are reserved, never sold, until the final
/// payment converts the agreement into exactly one sale.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class LayByAgreement : Entity
{
    private readonly List<LayByAgreementLine> _lines = [];
    private readonly List<LayByInstalment> _instalments = [];

    private LayByAgreement(
        Guid tenantId,
        Guid? storeId,
        string agreementNumber,
        Guid partnerId,
        Money agreedTotal,
        Money depositRequired,
        int termMonths,
        DateTimeOffset expiryDate,
        Money adminFee,
        LayByStatus status)
        : base(tenantId, storeId)
    {
        AgreementNumber = agreementNumber;
        PartnerId = partnerId;
        AgreedTotal = agreedTotal;
        DepositRequired = depositRequired;
        TermMonths = termMonths;
        ExpiryDate = expiryDate;
        AdminFee = adminFee;
        Status = status;
        PaidToDate = Money.Zero(agreedTotal.Currency);
    }

    private LayByAgreement()
    {
    }

    /// <summary>The human-readable number, series <c>LAY</c>, per company.</summary>
    public string AgreementNumber { get; private set; } = string.Empty;

    /// <summary>The customer. A bare id, never a cross-schema key.</summary>
    public Guid PartnerId { get; private set; }

    /// <summary>The frozen price promise. Never re-resolved, whatever the shelf does.</summary>
    public Money AgreedTotal { get; private set; }

    /// <summary>The minimum first payment that activates the agreement.</summary>
    public Money DepositRequired { get; private set; }

    /// <summary>How much has been paid, sum of the instalment rows. Never set directly.</summary>
    public Money PaidToDate { get; private set; }

    /// <summary>The agreed payment window in months.</summary>
    public int TermMonths { get; private set; }

    /// <summary>When unpaid agreements lapse, UTC.</summary>
    public DateTimeOffset ExpiryDate { get; private set; }

    /// <summary>Kept on cancellation. Snapshotted from terms at opening, never re-read.</summary>
    public Money AdminFee { get; private set; }

    /// <summary>Where the agreement stands.</summary>
    public LayByStatus Status { get; private set; }

    /// <summary>When the agreement completed, UTC. Dates the revenue. Null until then.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the agreement was cancelled, UTC. Null unless cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>What went back to the customer on cancellation. Set once.</summary>
    public Money? CancelRefund { get; private set; }

    /// <summary>What the shop kept on cancellation. Set once.</summary>
    public Money? CancelFee { get; private set; }

    /// <summary>The frozen lines.</summary>
    public IReadOnlyList<LayByAgreementLine> Lines => _lines;

    /// <summary>The append-only payment history.</summary>
    public IReadOnlyList<LayByInstalment> Instalments => _instalments;

    /// <summary>What is still owed: agreed total less paid to date.</summary>
    public Money Remaining => AgreedTotal - PaidToDate;

    /// <summary>Opens a draft agreement. Activation takes the deposit.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the agreement belongs to one.</param>
    /// <param name="agreementNumber">The next number in the company's <c>LAY</c> series.</param>
    /// <param name="partnerId">The customer.</param>
    /// <param name="agreedTotal">The frozen total. Must be positive.</param>
    /// <param name="depositRequired">The minimum first payment. Must be positive and within total.</param>
    /// <param name="termMonths">The payment window. Must be positive.</param>
    /// <param name="expiryDate">When it lapses.</param>
    /// <param name="adminFee">The cancellation fee, snapshotted from terms now.</param>
    /// <param name="companyId">The owning company, stamped for exports and projections.</param>
    public static LayByAgreement Open(
        Guid tenantId,
        Guid? storeId,
        string agreementNumber,
        Guid partnerId,
        Money agreedTotal,
        Money depositRequired,
        int termMonths,
        DateTimeOffset expiryDate,
        Money adminFee,
        Guid? companyId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A lay-by must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(agreementNumber);

        if (partnerId == Guid.Empty)
        {
            throw new ArgumentException("A lay-by must have a customer.", nameof(partnerId));
        }

        if (agreedTotal.Amount <= 0m)
        {
            throw new ArgumentException("The agreed total must be positive.", nameof(agreedTotal));
        }

        if (depositRequired.Amount <= 0m || depositRequired.Amount > agreedTotal.Amount)
        {
            throw new ArgumentException("The deposit must be positive and within the total.", nameof(depositRequired));
        }

        if (termMonths <= 0)
        {
            throw new ArgumentException("The term must be positive.", nameof(termMonths));
        }

        var agreement = new LayByAgreement(
            tenantId, storeId, agreementNumber.Trim(), partnerId, agreedTotal, depositRequired,
            termMonths, expiryDate, adminFee, LayByStatus.Draft);

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            agreement.AssignCompany(companyId.Value);
        }

        return agreement;
    }

    /// <summary>Adds a snapshotted line to a draft.</summary>
    /// <param name="line">The line, already priced, taxed and pack-snapped.</param>
    public void AddLine(LayByAgreementLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Status != LayByStatus.Draft)
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        _lines.Add(line);
    }

    /// <summary>Signs the agreement: the deposit is in hand and the plan is live.</summary>
    public void Activate()
    {
        if (Status != LayByStatus.Draft)
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        if (_lines.Count == 0)
        {
            throw new LayByExceptions("LAYBY_NO_LINES", "A lay-by must have at least one line.");
        }

        Status = LayByStatus.Active;
    }

    /// <summary>Records a payment. Overpayment is refused — money without a home is a dispute.</summary>
    /// <param name="instalment">The append-only payment row.</param>
    public void AddInstalment(LayByInstalment instalment)
    {
        ArgumentNullException.ThrowIfNull(instalment);

        if (Status != LayByStatus.Active)
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        if (instalment.Amount.Amount > Remaining.Amount)
        {
            throw LayByExceptions.Overpayment();
        }

        _instalments.Add(instalment);
        PaidToDate += instalment.Amount;
    }

    /// <summary>
    /// Converts a fully paid agreement: the held stock is consumed and the revenue recognised by
    /// the caller (ADR-143). The agreement itself only moves state — it never touches stock or the
    /// ledger, so one completion can never produce two issues or two recognitions.
    /// </summary>
    /// <param name="now">The instant of completion, UTC. Dates the revenue.</param>
    public void Complete(DateTimeOffset now)
    {
        if (Status != LayByStatus.Active)
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        if (PaidToDate.Amount != AgreedTotal.Amount)
        {
            throw LayByExceptions.NotFullyPaid();
        }

        Status = LayByStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>Cancels per the snapshotted terms: every cent paid is either refunded or fee.</summary>
    /// <param name="now">The instant of cancellation, UTC.</param>
    /// <param name="refund">What goes back to the customer.</param>
    /// <param name="fee">What the shop keeps. May not exceed the snapshotted admin fee.</param>
    public void Cancel(DateTimeOffset now, Money refund, Money fee)
    {
        if (Status is not (LayByStatus.Draft or LayByStatus.Active))
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        if ((refund.Amount + fee.Amount) != PaidToDate.Amount)
        {
            throw LayByExceptions.CancellationMustAccount();
        }

        if (fee.Amount > AdminFee.Amount)
        {
            throw new LayByExceptions("LAYBY_FEE_EXCEEDS_TERMS", "The fee may not exceed the snapshotted admin fee.");
        }

        Status = LayByStatus.Cancelled;
        CancelledAt = now;
        CancelRefund = refund;
        CancelFee = fee;
    }

    /// <summary>Lapses an unpaid agreement at expiry. Paid money stays claimable, stock is freed.</summary>
    public void Expire()
    {
        if (Status is LayByStatus.Completed or LayByStatus.Cancelled or LayByStatus.Expired)
        {
            throw LayByExceptions.UnexpectedStatus(Status);
        }

        Status = LayByStatus.Expired;
    }
}
