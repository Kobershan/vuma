using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// One change in the registry waiting to reach a company database — the mechanism 06d's saga leg
/// dispatch, the barcode routing index and the group read models all publish through
/// (`docs/MULTI_COMPANY.md` §1). Distinct from <see cref="OutboxMessage"/>: that one carries the
/// terminal → store → cloud tier protocol; this one carries registry ↔ sibling-company-database traffic
/// within one tenant, which is a different transport with different peers.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this stage writes or dispatches one outside a test — the table exists so 06d has
/// somewhere to put a row on day one, per 06c's own "built here, used there" deliverable.
/// </para>
/// <para>
/// <see cref="ReplicationScope.NodeLocal"/>, the same reasoning <see cref="OutboxMessage"/> itself
/// gives: an outbox that replicated would produce an outbox row describing an outbox row, for ever.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.AppendOnly)]
public sealed class RegistryOutboxMessage : Entity
{
    private RegistryOutboxMessage(Guid tenantId, Guid operationId, string messageKind, Guid? targetCompanyId, string payload)
        : base(tenantId)
    {
        OperationId = operationId;
        MessageKind = messageKind;
        TargetCompanyId = targetCompanyId;
        Payload = payload;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RegistryOutboxMessage()
    {
    }

    /// <summary>The idempotency key the receiving company database deduplicates on.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>What this message is, for example <c>"SagaLegDispatch"</c> or <c>"RoutingIndexUpdate"</c>.</summary>
    public string MessageKind { get; private set; } = string.Empty;

    /// <summary>The one company database this reaches, or <c>null</c> for a broadcast to every company.</summary>
    public Guid? TargetCompanyId { get; private set; }

    /// <summary>The message body, as JSON.</summary>
    public string Payload { get; private set; } = "{}";

    /// <summary>Where the message is in its journey.</summary>
    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The earliest the dispatcher should try again, or <c>null</c> to try now.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>When the target acknowledged it, or <c>null</c>.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>Why the last attempt failed, truncated. For the operator, not for a retry decision.</summary>
    public string? LastError { get; private set; }

    /// <summary>Queues a change for dispatch to one or every company database.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="operationId">The idempotency key.</param>
    /// <param name="messageKind">What this message is.</param>
    /// <param name="targetCompanyId">The one company it reaches, or <c>null</c> for every company.</param>
    /// <param name="payload">The message body, as JSON.</param>
    public static RegistryOutboxMessage Capture(
        Guid tenantId,
        Guid operationId,
        string messageKind,
        Guid? targetCompanyId,
        string payload)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A registry outbox message must belong to a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(messageKind))
        {
            throw new ArgumentException("A registry outbox message must name its kind.", nameof(messageKind));
        }

        return new RegistryOutboxMessage(tenantId, operationId, messageKind.Trim(), targetCompanyId, payload ?? "{}");
    }

    /// <summary>Marks the row as handed to the transport.</summary>
    public void MarkInFlight()
    {
        Status = OutboxStatus.InFlight;
        AttemptCount++;
    }

    /// <summary>Marks the row as acknowledged by the target.</summary>
    /// <param name="at">When the acknowledgement arrived, UTC.</param>
    public void MarkDispatched(DateTimeOffset at)
    {
        Status = OutboxStatus.Dispatched;
        DispatchedAt = at;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>Records a failed attempt and when to try again. Never terminal (ADR-116).</summary>
    /// <param name="error">What went wrong, for the operator.</param>
    /// <param name="nextAttemptAt">The earliest the dispatcher should retry.</param>
    public void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(error);

        Status = OutboxStatus.Failed;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>The widest an error message is stored. Matches the column.</summary>
    public const int MaxErrorLength = 1024;
}
