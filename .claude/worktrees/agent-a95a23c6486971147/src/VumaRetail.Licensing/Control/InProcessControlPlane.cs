using System.Collections.Concurrent;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Licensing.Enforcement;
using VumaRetail.Licensing.Signing;

namespace VumaRetail.Licensing.Control;

/// <summary>How a control plane is behaving, for the tests that need it to behave badly.</summary>
/// <remarks>
/// <c>docs/TESTING.md</c> §7 requires the store to survive a control plane that is unreachable, one
/// that returns 500s, and one that returns garbage — and to treat all three identically. Making those
/// first-class states of the fake is what turns that requirement into three lines of arrangement
/// instead of three bespoke mocks.
/// </remarks>
public enum ControlPlaneBehaviour
{
    /// <summary>Answers normally.</summary>
    Healthy = 0,

    /// <summary>Does not answer at all. The store's link is cut, or the vendor is down.</summary>
    Unreachable = 1,

    /// <summary>Answers with a server error.</summary>
    ServerError = 2,

    /// <summary>Answers with something that is not an answer.</summary>
    Garbage = 3,
}

/// <summary>What the vendor knows about one tenant.</summary>
/// <remarks>
/// The vendor's half of the model, kept deliberately small: Stage 30b builds the real one, with
/// billing, dunning, the abuse queue and the console. What is here is everything the <em>device</em>
/// side can observe, which is what makes it a faithful stand-in.
/// </remarks>
public sealed class ControlPlaneTenant
{
    /// <summary>The tenant.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The store, where the licence is store-scoped.</summary>
    public Guid? StoreId { get; init; }

    /// <summary>The plan code.</summary>
    public string PlanCode { get; set; } = "standard";

    /// <summary>The module flags the plan enables.</summary>
    public IReadOnlyList<string> Entitlements { get; set; } = [];

    /// <summary>The plan's quantities.</summary>
    public LicenceLimits Limits { get; set; } = LicenceLimits.Unlimited;

    /// <summary>Whether the subscription is current.</summary>
    public bool SubscriptionCurrent { get; set; } = true;

    /// <summary>
    /// When the dunning cycle completed, with notifications recorded as delivered.
    /// </summary>
    /// <remarks>
    /// The client refuses to restrict anybody without this, whatever the lease declares. That is the
    /// engineering half of "a single failed charge never restricts anyone".
    /// </remarks>
    public DateTimeOffset? DunningCompletedAt { get; set; }

    /// <summary>What is owed.</summary>
    public Money? AmountDue { get; set; }

    /// <summary>Until when a vendor write unlock is in force.</summary>
    public DateTimeOffset? WriteUnlockUntil { get; set; }

    /// <summary>The monotonic issuance counter. Increments on every licence issued.</summary>
    public long IssuanceCounter { get; set; } = 1;

    /// <summary>The activation bound to this licence, once there is one.</summary>
    public Guid? ActivationReference { get; set; }

    /// <summary>The fingerprint that activation is bound to.</summary>
    public string? FingerprintDigest { get; set; }

    /// <summary>When this tenant was last heard from.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Instructions waiting to be pushed down on the next heartbeat.</summary>
    public ConcurrentQueue<ControlPlaneCommand> PendingCommands { get; } = new();
}

/// <summary>
/// A working control plane, in process (ADR-022).
/// </summary>
/// <remarks>
/// <para>
/// It signs real documents with a real Ed25519 key, applies the real ladder, and refuses in the real
/// ways. That is the point: the verification path, the counter checks and the whole
/// no-accidental-lockout suite run against production code with a stand-in for exactly one thing — the
/// network — and Stage 30b's job becomes putting a real service behind the same contract rather than
/// discovering the contract.
/// </para>
/// <para>
/// It is also what makes the two hardest tests in <c>docs/TESTING.md</c> §7 possible at all: "control
/// plane switched off for a full simulated trading day: zero customer impact", and "offline for 45
/// days then reconnect: metering for every missed day arrives exactly once".
/// </para>
/// </remarks>
/// <param name="signer">The vendor's signing key.</param>
/// <param name="options">The ladder, shared with the client so the two agree by construction.</param>
/// <param name="clock">The vendor's clock.</param>
public sealed class InProcessControlPlane(LicenceSigner signer, LicensingOptions options, IClock clock)
    : IControlPlaneClient
{
    private readonly ConcurrentDictionary<string, ControlPlaneTenant> _byKeyDigest = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, ControlPlaneTenant> _byActivation = new();
    private readonly ConcurrentDictionary<(string Node, DateOnly Period), string> _metering = new();

    /// <summary>How the control plane is behaving. Tests move this.</summary>
    public ControlPlaneBehaviour Behaviour { get; set; } = ControlPlaneBehaviour.Healthy;

    /// <summary>Every metering rollup received, keyed by node and day. Exactly-once is asserted on it.</summary>
    public IReadOnlyDictionary<(string Node, DateOnly Period), string> Metering => _metering;

    /// <summary>How many metering deliveries were accepted, including duplicates that were ignored.</summary>
    public int MeteringDeliveries { get; private set; }

    /// <summary>Registers a tenant and returns the licence key its customer would be sent.</summary>
    /// <param name="tenant">The tenant's subscription state.</param>
    /// <returns>The licence key.</returns>
    public LicenceKey Register(ControlPlaneTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        LicenceKey key = LicenceKey.NewKey();
        _byKeyDigest[key.Digest()] = tenant;

        return key;
    }

    /// <summary>The tenant behind a licence key, for a test that wants to change its subscription.</summary>
    /// <param name="key">The key.</param>
    public ControlPlaneTenant this[LicenceKey key] => _byKeyDigest[key.Digest()];

    /// <summary>
    /// Issues an emergency write code (<c>LICENSING.md</c> §5).
    /// </summary>
    /// <param name="tenantId">The one tenant it works for.</param>
    /// <param name="lifetime">How long it lasts. The client caps this at its configured ceiling.</param>
    /// <param name="reason">The vendor's reason, recorded at both ends.</param>
    /// <returns>The signed code, as it would be read over the phone.</returns>
    public string IssueEmergencyCode(Guid tenantId, TimeSpan lifetime, string reason = "support")
    {
        DateTimeOffset now = clock.UtcNow;

        return signer.Sign(new EmergencyCodeDocument(
            SignedDocumentKind.EmergencyCode,
            UuidV7.NewGuid(),
            tenantId,
            now,
            now + lifetime,
            reason));
    }

    /// <summary>Queues an instruction for the next heartbeat.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="command">The instruction.</param>
    public void Push(Guid tenantId, ControlPlaneCommand command)
    {
        foreach (ControlPlaneTenant tenant in _byKeyDigest.Values.Where(entry => entry.TenantId == tenantId))
        {
            tenant.PendingCommands.Enqueue(command);
        }
    }

    /// <inheritdoc />
    public Task<ActivationGrant> ActivateAsync(
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Refuse();

        LicenceKey key = LicenceKey.Parse(request.LicenceKey);

        if (!_byKeyDigest.TryGetValue(key.Digest(), out ControlPlaneTenant? tenant))
        {
            throw new ControlPlaneRefusedException(
                ControlPlaneRefusal.LicenceKeyInvalid,
                "That licence key is not one of ours.");
        }

        if (!tenant.SubscriptionCurrent)
        {
            throw new ControlPlaneRefusedException(
                ControlPlaneRefusal.SubscriptionNotActive,
                "That subscription is not active. Settle the account and try again.");
        }

        if (tenant.ActivationReference is not null
            && !string.Equals(tenant.FingerprintDigest, request.FingerprintDigest, StringComparison.Ordinal))
        {
            throw new ControlPlaneRefusedException(
                ControlPlaneRefusal.LicenceAlreadyActivated,
                "That licence is already activated on another machine. Ask us for a rebind.");
        }

        Guid activation = tenant.ActivationReference ?? UuidV7.NewGuid();

        tenant.ActivationReference = activation;
        tenant.FingerprintDigest = request.FingerprintDigest;
        tenant.LastSeenAt = clock.UtcNow;

        _byActivation[activation] = tenant;

        return Task.FromResult(new ActivationGrant(
            tenant.TenantId,
            tenant.StoreId,
            activation,
            IssueLicence(tenant, activation),
            IssueLease(tenant, activation)));
    }

    /// <inheritdoc />
    public Task<ActivationGrant> RebindAsync(RebindRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Refuse();

        ControlPlaneTenant tenant = Resolve(request.ActivationReference);

        tenant.FingerprintDigest = request.FingerprintDigest;
        tenant.LastSeenAt = clock.UtcNow;

        // Disaster recovery is auto-approved without exception (LICENSING.md §3). A store that has
        // burned down does not also need a vendor to answer the phone.
        return Task.FromResult(new ActivationGrant(
            tenant.TenantId,
            tenant.StoreId,
            request.ActivationReference,
            IssueLicence(tenant, request.ActivationReference),
            IssueLease(tenant, request.ActivationReference)));
    }

    /// <inheritdoc />
    public Task<LeaseGrant> RefreshLeaseAsync(LeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Refuse();

        ControlPlaneTenant tenant = Resolve(request.ActivationReference);

        tenant.LastSeenAt = clock.UtcNow;

        return Task.FromResult(new LeaseGrant(
            IssueLease(tenant, request.ActivationReference),
            Licence: null,
            tenant.AmountDue,
            tenant.SubscriptionCurrent ? null : "https://pay.vumaretail.app/invoice",
            "https://pay.vumaretail.app/payment-method",
            tenant.DunningCompletedAt,
            tenant.WriteUnlockUntil,
            "+27 11 000 0000",
            tenant.SubscriptionCurrent
                ? []
                : ["Your subscription is not current. Pay from the licence screen to restore access."]));
    }

    /// <inheritdoc />
    public Task<HeartbeatAcknowledgement> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Refuse();

        ControlPlaneTenant tenant = Resolve(request.ActivationReference);

        tenant.LastSeenAt = clock.UtcNow;

        List<ControlPlaneCommand> commands = [];

        while (tenant.PendingCommands.TryDequeue(out ControlPlaneCommand? queued))
        {
            commands.Add(queued);
        }

        return Task.FromResult(new HeartbeatAcknowledgement(clock.UtcNow, commands));
    }

    /// <inheritdoc />
    public Task SendMeteringAsync(
        string nodeId,
        DateOnly period,
        string payload,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        MeteringDeliveries++;

        // Idempotent by (node, period), which is the contract in docs/API_CONTROL_PLANE.md §2. A store
        // that has been offline for six weeks catches up without billing itself twice.
        _metering[(nodeId, period)] = payload;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Where the vendor would say a tenant sits, given how long since it was last heard from.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="now">The instant to evaluate at, UTC.</param>
    /// <remarks>
    /// Exposed so the equivalence <c>docs/API_CONTROL_PLANE.md</c> §2 requires can be asserted: the
    /// control plane's declared level and the client's locally computed one must agree, because a
    /// store with a severed link has to reach the same answer with nobody to ask. Both sides run the
    /// same <see cref="EnforcementPolicy"/> over the same options, so agreement is a property rather
    /// than a coincidence that has to be maintained by hand.
    /// </remarks>
    public EnforcementLevel DeclaredLevelFor(ControlPlaneTenant tenant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (!tenant.SubscriptionCurrent)
        {
            return tenant.DunningCompletedAt is { } completed && now >= completed.AddDays(options.DunningGraceDays)
                ? EnforcementLevel.ReadOnly
                : EnforcementLevel.Notice;
        }

        double days = (now - (tenant.LastSeenAt ?? now)).TotalDays;

        return days >= options.OfflineReadOnlyAfterDays
            ? EnforcementLevel.ReadOnly
            : days >= options.OfflineBannerAfterDays
                ? EnforcementLevel.Notice
                : EnforcementLevel.Normal;
    }

    private ControlPlaneTenant Resolve(Guid activation)
        => _byActivation.TryGetValue(activation, out ControlPlaneTenant? tenant)
            ? tenant
            : throw new ControlPlaneRefusedException(
                ControlPlaneRefusal.ActivationUnknown,
                "This installation is not one we know about. Activate it again.");

    private string IssueLicence(ControlPlaneTenant tenant, Guid activation)
    {
        DateTimeOffset now = clock.UtcNow;

        return signer.Sign(new LicenceDocument(
            SignedDocumentKind.Licence,
            tenant.TenantId,
            tenant.StoreId,
            activation,
            tenant.PlanCode,
            tenant.Entitlements,
            tenant.Limits,
            now,
            now.AddMonths(1),
            tenant.FingerprintDigest ?? string.Empty,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            tenant.IssuanceCounter++));
    }

    private string IssueLease(ControlPlaneTenant tenant, Guid activation)
    {
        DateTimeOffset now = clock.UtcNow;

        EnforcementLevel level = DeclaredLevelFor(tenant, now);

        // Path A is the *client's* computation and must not arrive pre-decided in the lease: a store
        // that can be reached is by definition not on Path A, so a lease that declared CannotVerify
        // would be a contradiction the client would then act on.
        EnforcementReason reason = tenant.SubscriptionCurrent
            ? EnforcementReason.None
            : EnforcementReason.SubscriptionLapsed;

        return signer.Sign(new LeaseDocument(
            SignedDocumentKind.Lease,
            UuidV7.NewGuid(),
            tenant.TenantId,
            tenant.StoreId,
            activation,
            tenant.Entitlements,
            tenant.Limits,
            tenant.SubscriptionCurrent ? EnforcementLevel.Normal : level,
            reason,
            now,
            now + options.LeaseLifetime,
            tenant.IssuanceCounter,
            tenant.DunningCompletedAt,
            tenant.WriteUnlockUntil));
    }

    /// <summary>
    /// Fails the way the configured behaviour says to.
    /// </summary>
    /// <remarks>
    /// All three failure modes raise the same exception, and that is the assertion rather than an
    /// implementation detail: a timeout, a 500 and a malformed body are indistinguishable to the
    /// enforcement policy, because treating any of them as "unlicensed" is how one bad vendor
    /// deployment puts every customer into read-only at once (ADR-028's fourth rule).
    /// </remarks>
    private void Refuse()
    {
        switch (Behaviour)
        {
            case ControlPlaneBehaviour.Unreachable:
                throw new ControlPlaneUnreachableException("The licence service could not be reached.");
            case ControlPlaneBehaviour.ServerError:
                throw new ControlPlaneUnreachableException("The licence service answered 500.");
            case ControlPlaneBehaviour.Garbage:
                throw new ControlPlaneUnreachableException("The licence service answered with nonsense.");
            default:
                break;
        }
    }
}
