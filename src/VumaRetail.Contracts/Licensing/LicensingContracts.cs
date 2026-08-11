namespace VumaRetail.Contracts.Licensing;

/// <summary>
/// The licence screen, in one response (<c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// Everything the stage brief asks the screen to show — plan, entitlements, limits against current
/// usage, expiry, last heartbeat, enforcement level — plus what the customer needs in order to fix it.
/// One call, because a screen that has to make five is a screen that shows five spinners on a shop's
/// back-office PC.
/// </remarks>
/// <param name="Activated">Whether this installation has been bound to a licence key.</param>
/// <param name="PlanCode">The plan the customer is on.</param>
/// <param name="EnforcementLevel">Normal, Notice or ReadOnly.</param>
/// <param name="EnforcementReason">Why.</param>
/// <param name="NoticeStage">What should be shown, and where.</param>
/// <param name="Messages">Anything the vendor wants said.</param>
/// <param name="LicenceExpiresAt">When the monthly licence stops being current, UTC.</param>
/// <param name="LeaseExpiresAt">When the current lease expires, UTC.</param>
/// <param name="LastContactAt">The last successful exchange with the licence service, UTC.</param>
/// <param name="RestrictedSince">When the current restriction began, UTC.</param>
/// <param name="NextEscalationAt">When the next rung is reached if nothing changes, UTC.</param>
/// <param name="EmergencyUnlockUntil">When an emergency access code in force expires, UTC.</param>
/// <param name="AmountDue">What is owed.</param>
/// <param name="Currency">The currency of <paramref name="AmountDue"/>.</param>
/// <param name="PayUrl">Where to pay, from inside the product.</param>
/// <param name="UpdatePaymentMethodUrl">Where to fix an expired card.</param>
/// <param name="SupportPhone">The vendor's number.</param>
/// <param name="Entitlements">The modules this plan includes.</param>
/// <param name="Limits">Each limit, its ceiling and what is currently used.</param>
public sealed record LicenceStatusResponse(
    bool Activated,
    string PlanCode,
    string EnforcementLevel,
    string EnforcementReason,
    string NoticeStage,
    IReadOnlyList<string> Messages,
    DateTimeOffset? LicenceExpiresAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? RestrictedSince,
    DateTimeOffset? NextEscalationAt,
    DateTimeOffset? EmergencyUnlockUntil,
    decimal? AmountDue,
    string? Currency,
    string? PayUrl,
    string? UpdatePaymentMethodUrl,
    string? SupportPhone,
    IReadOnlyList<ModuleEntitlementResponse> Entitlements,
    IReadOnlyList<LimitUsageResponse> Limits);

/// <summary>One module, and whether this plan includes it.</summary>
/// <param name="Module">The module.</param>
/// <param name="Description">What it is, in words a shop owner recognises on an invoice.</param>
/// <param name="Enabled">Whether it is switched on.</param>
/// <param name="Core">True for the platform modules, which are never sold separately.</param>
public sealed record ModuleEntitlementResponse(string Module, string Description, bool Enabled, bool Core);

/// <summary>One limit, its ceiling and what is used against it.</summary>
/// <param name="Limit">Which limit.</param>
/// <param name="Ceiling">What the plan allows. <c>null</c> means unlimited.</param>
/// <param name="Used">What is currently used.</param>
/// <param name="Hard">True when exceeding it refuses; false when it only meters and warns.</param>
public sealed record LimitUsageResponse(string Limit, long? Ceiling, long Used, bool Hard);

/// <summary>What a caller sends to activate this installation.</summary>
/// <param name="LicenceKey">The key, as typed. Hyphens and case do not matter.</param>
/// <param name="StoreName">What the customer calls this shop.</param>
/// <param name="ContactEmail">Where the vendor sends dunning and card-expiry warnings.</param>
public sealed record ActivateRequest(string LicenceKey, string StoreName, string ContactEmail);

/// <summary>What an activation or rebind produced.</summary>
/// <param name="ActivationId">The activation.</param>
/// <param name="TenantId">The tenant the key resolved to.</param>
/// <param name="PlanCode">The plan.</param>
/// <param name="LicenceExpiresAt">When the licence stops being current, UTC.</param>
/// <param name="EnforcementLevel">Where the tenant sits immediately afterwards.</param>
public sealed record ActivationResponse(
    Guid ActivationId,
    Guid TenantId,
    string PlanCode,
    DateTimeOffset LicenceExpiresAt,
    string EnforcementLevel);

/// <summary>What a caller sends to move the binding to different hardware.</summary>
/// <param name="Reason">HardwareFailure, Migration or DisasterRecovery.</param>
/// <param name="Evidence">Anything supporting it, for a vendor decision.</param>
public sealed record RebindRequestBody(string Reason, string? Evidence = null);

/// <summary>What a lease refresh — the "retry now" button — did.</summary>
/// <param name="Reached">
/// Whether the licence service answered. False is not an error: the store carries on.
/// </param>
/// <param name="EnforcementLevel">Where the tenant sits afterwards.</param>
/// <param name="LeaseExpiresAt">When the lease now held expires, UTC.</param>
public sealed record LeaseRefreshResponse(bool Reached, string EnforcementLevel, DateTimeOffset? LeaseExpiresAt);

/// <summary>What a caller sends to redeem an emergency access code.</summary>
/// <param name="Code">The code, as read over the phone by support.</param>
public sealed record RedeemEmergencyCodeRequest(string Code);

/// <summary>What redeeming an emergency code produced.</summary>
/// <param name="ExpiresAt">When the unlock ends, UTC. Enforced with no network involved.</param>
public sealed record EmergencyCodeResponse(DateTimeOffset ExpiresAt);

/// <summary>
/// A terminal's sub-lease: what this till may do, and until when.
/// </summary>
/// <remarks>
/// A till never needs internet of its own (<c>LICENSING.md</c> §2). It asks its store server, over
/// the LAN, on an already terminal-authenticated connection, and gets back the store's entitlements
/// capped by the store's own lease.
/// </remarks>
/// <param name="TerminalId">The terminal this sub-lease was issued to.</param>
/// <param name="EnforcementLevel">What the till may do.</param>
/// <param name="Entitlements">The modules in force.</param>
/// <param name="IssuedAt">When it was issued, UTC.</param>
/// <param name="ExpiresAt">When it expires, UTC. Never later than the store server's own lease.</param>
public sealed record SubLeaseResponse(
    Guid TerminalId,
    string EnforcementLevel,
    IReadOnlyList<string> Entitlements,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>One vendor support-access request or grant, as the tenant sees it.</summary>
/// <param name="Id">The grant.</param>
/// <param name="RequestedBy">Which vendor person asked.</param>
/// <param name="Reason">Why they asked.</param>
/// <param name="Scope">What they asked for.</param>
/// <param name="State">Requested, Approved, Declined, Revoked or Expired.</param>
/// <param name="RequestedAt">When they asked, UTC.</param>
/// <param name="DecidedBy">Who at the tenant answered.</param>
/// <param name="DecidedAt">When, UTC.</param>
/// <param name="ExpiresAt">When the access ends, UTC.</param>
/// <param name="Active">True while the vendor can actually see anything — this drives the banner.</param>
public sealed record SupportGrantResponse(
    Guid Id,
    string RequestedBy,
    string Reason,
    string Scope,
    string State,
    DateTimeOffset RequestedAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiresAt,
    bool Active);

/// <summary>What a caller sends to approve a support request.</summary>
/// <param name="DurationHours">How long access lasts. Defaults to the configured four hours.</param>
public sealed record ApproveSupportAccessRequest(double? DurationHours = null);

/// <summary>One day's metering rollup, as the tenant may inspect it.</summary>
/// <param name="Period">The day, <c>yyyy-MM-dd</c>.</param>
/// <param name="NodeId">The node the counts were taken on.</param>
/// <param name="State">Pending or Sent.</param>
/// <param name="SentAt">When the vendor accepted it, UTC.</param>
/// <param name="Payload">
/// The counters themselves, verbatim. A tenant can always see exactly what was sent about them — which
/// is what makes the privacy claim in <c>LICENSING.md</c> §9 checkable rather than merely stated.
/// </param>
public sealed record MeteringRecordResponse(
    string Period,
    string NodeId,
    string State,
    DateTimeOffset? SentAt,
    string Payload);
