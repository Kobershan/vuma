using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// Vuma's side of one earn/burn: an event log, not a ledger (Stage 20). The points ledger lives
/// in Orbit; this row exists so a retried till submission, a replayed outbox message and a
/// double-picked retry worker all collapse onto one outcome via <see cref="IdempotencyKey"/>.
/// </summary>
/// <remarks>
/// Deliberately NOT an <c>IImmutableRecord</c>: the row is a small state machine
/// (<c>Pending → Confirmed | QueuedForRetry → Confirmed | Failed</c>), and the platform audit
/// trail already preserves every transition for investigation (R6). The precedent is Stage 15's
/// <c>ReplenishmentSuggestion</c>, whose accept mutates status the same way. Truly append-only
/// facts (the earn/burn request itself: customer, amount, key) are set once at construction and
/// never mutated — only the processing status moves.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class LoyaltyTransaction : Entity
{
    private LoyaltyTransaction()
    {
    }

    /// <summary>Records an earn/burn intent before calling Orbit.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="customerId">The member.</param>
    /// <param name="type">Earn, burn, adjustment or expiry.</param>
    /// <param name="amount">Points, scale 4. Positive for earn, positive magnitude for burn.</param>
    /// <param name="currency">ISO 4217 code of the purchase (earn) or settings currency.</param>
    /// <param name="idempotencyKey">Client-supplied UUID v7. The at-most-once key.</param>
    /// <param name="reference">What caused it (e.g. sale id).</param>
    /// <param name="occurredAt">When it happened, UTC. From <c>IClock</c>.</param>
    /// <param name="storeId">The owning store, if any.</param>
    /// <exception cref="InvalidPointsException">Non-positive amount or missing key.</exception>
    public LoyaltyTransaction(
        Guid tenantId,
        Guid companyId,
        Guid customerId,
        TransactionType type,
        decimal amount,
        string currency,
        Guid idempotencyKey,
        string? reference,
        DateTimeOffset occurredAt,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        if (amount <= 0m)
        {
            throw new InvalidPointsException("A loyalty transaction amount must be positive.");
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new InvalidPointsException("An idempotency key is required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        AssignCompany(companyId);
        CustomerId = customerId;
        TransactionType = type;
        Amount = decimal.Round(amount, Money.Scale, Money.Rounding);
        Currency = currency.Trim().ToUpperInvariant();
        IdempotencyKey = idempotencyKey;
        Reference = reference;
        OccurredAt = occurredAt;
        Status = TransactionStatus.Pending;
    }

    /// <summary>The member.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Earn, burn, adjustment or expiry.</summary>
    public TransactionType TransactionType { get; private set; }

    /// <summary>Points, scale 4.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code of the purchase (earn) or settings currency.</summary>
    public string Currency { get; private set; } = "ZAR";

    /// <summary>The at-most-once key. Unique per company.</summary>
    public Guid IdempotencyKey { get; private set; }

    /// <summary>What caused it (e.g. sale id).</summary>
    public string? Reference { get; private set; }

    /// <summary>When it happened, UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Orbit's transaction id, once confirmed.</summary>
    public string? OrbitTransactionId { get; private set; }

    /// <summary>Orbit's reported balance at confirmation. What a replayed key returns.</summary>
    public decimal? ResultingBalance { get; private set; }

    /// <summary>Where it stands.</summary>
    public TransactionStatus Status { get; private set; }

    /// <summary>Marks the transaction confirmed by Orbit. Terminal.</summary>
    /// <param name="orbitTransactionId">Orbit's transaction id.</param>
    /// <param name="resultingBalance">Orbit's reported balance after applying.</param>
    public void MarkConfirmed(string orbitTransactionId, decimal resultingBalance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orbitTransactionId);
        Status = TransactionStatus.Confirmed;
        OrbitTransactionId = orbitTransactionId;
        ResultingBalance = resultingBalance;
    }

    /// <summary>Marks the transaction queued for retry after an Orbit outage.</summary>
    public void MarkQueuedForRetry() => Status = TransactionStatus.QueuedForRetry;

    /// <summary>Marks retries exhausted. Terminal; needs reconciliation.</summary>
    public void MarkFailed() => Status = TransactionStatus.Failed;
}
