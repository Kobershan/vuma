using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// A vendor emergency write code that has been used on this installation (<c>LICENSING.md</c> §5).
/// </summary>
/// <remarks>
/// <para>
/// The row exists for two reasons and both matter. It is what makes a code <b>single-use</b> — the
/// unique index on <c>(tenant_id, code_reference)</c> is the guarantee, not the lookup — and it is
/// what gets reported at the next heartbeat, because repeated emergency codes to one tenant is a
/// signal the vendor should see rather than a favour that quietly becomes the billing model.
/// </para>
/// <para>
/// A code works with <b>no internet at all</b>. It is verified against the same pinned public key as a
/// licence, so it cannot be forged, and it carries its own expiry, so it cannot outlive its purpose
/// even on a machine that never reconnects.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class EmergencyUnlock : Entity
{
    private EmergencyUnlock(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private EmergencyUnlock()
    {
    }

    /// <summary>The vendor's id for the code. Unique per tenant, which is what stops reuse.</summary>
    public Guid CodeReference { get; private set; }

    /// <summary>When it was typed in, UTC.</summary>
    public DateTimeOffset RedeemedAt { get; private set; }

    /// <summary>When it stops working, UTC. From the signed code, not from this machine's clock.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The vendor's reason for issuing it, for the audit trail.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When the redemption was reported to the control plane, UTC.</summary>
    public DateTimeOffset? ReportedAt { get; private set; }

    /// <summary>Records a redemption whose signature has already been verified.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store.</param>
    /// <param name="codeReference">The vendor's id for the code.</param>
    /// <param name="redeemedAt">When it was typed in, UTC.</param>
    /// <param name="expiresAt">When it stops working, UTC.</param>
    /// <param name="reason">The vendor's reason.</param>
    /// <returns>The redemption record.</returns>
    public static EmergencyUnlock Redeem(
        Guid tenantId,
        Guid? storeId,
        Guid codeReference,
        DateTimeOffset redeemedAt,
        DateTimeOffset expiresAt,
        string reason)
        => new(tenantId, storeId)
        {
            CodeReference = codeReference,
            RedeemedAt = redeemedAt,
            ExpiresAt = expiresAt,
            Reason = reason,
        };

    /// <summary>True while the unlock is still in force at the given instant.</summary>
    /// <param name="at">The instant to test, UTC.</param>
    public bool IsInForceAt(DateTimeOffset at) => at < ExpiresAt;

    /// <summary>Marks the redemption as reported to the vendor.</summary>
    /// <param name="at">When, UTC.</param>
    public void MarkReported(DateTimeOffset at) => ReportedAt = at;
}

/// <summary>
/// Something the client-side hardening noticed (<c>LICENSING.md</c> §7).
/// </summary>
/// <remarks>
/// <b>Nothing here ever restricts anybody.</b> ADR-026 chose detection over prevention precisely
/// because a false positive that stops a paying customer trading costs far more than a week of piracy,
/// so a flag is written, reported at the next heartbeat, and judged by a human in the vendor's abuse
/// queue. A clock set backwards produces one of these and buys nothing; it does not produce a lockout.
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class TamperFlag : Entity
{
    private TamperFlag(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private TamperFlag()
    {
    }

    /// <summary>What was noticed.</summary>
    public TamperKind Kind { get; private set; }

    /// <summary>When, UTC.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>The detail, scrubbed of anything that is not licensing state.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>When it was reported to the control plane, UTC.</summary>
    public DateTimeOffset? ReportedAt { get; private set; }

    /// <summary>The longest detail that will be stored.</summary>
    public const int MaxDetailLength = 1024;

    /// <summary>Raises a flag.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store.</param>
    /// <param name="kind">What was noticed.</param>
    /// <param name="detectedAt">When, UTC.</param>
    /// <param name="detail">The detail.</param>
    /// <returns>The flag.</returns>
    public static TamperFlag Raise(
        Guid tenantId,
        Guid? storeId,
        TamperKind kind,
        DateTimeOffset detectedAt,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new TamperFlag(tenantId, storeId)
        {
            Kind = kind,
            DetectedAt = detectedAt,
            Detail = detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail,
        };
    }

    /// <summary>Marks the flag as reported to the vendor.</summary>
    /// <param name="at">When, UTC.</param>
    public void MarkReported(DateTimeOffset at) => ReportedAt = at;
}

/// <summary>
/// The highest wall-clock instant this installation has ever seen (<c>LICENSING.md</c> §7).
/// </summary>
/// <remarks>
/// One row per node. Its only job is to make setting the system clock backwards worthless: a lease's
/// age is measured against <c>max(now, highest seen)</c>, so winding the clock back cannot extend
/// anything — and the attempt raises a <see cref="TamperFlag"/> rather than a restriction, because a
/// clock that jumps is far more often a dead CMOS battery than a fraud.
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class ClockWatermark : Entity
{
    private ClockWatermark(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ClockWatermark()
    {
    }

    /// <summary>The node this watermark belongs to.</summary>
    public string NodeId { get; private set; } = string.Empty;

    /// <summary>The highest instant ever observed here, UTC.</summary>
    public DateTimeOffset HighestSeen { get; private set; }

    /// <summary>How many times the clock has been observed to move backwards.</summary>
    public int RollbackCount { get; private set; }

    /// <summary>Starts a watermark for a node.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store.</param>
    /// <param name="nodeId">The node.</param>
    /// <param name="at">The instant to start from, UTC.</param>
    /// <returns>The watermark.</returns>
    public static ClockWatermark Start(Guid tenantId, Guid? storeId, string nodeId, DateTimeOffset at)
        => new(tenantId, storeId)
        {
            NodeId = nodeId,
            HighestSeen = at,
        };

    /// <summary>
    /// Folds an observed instant in.
    /// </summary>
    /// <param name="at">What the wall clock says now, UTC.</param>
    /// <returns>True when the clock had moved backwards — the caller raises the flag.</returns>
    public bool Observe(DateTimeOffset at)
    {
        if (at >= HighestSeen)
        {
            HighestSeen = at;
            return false;
        }

        RollbackCount++;
        return true;
    }

    /// <summary>The instant to reason about: never earlier than the highest ever seen.</summary>
    /// <param name="wallClock">What the wall clock says, UTC.</param>
    public DateTimeOffset Effective(DateTimeOffset wallClock)
        => wallClock > HighestSeen ? wallClock : HighestSeen;
}

/// <summary>
/// One day's usage rollup, queued for the control plane (<c>LICENSING.md</c> §9).
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by <c>(node, period)</c> — the unique index says so — because a store that has been
/// offline for six weeks must be able to catch up without billing itself twice, and the retry that
/// crosses with a slow acknowledgement must not create a second day.
/// </para>
/// <para>
/// The payload is <b>counts and health only</b>. R10 and ADR-024 both draw the line in the same place,
/// and it is enforced in code rather than by review: the payload is built from a whitelist of
/// aggregate counters, and a test asserts the serialised document carries no free text, no name and
/// no field sourced from a business table.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class MeteringRecord : Entity
{
    private MeteringRecord(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private MeteringRecord()
    {
    }

    /// <summary>The node the counts were taken on.</summary>
    public string NodeId { get; private set; } = string.Empty;

    /// <summary>The day being reported, as <c>yyyy-MM-dd</c>.</summary>
    public DateOnly Period { get; private set; }

    /// <summary>The whitelisted counters, as JSON.</summary>
    public string Payload { get; private set; } = "{}";

    /// <summary>Whether it has reached the control plane.</summary>
    public MeteringDeliveryState State { get; private set; } = MeteringDeliveryState.Pending;

    /// <summary>When it was accepted, UTC.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Queues a day's rollup.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store.</param>
    /// <param name="nodeId">The node.</param>
    /// <param name="period">The day.</param>
    /// <param name="payload">The whitelisted counters, as JSON.</param>
    /// <returns>The record.</returns>
    public static MeteringRecord Queue(
        Guid tenantId,
        Guid? storeId,
        string nodeId,
        DateOnly period,
        string payload)
        => new(tenantId, storeId)
        {
            NodeId = nodeId,
            Period = period,
            Payload = payload,
        };

    /// <summary>Replaces the counters for a day that has been recomputed but not yet sent.</summary>
    /// <param name="payload">The recomputed counters.</param>
    public void Recompute(string payload)
    {
        if (State is MeteringDeliveryState.Sent)
        {
            return;
        }

        Payload = payload;
    }

    /// <summary>Records a delivery attempt that failed.</summary>
    public void RecordAttempt() => AttemptCount++;

    /// <summary>Records acceptance by the control plane.</summary>
    /// <param name="at">When, UTC.</param>
    public void MarkSent(DateTimeOffset at)
    {
        State = MeteringDeliveryState.Sent;
        SentAt = at;
    }
}

/// <summary>
/// A tenant-granted, time-boxed vendor support grant (<c>LICENSING.md</c> §9).
/// </summary>
/// <remarks>
/// <para>
/// There is <b>no route to a tenant's business data without one of these in the
/// <see cref="SupportGrantState.Approved"/> state</b>, and it is built that way now because consent
/// cannot be retrofitted. While a grant is live the tenant's own UI shows a banner, and every vendor
/// action is written to the tenant's audit trail as well as the vendor's.
/// </para>
/// <para>
/// <see cref="ReplicationScope.Bidirectional"/>: the vendor asks through the control plane and the
/// tenant may answer from the back office or from the Android admin app (Stage 30), which reaches the
/// cloud tier rather than the store server. Both ends can therefore change it, which is the definition
/// of the expensive case — and it is the right one here, because a consent decision the customer made
/// on their phone has to reach the till that shows the banner.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.LastWriterWins)]
public sealed class SupportGrant : Entity
{
    private SupportGrant(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SupportGrant()
    {
    }

    /// <summary>The control plane's id for the request, so both audit trails can be joined.</summary>
    public Guid GrantReference { get; private set; }

    /// <summary>Which vendor person asked. Recorded, not trusted.</summary>
    public string RequestedBy { get; private set; } = string.Empty;

    /// <summary>Why they asked, in their own words, shown to the tenant admin.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>What they asked for access to.</summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>When the request arrived, UTC.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Where the request stands.</summary>
    public SupportGrantState State { get; private set; } = SupportGrantState.Requested;

    /// <summary>Who at the tenant answered.</summary>
    public string? DecidedBy { get; private set; }

    /// <summary>When they answered, UTC.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>When the grant lapses on its own, UTC.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Records a request arriving from the control plane.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, where the request is store-scoped.</param>
    /// <param name="grantReference">The control plane's id.</param>
    /// <param name="requestedBy">The vendor person.</param>
    /// <param name="reason">Their reason.</param>
    /// <param name="scope">What they want to see.</param>
    /// <param name="requestedAt">When, UTC.</param>
    /// <returns>The request.</returns>
    public static SupportGrant Request(
        Guid tenantId,
        Guid? storeId,
        Guid grantReference,
        string requestedBy,
        string reason,
        string scope,
        DateTimeOffset requestedAt)
        => new(tenantId, storeId)
        {
            GrantReference = grantReference,
            RequestedBy = requestedBy,
            Reason = reason,
            Scope = scope,
            RequestedAt = requestedAt,
        };

    /// <summary>The tenant admin approves, for a bounded time.</summary>
    /// <param name="decidedBy">Who approved.</param>
    /// <param name="at">When, UTC.</param>
    /// <param name="duration">How long the grant lasts.</param>
    /// <exception cref="SupportGrantStateException">The request has already been answered.</exception>
    public void Approve(string decidedBy, DateTimeOffset at, TimeSpan duration)
    {
        if (State is not SupportGrantState.Requested)
        {
            throw new SupportGrantStateException($"This support request is already {State}.");
        }

        State = SupportGrantState.Approved;
        DecidedBy = decidedBy;
        DecidedAt = at;
        ExpiresAt = at + duration;
    }

    /// <summary>The tenant admin says no.</summary>
    /// <param name="decidedBy">Who declined.</param>
    /// <param name="at">When, UTC.</param>
    /// <exception cref="SupportGrantStateException">The request has already been answered.</exception>
    public void Decline(string decidedBy, DateTimeOffset at)
    {
        if (State is not SupportGrantState.Requested)
        {
            throw new SupportGrantStateException($"This support request is already {State}.");
        }

        State = SupportGrantState.Declined;
        DecidedBy = decidedBy;
        DecidedAt = at;
    }

    /// <summary>
    /// The tenant ends an approved grant early.
    /// </summary>
    /// <param name="decidedBy">Who revoked.</param>
    /// <param name="at">When, UTC.</param>
    /// <remarks>
    /// Idempotent on an already-ended grant, because "make sure nobody is in my system" is a button
    /// somebody presses twice.
    /// </remarks>
    public void Revoke(string decidedBy, DateTimeOffset at)
    {
        if (State is SupportGrantState.Revoked or SupportGrantState.Expired or SupportGrantState.Declined)
        {
            return;
        }

        State = SupportGrantState.Revoked;
        DecidedBy = decidedBy;
        DecidedAt = at;
        ExpiresAt = at;
    }

    /// <summary>True while a vendor may actually see anything.</summary>
    /// <param name="at">The instant to test, UTC.</param>
    public bool IsActiveAt(DateTimeOffset at)
        => State is SupportGrantState.Approved && ExpiresAt is { } expiry && at < expiry;
}

/// <summary>A support grant was moved into a state it cannot reach from where it is.</summary>
/// <param name="message">What was attempted.</param>
public sealed class SupportGrantStateException(string message)
    : DomainException("LICENCE_SUPPORT_GRANT_STATE", message);
