using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Sync.Dispatch;

/// <summary>How the outbox dispatcher is paced.</summary>
/// <remarks>
/// Every value here is a trade between how quickly the cloud sees a sale and how much a store's
/// connection is asked to carry. None of them affects whether a sale completes — R1 is satisfied by
/// the dispatcher being somewhere else entirely, not by it being fast.
/// </remarks>
public sealed class OutboxDispatcherOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Sync:Dispatcher";

    /// <summary>How many operations go in one batch.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>How long to wait after an empty poll before looking again.</summary>
    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The first retry delay after a failure. Doubles per attempt.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest a failed row waits before being retried.
    /// </summary>
    /// <remarks>
    /// Capped, and the cap is why <see cref="OutboxMessage.MarkFailed"/> never gives up. A store on a
    /// bad line must catch up on its own once the line returns, and an uncapped exponential backoff
    /// would have it waiting days by the time anybody noticed.
    /// </remarks>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Whether the dispatcher runs at all. Off where there is no peer to send to.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Drains the outbox to a peer, in batches, with capped exponential backoff.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate component from anything on a sale path. R1 — "POS never stops" — is not
/// satisfied by making replication fast; it is satisfied by making it structurally unable to block a
/// sale. A till writes its outbox row inside its own transaction and is finished. Whether the cloud
/// is reachable is this class's problem and nobody else's, and the till never learns the answer.
/// </para>
/// <para>
/// Each pass is one unit of work: take what is due, mark it in flight, send it, mark the results.
/// Failure is expected rather than exceptional — a shop's ADSL drops, the cloud is being deployed —
/// so a failed pass logs at warning, backs the rows off, and comes round again.
/// </para>
/// </remarks>
/// <param name="outbox">The queue.</param>
/// <param name="cursors">Where the peer has got to.</param>
/// <param name="transport">How a batch reaches the peer.</param>
/// <param name="node">This node's identity and tier.</param>
/// <param name="tenant">The tenant whose rows are being shipped.</param>
/// <param name="unitOfWork">The transaction the delivery state is written in.</param>
/// <param name="clock">Wall-clock time, for the backoff.</param>
/// <param name="options">How the dispatcher is paced.</param>
/// <param name="logger">Where the pass summaries go.</param>
public sealed class OutboxDispatcher(
    IOutboxRepository outbox,
    ISyncCursorRepository cursors,
    ISyncTransport transport,
    INodeIdentity node,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    IClock clock,
    OutboxDispatcherOptions options,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>
    /// Ships one batch, if anything is due.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many operations the peer settled. Zero means there was nothing to do, or it failed.</returns>
    /// <remarks>
    /// One pass rather than a loop, so the hosted service owns the pacing and a test owns the
    /// stepping. Sync behaviour that can only be observed by waiting is sync behaviour nobody
    /// asserts.
    /// </remarks>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<OutboxMessage> due = await outbox
            .ListDueAsync(options.BatchSize, now, cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<OutboxMessage> shippable = [.. due.Where(IsForThisPeer)];

        if (shippable.Count == 0)
        {
            // Rows destined for a tier this transport does not serve — a StoreLocal change on a store
            // server with only a cloud peer. They are settled rather than left to be re-read on every
            // pass for ever.
            foreach (OutboxMessage message in due)
            {
                message.MarkDispatched(now);
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        foreach (OutboxMessage message in shippable)
        {
            message.MarkInFlight();
        }

        SyncBatch batch = new(
            node.NodeId,
            node.Kind,
            tenant.TenantId,
            tenant.StoreId,
            [.. shippable.Select(ToOperation)]);

        try
        {
            SyncAcknowledgement ack = await transport.SendAsync(batch, cancellationToken).ConfigureAwait(false);

            await SettleAsync(shippable, ack, now, cancellationToken).ConfigureAwait(false);

            return ack.SettledCount;
        }
        catch (SyncTransportException failure)
        {
            // Expected, not exceptional. Warning rather than error, because a store on a domestic
            // line does this several times a week and an error-level log there trains everybody to
            // ignore the error level.
            logger.LogWarning(
                failure,
                "Could not ship {OperationCount} operations to {PeerNode}; backing off",
                shippable.Count,
                transport.PeerNode);

            foreach (OutboxMessage message in shippable)
            {
                message.MarkFailed(failure.Message, now + BackoffFor(message.AttemptCount));
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return 0;
        }
    }

    /// <summary>
    /// True when this transport's peer is a tier the message's scope is aimed at.
    /// </summary>
    /// <remarks>
    /// The scope is the entity's declaration, and the peer is where this transport points. A store
    /// server shipping to the cloud sends <c>StoreToCloud</c> and <c>Bidirectional</c> rows and holds
    /// back <c>StoreLocal</c> ones, which belong to its terminals.
    /// </remarks>
    private bool IsForThisPeer(OutboxMessage message) => message.Scope switch
    {
        ReplicationScope.NodeLocal => false,
        ReplicationScope.Bidirectional => true,
        ReplicationScope.StoreToCloud => node.Kind is NodeKind.Store or NodeKind.Terminal,
        ReplicationScope.CloudToStore => node.Kind is NodeKind.Cloud,
        ReplicationScope.StoreLocal => node.Kind is NodeKind.Terminal,
        _ => false,
    };

    private static SyncOperation ToOperation(OutboxMessage message)
        => new(
            message.OperationId,
            message.EntityType,
            message.EntityId,
            message.Operation,
            message.OperationHlc,
            message.Payload,
            message.OccurredAt);

    private async Task SettleAsync(
        IReadOnlyList<OutboxMessage> shipped,
        SyncAcknowledgement ack,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, SyncOperationResult> byOperation = ack.Results
            .ToDictionary(result => result.OperationId);

        foreach (OutboxMessage message in shipped)
        {
            if (!byOperation.TryGetValue(message.OperationId, out SyncOperationResult? result))
            {
                // The peer did not answer for this one. Not settled, so it is retried — an
                // incomplete acknowledgement must never be read as a complete one.
                message.MarkFailed("The peer did not acknowledge this operation.", now + BackoffFor(message.AttemptCount));
                continue;
            }

            if (result.Outcome == InboxOutcome.Rejected)
            {
                // A rejection is the peer saying "I will never accept this", so retrying it is
                // pointless — but it is not dropped either. The row stays Failed with the reason on
                // it, which is what somebody investigating needs, and the long backoff keeps it out
                // of the way meanwhile.
                message.MarkFailed(
                    result.Detail ?? "Rejected by the peer.",
                    now + options.MaxBackoff);
                continue;
            }

            message.MarkDispatched(now);
            message.MarkSyncState(SyncState.Synced);
        }

        await AdvanceCursorAsync(ack, now, cancellationToken).ConfigureAwait(false);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AdvanceCursorAsync(
        SyncAcknowledgement ack,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        SyncCursor? cursor = await cursors
            .FindAsync(transport.PeerNode, SyncDirection.Outbound, cancellationToken)
            .ConfigureAwait(false);

        if (cursor is null)
        {
            cursor = SyncCursor.Start(tenant.TenantId, tenant.StoreId, transport.PeerNode, SyncDirection.Outbound);
            cursors.Add(cursor);
        }

        cursor.Advance(ack.AcknowledgedStamp, ack.SettledCount, now);
    }

    /// <summary>
    /// Exponential backoff, capped. Doubles per attempt from the initial delay.
    /// </summary>
    /// <param name="attemptCount">How many attempts this row has had.</param>
    private TimeSpan BackoffFor(int attemptCount)
    {
        // Shift rather than Math.Pow, and clamped before the shift: a store offline for a fortnight
        // reaches an attempt count that would overflow a double-and-double loop long before it
        // reaches the cap.
        int steps = Math.Clamp(attemptCount - 1, 0, 20);
        double seconds = options.InitialBackoff.TotalSeconds * (1L << steps);

        return seconds >= options.MaxBackoff.TotalSeconds
            ? options.MaxBackoff
            : TimeSpan.FromSeconds(seconds);
    }
}
