using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>Where a terminal stands in its enrolment lifecycle.</summary>
public enum TerminalStatus
{
    /// <summary>Enrolled by a staff member, holding a one-time code, not yet activated by the device.</summary>
    Pending = 0,

    /// <summary>Activated, certificate pinned, trading.</summary>
    Active = 1,

    /// <summary>Revoked. Its certificate no longer authenticates anything.</summary>
    Revoked = 2,
}

/// <summary>
/// An enrolled till, back-office PC or self-checkout unit.
/// </summary>
/// <remarks>
/// <para>
/// Trust on first enrolment, pinned thereafter (<c>docs/SECURITY.md</c> §1). A staff member with
/// <c>identity.terminal.enrol</c> creates the terminal, which produces a one-time code with a short
/// expiry. The device generates its own key pair and self-signed client certificate, presents it once
/// with the code, and the thumbprint is bound. Afterwards only that thumbprint authenticates this
/// terminal. No certificate authority to run, and no private key ever leaves the till.
/// </para>
/// <para>
/// The device fingerprint is recorded but never enforced. ADR-026 is explicit that detection never
/// auto-disables anything: a replaced motherboard on a Saturday must not close a till, so a changed
/// fingerprint authenticates and raises a flag for a human.
/// </para>
/// <para>
/// The store is the base entity's own <c>store_id</c> and there is no foreign key to
/// <c>platform.stores</c> — that would cross a module schema boundary and fail the architecture test
/// (ADR-010). Terminals are counted for licensing, which is why they replicate
/// <see cref="ReplicationScope.StoreToCloud"/>: the store enrols them and the cloud observes them.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Terminal : Entity
{
    private Terminal(Guid tenantId, Guid storeId, string code, string name)
        : base(tenantId, storeId)
    {
        Code = code;
        Name = name;
        Status = TerminalStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Terminal()
    {
    }

    /// <summary>The short code printed on the till and on its receipts, for example <c>T01</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The terminal's name in the admin UI, for example <c>Front counter 1</c>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Where the terminal is in its lifecycle.</summary>
    public TerminalStatus Status { get; private set; }

    /// <summary>Hash of the one-time enrolment code. The plaintext is shown once and never stored.</summary>
    public string? EnrolmentCodeHash { get; private set; }

    /// <summary>When the enrolment code stops being accepted.</summary>
    public DateTimeOffset? EnrolmentCodeExpiresAt { get; private set; }

    /// <summary>The pinned SHA-256 thumbprint of the terminal's client certificate.</summary>
    public string? CertificateThumbprint { get; private set; }

    /// <summary>The device fingerprint recorded at activation. Observed, never enforced (ADR-026).</summary>
    public string? DeviceFingerprint { get; private set; }

    /// <summary>True once the fingerprint has changed since activation. A flag for a human, not a lock.</summary>
    public bool HasFingerprintDrift { get; private set; }

    /// <summary>When the terminal completed activation.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>When the terminal last authenticated.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>Why the terminal was revoked, recorded for the audit trail.</summary>
    public string? RevocationReason { get; private set; }

    /// <summary>Enrols a terminal, leaving it pending until the device activates.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the terminal trades in.</param>
    /// <param name="code">The short terminal code, unique within the store.</param>
    /// <param name="name">The terminal's display name.</param>
    /// <param name="enrolmentCodeHash">Hash of the one-time code the device will present.</param>
    /// <param name="expiresAt">When that code stops being accepted.</param>
    public static Terminal Enrol(
        Guid tenantId,
        Guid storeId,
        string code,
        string name,
        string enrolmentCodeHash,
        DateTimeOffset expiresAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A terminal must belong to a tenant.", nameof(tenantId));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A terminal must belong to a store.", nameof(storeId));
        }

        Terminal terminal = new(tenantId, storeId, Require(code, nameof(code)).ToUpperInvariant(), Require(name, nameof(name)))
        {
            EnrolmentCodeHash = Require(enrolmentCodeHash, nameof(enrolmentCodeHash)),
            EnrolmentCodeExpiresAt = expiresAt,
        };

        return terminal;
    }

    /// <summary>
    /// Completes activation: pins the certificate thumbprint and spends the enrolment code.
    /// </summary>
    /// <param name="certificateThumbprint">SHA-256 thumbprint of the device's client certificate.</param>
    /// <param name="deviceFingerprint">The device fingerprint to record.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="TerminalActivationException">The terminal is not pending, or the code has expired.</exception>
    public void Activate(string certificateThumbprint, string deviceFingerprint, DateTimeOffset now)
    {
        if (Status != TerminalStatus.Pending)
        {
            throw new TerminalActivationException($"Terminal {Code} is {Status}, not pending activation.");
        }

        if (EnrolmentCodeExpiresAt is null || EnrolmentCodeExpiresAt <= now)
        {
            throw new TerminalActivationException($"The enrolment code for terminal {Code} has expired.");
        }

        CertificateThumbprint = Require(certificateThumbprint, nameof(certificateThumbprint)).ToUpperInvariant();
        DeviceFingerprint = Require(deviceFingerprint, nameof(deviceFingerprint));
        Status = TerminalStatus.Active;
        ActivatedAt = now;
        LastSeenAt = now;

        // The code is spent, not merely expired. A code that is still comparable after use is a
        // second credential for a terminal that already has a certificate.
        EnrolmentCodeHash = null;
        EnrolmentCodeExpiresAt = null;
    }

    /// <summary>Records a successful authentication, and whether the device looks like the same box.</summary>
    /// <param name="deviceFingerprint">The fingerprint presented, or <c>null</c> if the caller sent none.</param>
    /// <param name="now">The current instant.</param>
    public void RecordAuthentication(string? deviceFingerprint, DateTimeOffset now)
    {
        LastSeenAt = now;

        if (deviceFingerprint is not null
            && DeviceFingerprint is not null
            && !string.Equals(deviceFingerprint, DeviceFingerprint, StringComparison.Ordinal))
        {
            // Flagged, not refused. ADR-026: a false positive that closes a till on a Saturday costs
            // more than the piracy it would have caught.
            HasFingerprintDrift = true;
        }
    }

    /// <summary>Revokes the terminal. Its certificate stops authenticating immediately.</summary>
    /// <param name="reason">Why, for the audit trail.</param>
    public void Revoke(string reason)
    {
        Status = TerminalStatus.Revoked;
        RevocationReason = Require(reason, nameof(reason));
        EnrolmentCodeHash = null;
        EnrolmentCodeExpiresAt = null;
    }

    /// <summary>True when this terminal may authenticate and trade.</summary>
    public bool CanAuthenticate => Status == TerminalStatus.Active && CertificateThumbprint is not null;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>Thrown when a terminal cannot be activated in its current state.</summary>
/// <param name="message">What went wrong.</param>
public sealed class TerminalActivationException(string message)
    : DomainException("TERMINAL_ACTIVATION_REFUSED", message);
