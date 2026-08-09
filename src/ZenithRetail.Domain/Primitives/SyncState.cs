namespace ZenithRetail.Domain.Primitives;

/// <summary>
/// Where a row stands in the terminal → store → cloud replication chain (ADR-006).
/// </summary>
public enum SyncState
{
    /// <summary>Written locally and not yet handed to the outbox. The starting state for every row.</summary>
    Local = 0,

    /// <summary>Queued in the transactional outbox, waiting for the dispatcher.</summary>
    Pending = 1,

    /// <summary>Handed to the transport; delivery not yet confirmed by the receiver.</summary>
    InFlight = 2,

    /// <summary>Acknowledged by the next tier up.</summary>
    Synced = 3,

    /// <summary>
    /// Diverged from another node and the entity's <see cref="ConflictPolicy"/> could not settle it
    /// automatically. Sitting in the review queue for a human.
    /// </summary>
    Conflicted = 4,
}

/// <summary>
/// How a divergence on an entity is settled when two tiers changed the same row (ADR-007).
/// </summary>
/// <remarks>
/// Every replicated entity declares one of these explicitly. There is no default on purpose — an
/// entity whose conflict behaviour nobody chose is an entity that will lose someone's data quietly.
/// </remarks>
public enum ConflictPolicy
{
    /// <summary>Highest hybrid logical clock stamp wins. For rows where the latest edit is simply the right one.</summary>
    LastWriterWins = 0,

    /// <summary>The store's version wins. For anything the store operates and the cloud only observes.</summary>
    StoreWins = 1,

    /// <summary>The cloud's version wins. For tenant-level configuration, pricing policy and licensing.</summary>
    CloudWins = 2,

    /// <summary>
    /// Entries are never overwritten, only accumulated — the stock ledger, the loyalty ledger, journals.
    /// Independent entries from two nodes merge non-destructively (ADR-005).
    /// </summary>
    AppendOnly = 3,

    /// <summary>
    /// Route to the review queue. For genuine business conflicts, such as two stores editing one
    /// supplier, where picking a winner automatically would silently discard a real decision.
    /// </summary>
    ManualReview = 4,
}
