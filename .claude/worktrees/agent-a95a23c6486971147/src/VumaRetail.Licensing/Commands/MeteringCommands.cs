using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Licensing;
using VumaRetail.Licensing.Metering;

namespace VumaRetail.Licensing.Commands;

/// <summary>
/// Computes a day's usage rollup and queues it (<c>LICENSING.md</c> §9).
/// </summary>
/// <remarks>
/// Idempotent by <c>(node, period)</c>: running it twice for one day recomputes the same row rather
/// than queueing a second. A day that has already been accepted is left alone, because re-sending
/// different numbers for a day the vendor has billed is worse than sending slightly stale ones.
/// </remarks>
/// <param name="Period">The day to roll up. Defaults to yesterday.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RollUpMeteringCommand(DateOnly? Period = null) : ICommand<Unit>;

/// <summary>Computes the rollup and queues it.</summary>
/// <param name="metering">The queued rollups.</param>
/// <param name="collector">Builds the payload from whitelisted counters.</param>
/// <param name="tenant">The tenant this host serves.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RollUpMeteringCommandHandler(
    IMeteringRepository metering,
    MeteringCollector collector,
    ITenantContext tenant,
    INodeIdentity node,
    IClock clock) : ICommandHandler<RollUpMeteringCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        RollUpMeteringCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateOnly period = command.Period ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-1);

        MeteringPayload payload = await collector.CollectAsync(period, cancellationToken).ConfigureAwait(false);

        MeteringRecord? existing = await metering
            .FindAsync(node.NodeId, period, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            metering.Add(MeteringRecord.Queue(
                tenant.TenantId,
                tenant.StoreId,
                node.NodeId,
                period,
                payload.ToJson()));
        }
        else
        {
            existing.Recompute(payload.ToJson());
        }

        return Unit.Value;
    }
}

/// <summary>How many rollups a delivery pass sent and how many are still waiting.</summary>
/// <param name="Sent">How many the control plane accepted.</param>
/// <param name="Outstanding">How many are still queued.</param>
public sealed record MeteringDeliveryResult(int Sent, int Outstanding);

/// <summary>
/// Delivers queued rollups to the control plane.
/// </summary>
/// <remarks>
/// Not exempt from read-only, and that is the right way round: metering is what the vendor bills a
/// <em>trading</em> tenant on, and a read-only tenant is not trading. The queue simply waits, and a
/// tenant who has been offline for six weeks and then pays sends every missed day exactly once —
/// which is the property <c>(node, period)</c> idempotency exists for.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeliverMeteringCommand : ICommand<MeteringDeliveryResult>;

/// <summary>Sends what is queued, oldest first, and stops at the first refusal.</summary>
/// <param name="metering">The queued rollups.</param>
/// <param name="controlPlane">The vendor's device API.</param>
/// <param name="options">How many to attempt per pass.</param>
/// <param name="clock">The only source of time.</param>
public sealed class DeliverMeteringCommandHandler(
    IMeteringRepository metering,
    IControlPlaneClient controlPlane,
    LicensingOptions options,
    IClock clock) : ICommandHandler<DeliverMeteringCommand, MeteringDeliveryResult>
{
    /// <inheritdoc />
    public async Task<MeteringDeliveryResult> HandleAsync(
        DeliverMeteringCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<MeteringRecord> pending = await metering
            .ListPendingAsync(options.MeteringBatchSize, cancellationToken)
            .ConfigureAwait(false);

        int sent = 0;

        foreach (MeteringRecord record in pending)
        {
            record.RecordAttempt();

            try
            {
                await controlPlane
                    .SendMeteringAsync(record.NodeId, record.Period, record.Payload, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ControlPlaneUnreachableException)
            {
                // Stop the pass rather than hammering an endpoint that is plainly not answering. The
                // rest stay queued; nothing is lost and nothing is duplicated.
                break;
            }

            record.MarkSent(clock.UtcNow);
            sent++;
        }

        return new MeteringDeliveryResult(sent, pending.Count - sent);
    }
}
