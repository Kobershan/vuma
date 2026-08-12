using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Sync.Clock;

/// <summary>
/// The hybrid logical clock of ADR-007.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm, in three lines: take the wall clock; if it is ahead of everything seen so far, use
/// it and reset the counter; otherwise keep the highest physical time seen and increment the counter.
/// That is all it takes to get a monotonic, causally consistent order out of clocks that disagree.
/// </para>
/// <para>
/// The two cases it exists for are the ones a plain timestamp gets wrong:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The wall clock goes backwards.</b> NTP corrects a store server that had drifted, or somebody
/// changes the date on a back-office PC. With timestamps, the node starts re-issuing values it has
/// already used, and a last-writer-wins rule silently reverts every edit made during the overlap.
/// Here, the physical component simply stops moving and the counter carries the order.
/// </item>
/// <item>
/// <b>A peer's event arrives from the future.</b> The store's clock is five minutes slow; the cloud
/// sends a change stamped ahead of anything the store can issue. Without <see cref="Observe"/> the
/// store keeps stamping <em>behind</em> a change it has already applied, so its own subsequent edits
/// lose to the one they were meant to supersede — the change comes back from the dead on the next
/// sync. <see cref="Observe"/> pulls this node past the remote stamp, once, and the anomaly ends.
/// </item>
/// </list>
/// <para>
/// A <see cref="Lock"/> rather than an interlocked compare-exchange loop. The state is two fields
/// that must move together, contention is low (this is called once per changed row, not per byte),
/// and a lock that is obviously correct beats a lock-free version that is nearly correct in the code
/// that decides which version of a sale survives.
/// </para>
/// </remarks>
/// <param name="clock">The wall clock. The only place it is read is inside the lock.</param>
/// <param name="node">This node's identity, which every stamp carries as its final tie-break.</param>
public sealed class HybridLogicalClock(IClock clock, INodeIdentity node) : IHybridClock
{
    private readonly Lock _gate = new();
    private long _physical;
    private int _counter;

    /// <inheritdoc />
    public HlcStamp Current
    {
        get
        {
            lock (_gate)
            {
                return new HlcStamp(_physical, _counter, node.NodeId);
            }
        }
    }

    /// <inheritdoc />
    public HlcStamp Next()
    {
        lock (_gate)
        {
            long wall = clock.UtcNow.ToUnixTimeMilliseconds();
            long advanced = Math.Max(_physical, wall);

            // The counter resets only when physical time genuinely moved. If it did not — a stalled
            // clock, or one that was corrected backwards — physical time stays put and the counter
            // carries the order, so the stamp still increases.
            _counter = advanced == _physical ? _counter + 1 : 0;
            _physical = advanced;

            return new HlcStamp(_physical, _counter, node.NodeId);
        }
    }

    /// <inheritdoc />
    public HlcStamp Observe(HlcStamp remote)
    {
        lock (_gate)
        {
            long wall = clock.UtcNow.ToUnixTimeMilliseconds();
            long advanced = Math.Max(_physical, Math.Max(remote.PhysicalMilliseconds, wall));

            // Four cases, and each has to leave this node strictly ahead of both itself and the peer.
            // The counter is +1 in every case where the physical component did not move past both,
            // because two stamps sharing a physical millisecond and a counter would be ordered only
            // by node id — an arbitrary winner rather than a causal one.
            _counter = (advanced == _physical, advanced == remote.PhysicalMilliseconds) switch
            {
                (true, true) => Math.Max(_counter, remote.Counter) + 1,
                (true, false) => _counter + 1,
                (false, true) => remote.Counter + 1,
                // The wall clock is ahead of both, so it carries the order on its own.
                (false, false) => 0,
            };

            _physical = advanced;

            return new HlcStamp(_physical, _counter, node.NodeId);
        }
    }
}
