using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Sync.Protocol;

/// <summary>What the receiver should do with an inbound operation.</summary>
public enum ConflictOutcome
{
    /// <summary>Apply the remote version over the local row.</summary>
    TakeRemote = 0,

    /// <summary>Leave the local row alone and tell the sender it was superseded.</summary>
    KeepLocal = 1,

    /// <summary>Insert the remote row alongside; nothing is overwritten.</summary>
    Append = 2,

    /// <summary>Leave the local row alone and open a conflict entry with both versions.</summary>
    Review = 3,
}

/// <summary>
/// Settles a divergence from the entity's declared <see cref="ConflictPolicy"/> (ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the policy, the two stamps and the two nodes' tiers. That is deliberate and it
/// is the security property, not a style: the resolution must not depend on anything the <em>sender</em>
/// controls beyond its own stamp. A sender that could nominate a policy could overwrite any row on
/// any node, and the receiver would have no basis to refuse.
/// </para>
/// <para>
/// It is also what makes the whole thing testable as a table. Five policies × three stamp orderings ×
/// the tiers involved is a small enough space to enumerate, and enumerating it is the only way to
/// know that the awkward combinations — <c>StoreWins</c> evaluated <em>on</em> the store,
/// <c>AppendOnly</c> with an older remote stamp — do what the ADR says rather than what the first
/// implementation happened to do.
/// </para>
/// </remarks>
public static class ConflictResolver
{
    /// <summary>
    /// Decides what to do with an inbound operation that targets a row this node already has.
    /// </summary>
    /// <param name="policy">The target entity's declared conflict policy.</param>
    /// <param name="localStamp">This node's stamp for the row.</param>
    /// <param name="remoteStamp">The sender's stamp for the operation.</param>
    /// <param name="localKind">This node's tier.</param>
    /// <param name="remoteKind">The sender's tier.</param>
    /// <returns>What the receiver should do.</returns>
    public static ConflictOutcome Resolve(
        ConflictPolicy policy,
        HlcStamp localStamp,
        HlcStamp remoteStamp,
        NodeKind localKind,
        NodeKind remoteKind)
        => policy switch
        {
            // The stamps are totally ordered, so "newer" means the same thing on both nodes and the
            // two of them converge without talking. An equal stamp is the same write arriving twice
            // and is left alone — re-applying it would be a no-op that still burned a row version.
            ConflictPolicy.LastWriterWins => remoteStamp > localStamp
                ? ConflictOutcome.TakeRemote
                : ConflictOutcome.KeepLocal,

            // Authority, not order. Whoever is nearer the store wins, whatever the clocks say —
            // the store is the source of truth for the store (R2), so a cloud-side edit to a row the
            // store owns loses even if it happened later.
            ConflictPolicy.StoreWins => Authority(localKind, remoteKind, NodeKind.Store),
            ConflictPolicy.CloudWins => Authority(localKind, remoteKind, NodeKind.Cloud),

            // ADR-005: entries accumulate, they are never overwritten. A stock ledger line or a
            // journal from another node is simply another line, and two nodes writing independent
            // entries is not a conflict at all — it is the normal case, merged non-destructively.
            ConflictPolicy.AppendOnly => ConflictOutcome.Append,

            // A genuine business conflict — two stores editing one supplier. Picking a winner
            // automatically would silently discard a real decision somebody made.
            ConflictPolicy.ManualReview => ConflictOutcome.Review,

            _ => ConflictOutcome.Review,
        };

    /// <summary>
    /// Resolves an authority policy: the node nearer <paramref name="authoritative"/> wins.
    /// </summary>
    /// <param name="localKind">This node's tier.</param>
    /// <param name="remoteKind">The sender's tier.</param>
    /// <param name="authoritative">The tier the policy names.</param>
    /// <remarks>
    /// The case that catches people out is the policy evaluated <em>on</em> the authoritative node:
    /// <c>StoreWins</c> on the store server means the local row wins, so an inbound change from the
    /// cloud is refused. That is correct and it is why the outcome is not simply "take the remote if
    /// the remote is authoritative" — both directions have to be answered.
    /// </remarks>
    private static ConflictOutcome Authority(NodeKind localKind, NodeKind remoteKind, NodeKind authoritative)
    {
        if (localKind == authoritative)
        {
            return ConflictOutcome.KeepLocal;
        }

        if (remoteKind == authoritative)
        {
            return ConflictOutcome.TakeRemote;
        }

        // Neither side is the authority the policy names — a terminal receiving from another
        // terminal, say. There is no basis in the policy to pick a winner, and guessing is how
        // somebody's edit disappears, so it goes to a person.
        return ConflictOutcome.Review;
    }
}
