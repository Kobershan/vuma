namespace ZenithRetail.Domain.Identity;

/// <summary>
/// How many failures a credential tolerates and how long it locks for afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Passed into <see cref="User"/> rather than hard-coded on it, because a shop with a PIN pad in a
/// busy queue and a head office with remote back-office access want different numbers, and
/// <c>CLAUDE.md</c> §9 is clear that this class of setting is configuration rather than code.
/// </para>
/// <para>
/// The defaults are the ones <c>docs/SECURITY.md</c> §1 documents: five attempts, fifteen minutes.
/// Five is low enough to make a 10 000-combination PIN useless to guess and high enough that a
/// cashier who fat-fingers twice on a Saturday is not locked out.
/// </para>
/// </remarks>
/// <param name="MaxFailedAttempts">Failures allowed before the credential locks.</param>
/// <param name="LockoutDuration">How long the lock lasts.</param>
public sealed record CredentialPolicy(int MaxFailedAttempts, TimeSpan LockoutDuration)
{
    /// <summary>The documented default: five attempts, then fifteen minutes.</summary>
    /// <remarks>
    /// A reference type rather than a struct so a host can register one instance in the container and
    /// every consumer sees the tenant's configured numbers. A nullable struct cannot be registered at
    /// all, and a default-constructed one would silently mean "zero attempts allowed".
    /// </remarks>
    public static CredentialPolicy Default { get; } = new(5, TimeSpan.FromMinutes(15));

    /// <summary>The shortest a PIN may be (<c>CLAUDE.md</c> §4).</summary>
    public const int MinimumPinLength = 4;

    /// <summary>The longest a PIN may be (<c>CLAUDE.md</c> §4).</summary>
    public const int MaximumPinLength = 8;

    /// <summary>
    /// The shortest a password may be. Length only — composition rules and forced expiry both push
    /// people towards <c>Password1!</c> and a note on the monitor (<c>docs/SECURITY.md</c> §1).
    /// </summary>
    public const int MinimumPasswordLength = 12;
}
