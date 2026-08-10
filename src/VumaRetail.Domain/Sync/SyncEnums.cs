namespace VumaRetail.Domain.Sync;

/// <summary>
/// Which tier of the terminal → store → cloud chain a node is.
/// </summary>
/// <remarks>
/// Read by <c>ConflictPolicy.StoreWins</c> and <c>CloudWins</c>, which are the two policies that
/// settle a divergence by *authority* rather than by order. A node that did not know what it was
/// could not evaluate either of them, and would have to ask the sender — which is exactly the thing
/// a sender must never get to decide.
/// </remarks>
public enum NodeKind
{
    /// <summary>A till or back-office machine holding a SQLite cache (ADR-003).</summary>
    Terminal = 0,

    /// <summary>The in-store server. Source of truth for the store (R2).</summary>
    Store = 1,

    /// <summary>The cloud tier. Source of truth for the tenant roll-up (R2).</summary>
    Cloud = 2,
}

/// <summary>What a replicated operation does to the row at the far end.</summary>
public enum SyncOperationKind
{
    /// <summary>Create the row, or update it if the receiving node already has it.</summary>
    Upsert = 0,

    /// <summary>
    /// Soft-delete the row. Never a hard delete — §7 rule 8 holds on every tier, and a replicated
    /// hard delete would be a way to erase a row the audit trail says still exists.
    /// </summary>
    Delete = 1,
}

/// <summary>Where an outbox row is in its journey to the next tier.</summary>
public enum OutboxStatus
{
    /// <summary>Written and waiting for the dispatcher. The state every row starts in.</summary>
    Pending = 0,

    /// <summary>Handed to the transport; the ack has not come back yet.</summary>
    InFlight = 1,

    /// <summary>Acknowledged by the receiver. Terminal, and eligible for pruning.</summary>
    Dispatched = 2,

    /// <summary>
    /// The transport or the receiver refused it. Carries the attempt count and the last error, and is
    /// retried with backoff — a store with a flaky link must catch up on its own, not by hand.
    /// </summary>
    Failed = 3,
}

/// <summary>What the receiver did with an inbound operation.</summary>
public enum InboxOutcome
{
    /// <summary>Applied to the local row.</summary>
    Applied = 0,

    /// <summary>
    /// Already seen. Not an error: the transport is at-least-once by design (ADR-006), so a replay is
    /// the normal way a resumed upload finishes.
    /// </summary>
    Duplicate = 1,

    /// <summary>The entity's policy said keep the local version. The remote version was not lost — see the conflict queue.</summary>
    Conflicted = 2,

    /// <summary>Refused: an entity this node does not know, or a payload that does not parse.</summary>
    Rejected = 3,
}

/// <summary>Which way a cursor tracks.</summary>
public enum SyncDirection
{
    /// <summary>What this node has successfully sent to the peer.</summary>
    Outbound = 0,

    /// <summary>What this node has successfully received from the peer.</summary>
    Inbound = 1,
}

/// <summary>How a conflict was settled, once a person settled it.</summary>
public enum ConflictResolution
{
    /// <summary>Still in the queue.</summary>
    Unresolved = 0,

    /// <summary>The local version stands and the remote version was discarded.</summary>
    KeptLocal = 1,

    /// <summary>The remote version was applied over the local one.</summary>
    TookRemote = 2,
}
