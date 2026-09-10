namespace VumaRetail.Domain.Loyalty;

/// <summary>What a loyalty transaction records (Stage 20).</summary>
public enum TransactionType
{
    /// <summary>Points earned (e.g. from a sale).</summary>
    Earn = 0,

    /// <summary>Points redeemed (e.g. for a discount).</summary>
    Burn = 1,

    /// <summary>Manual adjustment (goodwill, correction).</summary>
    Adjustment = 2,

    /// <summary>Points expired.</summary>
    Expiry = 3,
}

/// <summary>Where a loyalty transaction stands (Stage 20).</summary>
public enum TransactionStatus
{
    /// <summary>Created locally, Orbit call not yet attempted.</summary>
    Pending = 0,

    /// <summary>Confirmed by Orbit. Terminal.</summary>
    Confirmed = 1,

    /// <summary>Orbit unreachable; queued for retry. The till already served the customer.</summary>
    QueuedForRetry = 2,

    /// <summary>Retries exhausted after 24 hours. Terminal; needs reconciliation.</summary>
    Failed = 3,
}
