using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// A person who signs in — a manager in the back office, a cashier at a till, or both.
/// </summary>
/// <remarks>
/// <para>
/// Two credentials, deliberately independent. The password is for the back office and the API; the
/// 4–8 digit PIN identifies an operator at a till that has <em>already</em> authenticated itself with
/// its certificate (<c>docs/SECURITY.md</c> §1). They lock out separately, so a shift of mistyped
/// PINs at the till cannot lock a manager out of the reports, and a password-guessing attempt cannot
/// stop the till trading — which would breach R1 through the back door.
/// </para>
/// <para>
/// The entity holds hashes and never a secret. Hashing is Infrastructure's (ASP.NET Core Identity's
/// <c>PasswordHasher</c>); which algorithm was used is not a business rule and the domain does not
/// know. What the domain owns is the counting, the locking and the security stamp.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.Bidirectional"/> with <see cref="ConflictPolicy.CloudWins"/>:
/// head office creates staff, but a store manager legitimately resets a cashier's PIN on a Saturday
/// with the line down. When both edited, head office is where an estate is reconciled.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class User : Entity
{
    private User(Guid tenantId, string userName, string displayName)
        : base(tenantId)
    {
        UserName = userName;
        NormalizedUserName = Normalize(userName);
        DisplayName = displayName;
        SecurityStamp = UuidV7.NewGuid().ToString("N");
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private User()
    {
    }

    /// <summary>What the user types to sign in, in the case they chose.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>The upper-cased form the unique index and every lookup use.</summary>
    public string NormalizedUserName { get; private set; } = string.Empty;

    /// <summary>The name shown in the UI and on a shift report. Never used as an identifier.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The user's email address, where they have one. Optional — a cashier may not.</summary>
    public string? Email { get; private set; }

    /// <summary>The upper-cased email, for lookup.</summary>
    public string? NormalizedEmail { get; private set; }

    /// <summary>The password hash, or <c>null</c> for a POS-only operator who has no back-office login.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Changes whenever a credential changes. Every token issued before the change stops working,
    /// which is how a password reset actually ends the sessions it is meant to end.
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    /// <summary>The POS PIN hash, or <c>null</c> if this user does not work a till.</summary>
    public string? PinHash { get; private set; }

    /// <summary>Consecutive failed password attempts since the last success.</summary>
    public int FailedPasswordAttempts { get; private set; }

    /// <summary>When the password login unlocks, or <c>null</c> if it is not locked.</summary>
    public DateTimeOffset? PasswordLockedUntil { get; private set; }

    /// <summary>Consecutive failed PIN attempts since the last success.</summary>
    public int FailedPinAttempts { get; private set; }

    /// <summary>When the PIN unlocks, or <c>null</c> if it is not locked.</summary>
    public DateTimeOffset? PinLockedUntil { get; private set; }

    /// <summary>When the user last signed in successfully, by any credential.</summary>
    public DateTimeOffset? LastSignedInAt { get; private set; }

    /// <summary>False once the user has left. Access ends; their history stays (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a user with no credentials yet.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userName">The sign-in name, unique within the tenant.</param>
    /// <param name="displayName">The name shown in the UI.</param>
    /// <returns>An active user who cannot yet sign in, because nothing has been set.</returns>
    public static User Create(Guid tenantId, string userName, string displayName)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A user must belong to a tenant.", nameof(tenantId));
        }

        return new User(tenantId, Require(userName, nameof(userName)), Require(displayName, nameof(displayName)));
    }

    /// <summary>Sets the email address.</summary>
    /// <param name="email">The address, or <c>null</c> to clear it.</param>
    public void SetEmail(string? email)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        NormalizedEmail = Email is null ? null : Normalize(Email);
    }

    /// <summary>Renames the user for display. Their sign-in name and identity do not change.</summary>
    /// <param name="displayName">The new display name.</param>
    public void SetDisplayName(string displayName) => DisplayName = Require(displayName, nameof(displayName));

    /// <summary>
    /// Replaces the password hash and rotates the security stamp, ending every existing session.
    /// </summary>
    /// <param name="passwordHash">The new hash, computed by Infrastructure.</param>
    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = Require(passwordHash, nameof(passwordHash));
        FailedPasswordAttempts = 0;
        PasswordLockedUntil = null;
        RotateSecurityStamp();
    }

    /// <summary>Replaces the POS PIN hash and clears any PIN lockout.</summary>
    /// <param name="pinHash">The new hash, computed by Infrastructure.</param>
    /// <remarks>
    /// Deliberately does <b>not</b> rotate the security stamp. A manager resetting a cashier's PIN
    /// mid-shift must not sign that cashier out of the sale they are ringing up (R1).
    /// </remarks>
    public void SetPinHash(string pinHash)
    {
        PinHash = Require(pinHash, nameof(pinHash));
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    /// <summary>Removes the user's POS PIN, so they can no longer sign in at a till.</summary>
    public void ClearPin()
    {
        PinHash = null;
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    /// <summary>True while the password login is locked out.</summary>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public bool IsPasswordLockedOut(DateTimeOffset now) => PasswordLockedUntil > now;

    /// <summary>True while the PIN login is locked out.</summary>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public bool IsPinLockedOut(DateTimeOffset now) => PinLockedUntil > now;

    /// <summary>Records a failed password attempt, locking the account once the policy is exceeded.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="policy">Attempts allowed and how long the lock lasts.</param>
    public void RecordFailedPasswordAttempt(DateTimeOffset now, CredentialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        FailedPasswordAttempts++;

        if (FailedPasswordAttempts >= policy.MaxFailedAttempts)
        {
            PasswordLockedUntil = now + policy.LockoutDuration;
        }
    }

    /// <summary>Records a failed PIN attempt, locking the PIN once the policy is exceeded.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="policy">Attempts allowed and how long the lock lasts.</param>
    public void RecordFailedPinAttempt(DateTimeOffset now, CredentialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        FailedPinAttempts++;

        if (FailedPinAttempts >= policy.MaxFailedAttempts)
        {
            PinLockedUntil = now + policy.LockoutDuration;
        }
    }

    /// <summary>Records a successful sign-in, clearing the counters for the credential used.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="credential">Which credential was presented.</param>
    public void RecordSuccessfulSignIn(DateTimeOffset now, CredentialKind credential)
    {
        LastSignedInAt = now;

        // Only the credential that succeeded is forgiven. A correct PIN at the till says nothing
        // about whoever is guessing at the password on the back-office login.
        if (credential == CredentialKind.Password)
        {
            FailedPasswordAttempts = 0;
            PasswordLockedUntil = null;
        }
        else
        {
            FailedPinAttempts = 0;
            PinLockedUntil = null;
        }
    }

    /// <summary>Reactivates a user who had left.</summary>
    public void Activate() => IsActive = true;

    /// <summary>
    /// Ends the user's access. Their rows, their sales and their audit trail all stay (§7 rule 8).
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        RotateSecurityStamp();
    }

    /// <summary>Invalidates every token issued so far for this user.</summary>
    public void RotateSecurityStamp() => SecurityStamp = UuidV7.NewGuid().ToString("N");

    /// <summary>Upper-cases for lookup. Invariant, because a user name is not prose.</summary>
    /// <param name="value">The value to normalise.</param>
    /// <returns>The upper-cased form.</returns>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Trim().ToUpperInvariant();
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>Which credential a sign-in used.</summary>
public enum CredentialKind
{
    /// <summary>A password, from the back office or the API.</summary>
    Password = 0,

    /// <summary>A POS PIN, on an already terminal-authenticated session.</summary>
    Pin = 1,
}
