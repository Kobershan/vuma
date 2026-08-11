using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions.Licensing;

/// <summary>
/// The vendor's device API, as this installation sees it (<c>docs/API_CONTROL_PLANE.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// ADR-022's shape: an interface with a working fake, so the whole device side is testable — and
/// demonstrable — without a control plane in the room. <c>InProcessControlPlane</c> in
/// <c>VumaRetail.Licensing</c> is that fake, and it signs real documents with a real key, so the
/// verification path under test is the production one.
/// </para>
/// <para>
/// <b>Every method distinguishes two failures and the distinction is the most important thing in this
/// module.</b> A timeout, a 500, a dropped connection or a garbage body is
/// <see cref="ControlPlaneUnreachableException"/> and means <em>we do not know</em>. A well-formed
/// refusal — the key is invalid, the subscription is not active, the licence is already bound
/// elsewhere — is <see cref="ControlPlaneRefusedException"/> and means <em>we have been told</em>.
/// Only the second is ever grounds for anything, and one bad vendor deployment must never look like
/// the second (ADR-028's fourth rule).
/// </para>
/// </remarks>
public interface IControlPlaneClient
{
    /// <summary>Binds a licence key to this machine.</summary>
    /// <param name="request">The key, the fingerprint and what this machine is.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ControlPlaneUnreachableException">The control plane could not be reached.</exception>
    /// <exception cref="ControlPlaneRefusedException">The control plane refused, and said why.</exception>
    Task<ActivationGrant> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a binding to different hardware.
    /// </summary>
    /// <param name="request">The new fingerprint and the reason.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// <see cref="RebindReason.DisasterRecovery"/> is auto-approved, always. If a store has burned
    /// down, the last thing anybody needs is an activation error (<c>LICENSING.md</c> §3).
    /// </remarks>
    Task<ActivationGrant> RebindAsync(RebindRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the lease. Called on start and every 24 hours.</summary>
    /// <param name="request">Who is asking and what they currently hold.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<LeaseGrant> RefreshLeaseAsync(LeaseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reports health and collects any commands the vendor has queued.</summary>
    /// <param name="request">Counts and health only (R10).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<HeartbeatAcknowledgement> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Delivers a day's metering rollup, idempotently.</summary>
    /// <param name="nodeId">The node the counts were taken on.</param>
    /// <param name="period">The day.</param>
    /// <param name="payload">The whitelisted counters, already serialised.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SendMeteringAsync(
        string nodeId,
        DateOnly period,
        string payload,
        CancellationToken cancellationToken = default);
}

/// <summary>What a device sends to activate.</summary>
/// <param name="LicenceKey">The key the customer typed.</param>
/// <param name="FingerprintDigest">The whole fingerprint's digest.</param>
/// <param name="FingerprintScoreable">
/// The salted per-component hashes, so the vendor can score a later rebind. Never raw identifiers.
/// </param>
/// <param name="InstallId">This installation's stable id.</param>
/// <param name="NodeId">The sync node id.</param>
/// <param name="StoreName">What the customer calls this shop, for the vendor's console.</param>
/// <param name="ContactEmail">Where the vendor sends dunning and expiry warnings.</param>
/// <param name="Version">The software version.</param>
public sealed record ActivationRequest(
    string LicenceKey,
    string FingerprintDigest,
    IReadOnlyDictionary<FingerprintComponent, string> FingerprintScoreable,
    Guid InstallId,
    string NodeId,
    string StoreName,
    string ContactEmail,
    string Version);

/// <summary>Why a binding is moving.</summary>
public enum RebindReason
{
    /// <summary>The machine died and was replaced.</summary>
    HardwareFailure = 0,

    /// <summary>The customer moved to a different machine deliberately.</summary>
    Migration = 1,

    /// <summary>
    /// A restore. Auto-approved, always, without exception (<c>LICENSING.md</c> §3).
    /// </summary>
    DisasterRecovery = 2,
}

/// <summary>What a device sends to move its binding.</summary>
/// <param name="ActivationReference">The control plane's id for the activation.</param>
/// <param name="FingerprintDigest">The new machine's digest.</param>
/// <param name="FingerprintScoreable">The new machine's salted component hashes.</param>
/// <param name="Reason">Why.</param>
/// <param name="Evidence">Anything supporting it, for a vendor decision.</param>
public sealed record RebindRequest(
    Guid ActivationReference,
    string FingerprintDigest,
    IReadOnlyDictionary<FingerprintComponent, string> FingerprintScoreable,
    RebindReason Reason,
    string? Evidence = null);

/// <summary>What the control plane hands back on activation or rebind.</summary>
/// <param name="TenantId">The tenant the key resolved to.</param>
/// <param name="StoreId">The store, where the key is store-scoped.</param>
/// <param name="ActivationReference">The control plane's id for the activation.</param>
/// <param name="Licence">The signed licence document.</param>
/// <param name="Lease">The signed lease document.</param>
/// <param name="PendingVendorApproval">
/// True when a rebind needs a person at the vendor to approve it. The store keeps running on what it
/// already holds in the meantime — a pending rebind restricts nobody.
/// </param>
public sealed record ActivationGrant(
    Guid TenantId,
    Guid? StoreId,
    Guid ActivationReference,
    string Licence,
    string Lease,
    bool PendingVendorApproval = false);

/// <summary>What a device sends to refresh its lease.</summary>
/// <param name="NodeId">The node.</param>
/// <param name="ActivationReference">The control plane's id for the activation.</param>
/// <param name="FingerprintDigest">The machine's current digest, so a clone is visible.</param>
/// <param name="IssuanceCounter">The highest licence counter this node has seen.</param>
/// <param name="WallClock">What this machine's clock says, so a rollback is visible.</param>
/// <param name="BootId">This process's boot id.</param>
/// <param name="Version">The software version.</param>
public sealed record LeaseRequest(
    string NodeId,
    Guid ActivationReference,
    string FingerprintDigest,
    long IssuanceCounter,
    DateTimeOffset WallClock,
    Guid BootId,
    string Version);

/// <summary>The signed lease, and whatever the vendor wants the licence screen to say.</summary>
/// <param name="Lease">The signed lease document.</param>
/// <param name="Licence">A re-issued licence, when the plan changed. Usually <c>null</c>.</param>
/// <param name="AmountDue">What is owed, if anything.</param>
/// <param name="PayUrl">Where to pay.</param>
/// <param name="UpdatePaymentMethodUrl">Where to fix an expired card.</param>
/// <param name="DunningCompletedAt">When dunning completed, if it has. Path B turns on this.</param>
/// <param name="WriteUnlockUntil">Until when a vendor write unlock is in force.</param>
/// <param name="SupportPhone">The vendor's number.</param>
/// <param name="Messages">Anything else to show.</param>
public sealed record LeaseGrant(
    string Lease,
    string? Licence = null,
    Money? AmountDue = null,
    string? PayUrl = null,
    string? UpdatePaymentMethodUrl = null,
    DateTimeOffset? DunningCompletedAt = null,
    DateTimeOffset? WriteUnlockUntil = null,
    string? SupportPhone = null,
    IReadOnlyList<string>? Messages = null);

/// <summary>
/// What a heartbeat reports (<c>docs/API_CONTROL_PLANE.md</c> §2).
/// </summary>
/// <remarks>
/// Every field is a count, a timestamp, an id or an enum. There is no business data here and there is
/// no room for any — R10 caps what may leave a tenant's premises for vendor purposes, and the cap is a
/// property of this record's shape rather than of the code that fills it in.
/// </remarks>
/// <param name="NodeId">The node.</param>
/// <param name="ActivationReference">The control plane's id for the activation.</param>
/// <param name="At">When the heartbeat was taken, UTC.</param>
/// <param name="UptimeSeconds">How long this process has been running.</param>
/// <param name="Version">The software version.</param>
/// <param name="TerminalsRegistered">How many terminals are enrolled.</param>
/// <param name="TerminalsOnline">How many have been seen recently.</param>
/// <param name="SyncLagSeconds">How far behind replication is.</param>
/// <param name="OutboxDepth">How many operations are waiting.</param>
/// <param name="LastBackupAt">When a snapshot was last taken.</param>
/// <param name="LastBackupVerifiedAt">When one was last read back and checked.</param>
/// <param name="IssuanceCounter">The highest licence counter seen.</param>
/// <param name="BootId">This process's boot id.</param>
/// <param name="ClockRollbackDetected">Whether the clock has ever been seen to move backwards.</param>
/// <param name="IntegrityCheck">What the assembly self-check found.</param>
/// <param name="ErrorsLast24h">How many errors were logged in the last day.</param>
/// <param name="EmergencyCodesRedeemed">Codes used since the last heartbeat, by the vendor's id.</param>
public sealed record HeartbeatRequest(
    string NodeId,
    Guid ActivationReference,
    DateTimeOffset At,
    long UptimeSeconds,
    string Version,
    int TerminalsRegistered,
    int TerminalsOnline,
    long SyncLagSeconds,
    int OutboxDepth,
    DateTimeOffset? LastBackupAt,
    DateTimeOffset? LastBackupVerifiedAt,
    long IssuanceCounter,
    Guid BootId,
    bool ClockRollbackDetected,
    IntegrityStatus IntegrityCheck,
    int ErrorsLast24h,
    IReadOnlyList<Guid> EmergencyCodesRedeemed);

/// <summary>What the assembly integrity self-check found (<c>LICENSING.md</c> §7).</summary>
public enum IntegrityStatus
{
    /// <summary>
    /// Nothing to compare against. The installer records the expected hashes (Stage 31); until then
    /// this is the honest answer, and it is reported rather than assumed to be a pass.
    /// </summary>
    Unknown = 0,

    /// <summary>The licensing assembly hashes to what was recorded.</summary>
    Verified = 1,

    /// <summary>It does not. Reported to the abuse queue; it restricts nobody.</summary>
    Failed = 2,
}

/// <summary>What the control plane says back, including any work it wants done.</summary>
/// <param name="ServerTime">The vendor's clock, so a store can see its own drift.</param>
/// <param name="Commands">Work the vendor has queued for this node.</param>
public sealed record HeartbeatAcknowledgement(
    DateTimeOffset ServerTime,
    IReadOnlyList<ControlPlaneCommand> Commands);

/// <summary>One instruction pushed down on a heartbeat response.</summary>
/// <param name="Type">Which instruction.</param>
/// <param name="Payload">Its argument, where it takes one.</param>
public sealed record ControlPlaneCommand(ControlPlaneCommandType Type, string? Payload = null);

/// <summary>
/// The instructions the control plane may push (<c>docs/API_CONTROL_PLANE.md</c> §2).
/// </summary>
/// <remarks>
/// A closed set, and an unrecognised instruction is ignored rather than guessed at. The heartbeat is a
/// channel from the vendor into every customer's server; the set of things it can ask for should be
/// short enough to read in one screen and boring enough that none of them is interesting to an
/// attacker who has taken over the control plane.
/// </remarks>
public enum ControlPlaneCommandType
{
    /// <summary>Fetch a new lease now rather than at the next scheduled refresh.</summary>
    RefreshLease = 0,

    /// <summary>Check for an update and apply it (Stage 31's Velopack channel).</summary>
    UpdateNow = 1,

    /// <summary>Take a backup now.</summary>
    RunBackup = 2,

    /// <summary>Package scrubbed diagnostics for support.</summary>
    CollectDiagnostics = 3,

    /// <summary>Move to a different release channel.</summary>
    SetChannel = 4,

    /// <summary>Release the activation. The customer has cancelled and been offboarded.</summary>
    Deactivate = 5,

    /// <summary>End any active support-access grant immediately.</summary>
    RevokeSupportAccess = 6,

    /// <summary>A vendor person is asking for a time-boxed support grant. The tenant still decides.</summary>
    RequestSupportAccess = 7,
}

/// <summary>
/// The control plane could not be reached, or answered with something that is not an answer.
/// </summary>
/// <remarks>
/// <b>Never grounds for restricting anybody.</b> This is the exception a timeout, a 500, a TLS failure
/// and a malformed body all become, and the enforcement policy treats every one of them as "we do not
/// know" — which is Path A, which is a fortnight of normal trading, not a lockout. Deliberately not a
/// <see cref="DomainException"/>: nothing about the business said no.
/// </remarks>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The underlying failure, where there is one.</param>
public sealed class ControlPlaneUnreachableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Why the control plane refused.</summary>
public enum ControlPlaneRefusal
{
    /// <summary>No such key, or it is not one of ours.</summary>
    LicenceKeyInvalid = 0,

    /// <summary>Already bound to different hardware. The response says where to ask for a rebind.</summary>
    LicenceAlreadyActivated = 1,

    /// <summary>The key is real and the subscription is not active. A known state.</summary>
    SubscriptionNotActive = 2,

    /// <summary>This node is not the one the activation belongs to.</summary>
    ActivationUnknown = 3,

    /// <summary>The rebind allowance is used up and a person has to approve it.</summary>
    RebindNotPermitted = 4,
}

/// <summary>The control plane answered, and the answer was no.</summary>
/// <param name="refusal">Which refusal.</param>
/// <param name="message">What to tell the person at the keyboard.</param>
public sealed class ControlPlaneRefusedException(ControlPlaneRefusal refusal, string message)
    : DomainException(CodeFor(refusal), message, KindFor(refusal))
{
    /// <summary>Which refusal.</summary>
    public ControlPlaneRefusal Refusal { get; } = refusal;

    private static string CodeFor(ControlPlaneRefusal refusal) => refusal switch
    {
        ControlPlaneRefusal.LicenceKeyInvalid => "LICENCE_KEY_INVALID",
        ControlPlaneRefusal.LicenceAlreadyActivated => "LICENCE_ALREADY_ACTIVATED",
        ControlPlaneRefusal.SubscriptionNotActive => "SUBSCRIPTION_NOT_ACTIVE",
        ControlPlaneRefusal.ActivationUnknown => "LICENCE_ACTIVATION_UNKNOWN",
        _ => "LICENCE_REBIND_NOT_PERMITTED",
    };

    private static DomainProblemKind KindFor(ControlPlaneRefusal refusal) => refusal switch
    {
        ControlPlaneRefusal.LicenceAlreadyActivated => DomainProblemKind.Conflict,
        ControlPlaneRefusal.ActivationUnknown => DomainProblemKind.NotFound,
        _ => DomainProblemKind.Rule,
    };
}
