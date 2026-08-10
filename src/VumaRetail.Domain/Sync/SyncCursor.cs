using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sync;

/// <summary>
/// How far this node and a peer have got with each other, per direction.
/// </summary>
/// <remarks>
/// <para>
/// The cursor is what makes a transfer <em>resumable</em> rather than restartable. Without it, a
/// store that lost its link for a week would either re-send a week of sales the cloud already has, or
/// start from now and lose the week — and ADR-006's promise is "resumable after any crash or outage
/// of any length".
/// </para>
/// <para>
/// It advances only on a confirmed acknowledgement. A cursor moved optimistically is a cursor that
/// skips whatever was in flight when the link dropped, which is data loss with a tidy audit trail.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class SyncCursor : Entity
{
    private SyncCursor(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SyncCursor()
    {
    }

    /// <summary>The peer this cursor tracks — <c>cloud</c>, <c>store:{id}</c>, <c>terminal:{id}</c>.</summary>
    public string PeerNode { get; private set; } = string.Empty;

    /// <summary>Whether this cursor tracks what was sent to the peer or received from it.</summary>
    public SyncDirection Direction { get; private set; }

    /// <summary>The highest stamp confirmed in this direction, in its sortable string form.</summary>
    public string AcknowledgedStamp { get; private set; } = HlcStamp.MinValue.ToString();

    /// <summary>How many operations have been confirmed in this direction, ever.</summary>
    public long AcknowledgedCount { get; private set; }

    /// <summary>
    /// The last time this node and the peer successfully exchanged anything, UTC.
    /// </summary>
    /// <remarks>
    /// Read by the health surface, and deliberately <em>not</em> read by anything that restricts a
    /// tenant. ADR-028's fourth rule is that a vendor-side outage never restricts anyone, and "we
    /// have not heard from the cloud" is exactly the signal a nervous implementation would reach for.
    /// </remarks>
    public DateTimeOffset? LastContactAt { get; private set; }

    /// <summary>The highest confirmed stamp, parsed.</summary>
    public HlcStamp Acknowledged => HlcStamp.Parse(AcknowledgedStamp);

    /// <summary>Starts a cursor for a peer that has never been heard from.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, where the peer is store-scoped.</param>
    /// <param name="peerNode">The peer node id.</param>
    /// <param name="direction">Which way this cursor tracks.</param>
    public static SyncCursor Start(Guid tenantId, Guid? storeId, string peerNode, SyncDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerNode);

        return new SyncCursor(tenantId, storeId)
        {
            PeerNode = peerNode,
            Direction = direction,
        };
    }

    /// <summary>
    /// Advances the cursor to a confirmed stamp.
    /// </summary>
    /// <param name="stamp">The highest stamp the peer confirmed.</param>
    /// <param name="operations">How many operations that acknowledgement covered.</param>
    /// <param name="at">When the acknowledgement arrived, UTC.</param>
    /// <remarks>
    /// A stamp at or below the current one moves nothing but still records contact. Batches can
    /// arrive out of order after a retry, and a cursor that went backwards would re-send everything
    /// between — correct, because the receiver deduplicates, but a store re-uploading a month of
    /// sales every time one batch is retried is not a system anyone would keep running.
    /// </remarks>
    public void Advance(HlcStamp stamp, int operations, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operations);

        LastContactAt = at;

        if (stamp <= Acknowledged)
        {
            return;
        }

        AcknowledgedStamp = stamp.ToString();
        AcknowledgedCount += operations;
    }

    /// <summary>Records that contact was made without anything being confirmed.</summary>
    /// <param name="at">When contact happened, UTC.</param>
    public void RecordContact(DateTimeOffset at) => LastContactAt = at;
}
