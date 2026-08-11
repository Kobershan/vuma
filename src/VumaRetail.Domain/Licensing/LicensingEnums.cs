namespace VumaRetail.Domain.Licensing;

/// <summary>
/// Where a tenant sits on the enforcement ladder (ADR-028, <c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// Three rungs and no fourth. Hard lockout was ADR-027's ending and is superseded: it survives only as
/// a manual, two-person, audited vendor action for confirmed abuse, and there is deliberately no enum
/// member for it here — an automatic ladder cannot reach a state it cannot name.
/// </remarks>
public enum EnforcementLevel
{
    /// <summary>Everything works. The overwhelming majority of every tenant's life.</summary>
    Normal = 0,

    /// <summary>
    /// Everything still works, and the tenant is being told something is wrong. Banners, session
    /// notices and emails — never a refusal.
    /// </summary>
    Notice = 1,

    /// <summary>
    /// Full read, report, reprint and export; no writes, including no sales. The commercial lever,
    /// and the only restriction the ladder can produce.
    /// </summary>
    ReadOnly = 2,
}

/// <summary>
/// Why a tenant is where it is on the ladder.
/// </summary>
/// <remarks>
/// The reason is not decoration. ADR-028's second rule is that read-only must be <em>deliberate</em>,
/// and the way that is enforced is that every restriction has to name which of these put it there —
/// so a restriction with no defensible reason is a test failure rather than a support call.
/// </remarks>
public enum EnforcementReason
{
    /// <summary>Nothing is wrong.</summary>
    None = 0,

    /// <summary>
    /// This installation has never been activated against a licence key. Not a lapse — a beginning.
    /// </summary>
    NotActivated = 1,

    /// <summary>
    /// Path A. The store cannot reach the control plane and has not been able to for longer than the
    /// tolerance window. The software does not know whether the subscription is current; it knows only
    /// that it has not been told otherwise for a long time.
    /// </summary>
    CannotVerify = 2,

    /// <summary>
    /// Path B. The control plane can be reached and has said, after a <em>completed</em> dunning cycle,
    /// that the subscription is not current. The only reason that is ever a known state.
    /// </summary>
    SubscriptionLapsed = 3,

    /// <summary>
    /// A vendor-issued emergency code is in force. Full write access, expiring on its own schedule and
    /// with no network involved (<c>LICENSING.md</c> §5).
    /// </summary>
    EmergencyUnlock = 4,

    /// <summary>
    /// A vendor-granted, time-limited restoration of write access to a tenant that would otherwise be
    /// read-only — a settled dispute, a payment the gateway has not confirmed, a month-end to close.
    /// </summary>
    WriteUnlock = 5,
}

/// <summary>
/// How loudly a tenant is being warned, within <see cref="EnforcementLevel.Notice"/>.
/// </summary>
/// <remarks>
/// Separate from the level because the level decides what is <em>refused</em> and this decides what is
/// <em>shown</em>. Both ladders in <c>LICENSING.md</c> §4 escalate their warnings for days before
/// anything is refused, and a cashier mid-queue must never be the first person to find out.
/// </remarks>
public enum NoticeStage
{
    /// <summary>Nothing shown.</summary>
    None = 0,

    /// <summary>A banner in the back office, and an email to the tenant admin. The till is untouched.</summary>
    BackOfficeBanner = 1,

    /// <summary>A notice when a POS session opens — seen by a supervisor, not by a customer.</summary>
    PosSessionNotice = 2,

    /// <summary>Final notice on every channel, stating the read-only date explicitly.</summary>
    FinalNotice = 3,
}

/// <summary>What an activation is doing right now.</summary>
public enum ActivationState
{
    /// <summary>Bound to this hardware and in force.</summary>
    Active = 0,

    /// <summary>A rebind has been asked for and the vendor has not answered yet.</summary>
    RebindPending = 1,

    /// <summary>Released deliberately, freeing the binding for another machine.</summary>
    Deactivated = 2,
}

/// <summary>
/// A limit a plan puts on a tenant, and how the system behaves at its ceiling
/// (<c>LICENSING.md</c> §6).
/// </summary>
/// <remarks>
/// The hard/soft split is the important part and it is not per tenant: hard limits are checked at
/// configuration time — registering a terminal, creating a user — and never in a transaction path.
/// Nothing a cashier does mid-sale can hit one.
/// </remarks>
public enum LimitKind
{
    /// <summary>Trading locations. Hard.</summary>
    Stores = 0,

    /// <summary>Enrolled tills and back-office machines. Hard.</summary>
    Terminals = 1,

    /// <summary>People who can sign in. Hard.</summary>
    NamedUsers = 2,

    /// <summary>Transactions in a billing month. Soft — meter, warn, bill as overage. Never block.</summary>
    TransactionsPerMonth = 3,

    /// <summary>Stored bytes. Soft.</summary>
    StorageBytes = 4,

    /// <summary>API calls in a billing month. Soft.</summary>
    ApiCallsPerMonth = 5,
}

/// <summary>What a client-side integrity or clock check noticed (<c>LICENSING.md</c> §7).</summary>
/// <remarks>
/// Every one of these is <em>reported</em> and none of them restricts anybody. A false positive that
/// stopped a paying customer trading would cost far more than the piracy it prevented (ADR-026), so
/// detection lands in the vendor's abuse queue for a human and never in the enforcement policy.
/// </remarks>
public enum TamperKind
{
    /// <summary>The system clock moved backwards past the highest instant this install had seen.</summary>
    ClockRollback = 0,

    /// <summary>The licence store and its shadow copy disagree.</summary>
    ShadowMismatch = 1,

    /// <summary>The licensing assembly does not hash to what the installer recorded.</summary>
    IntegrityCheckFailed = 2,

    /// <summary>A licence or lease arrived carrying an issuance counter at or below one already seen.</summary>
    CounterRollback = 3,

    /// <summary>A signed document failed verification against the pinned public key.</summary>
    SignatureRejected = 4,
}

/// <summary>Where a tenant-granted vendor support grant stands (<c>LICENSING.md</c> §9).</summary>
public enum SupportGrantState
{
    /// <summary>The vendor has asked. Nobody has access.</summary>
    Requested = 0,

    /// <summary>A tenant admin approved it and it has not expired. The banner is showing.</summary>
    Approved = 1,

    /// <summary>The tenant said no.</summary>
    Declined = 2,

    /// <summary>The tenant ended it early.</summary>
    Revoked = 3,

    /// <summary>It ran out on its own, which is how most of them should end.</summary>
    Expired = 4,
}

/// <summary>Whether a day's metering rollup has reached the control plane.</summary>
public enum MeteringDeliveryState
{
    /// <summary>Computed and queued. Re-sendable after an outage of any length.</summary>
    Pending = 0,

    /// <summary>Accepted by the control plane.</summary>
    Sent = 1,
}
