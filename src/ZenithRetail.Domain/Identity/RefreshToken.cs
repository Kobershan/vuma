using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// A refresh token that has been issued — stored as a hash, rotated on every use.
/// </summary>
/// <remarks>
/// <para>
/// Only the SHA-256 hash is kept. A refresh token is a 30-day bearer credential; a database dump
/// holding them in plaintext is a month of unrestricted access to every account in it.
/// </para>
/// <para>
/// Rotation is the whole point. Every exchange mints a new token and marks this one replaced, so a
/// token seen twice means either a replay or a theft. Both get the same answer — the caller is
/// refused and every live token in the chain is revoked (<c>docs/SECURITY.md</c> §1) — because there
/// is no way to tell the legitimate holder from the thief at that moment, and the safe assumption is
/// the expensive one.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.NodeLocal"/>: a session credential has no business leaving
/// the node that issued it. Shipping these to the cloud would put every store's live sessions in one
/// place for no operational gain.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class RefreshToken : Entity
{
    private RefreshToken(
        Guid tenantId,
        Guid? storeId,
        Guid userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
        : base(tenantId, storeId)
    {
        UserId = userId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RefreshToken()
    {
    }

    /// <summary>The user the token signs in.</summary>
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the token. The plaintext is returned to the caller once and never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>The terminal the token was issued to, for a POS session.</summary>
    public Guid? TerminalId { get; private set; }

    /// <summary>
    /// The user's security stamp when the token was issued. A password change rotates the stamp,
    /// which retires every token carrying the old one without having to find and delete them.
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>When the token expires — 30 days, per <c>CLAUDE.md</c> §4.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the token was revoked, or <c>null</c> if it is still live.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Why it was revoked — rotation, sign-out, reuse detected, or a credential change.</summary>
    public RefreshTokenRevocation? RevocationReason { get; private set; }

    /// <summary>The token that replaced this one on rotation, so a chain can be walked and killed.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Issues a refresh token.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user being signed in.</param>
    /// <param name="tokenHash">SHA-256 of the token handed to the caller.</param>
    /// <param name="securityStamp">The user's security stamp at issue time.</param>
    /// <param name="issuedAt">The current instant.</param>
    /// <param name="lifetime">How long the token lasts.</param>
    /// <param name="storeId">The store the session acts in, if it is store-scoped.</param>
    /// <param name="terminalId">The terminal, for a POS session.</param>
    public static RefreshToken Issue(
        Guid tenantId,
        Guid userId,
        string tokenHash,
        string securityStamp,
        DateTimeOffset issuedAt,
        TimeSpan lifetime,
        Guid? storeId = null,
        Guid? terminalId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A refresh token must belong to a tenant.", nameof(tenantId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A refresh token must name a user.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken(tenantId, storeId, userId, tokenHash, issuedAt, issuedAt + lifetime)
        {
            SecurityStamp = securityStamp,
            TerminalId = terminalId,
        };
    }

    /// <summary>True when the token may still be exchanged.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="currentSecurityStamp">The user's security stamp as it is now.</param>
    /// <returns>Whether the token is live, unexpired and still matches the user's stamp.</returns>
    public bool IsUsable(DateTimeOffset now, string currentSecurityStamp)
        => RevokedAt is null
            && ExpiresAt > now
            && string.Equals(SecurityStamp, currentSecurityStamp, StringComparison.Ordinal);

    /// <summary>Marks the token as exchanged for a successor.</summary>
    /// <param name="successorId">The token issued in its place.</param>
    /// <param name="now">The current instant.</param>
    public void Rotate(Guid successorId, DateTimeOffset now)
    {
        Revoke(RefreshTokenRevocation.Rotated, now);
        ReplacedByTokenId = successorId;
    }

    /// <summary>Revokes the token.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="now">The current instant.</param>
    public void Revoke(RefreshTokenRevocation reason, DateTimeOffset now)
    {
        // Already-revoked tokens keep their original reason. "Reused" must not be overwritten by the
        // sweep that follows it, or the one row explaining the incident is the row that gets rewritten.
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}

/// <summary>Why a refresh token stopped being usable.</summary>
public enum RefreshTokenRevocation
{
    /// <summary>Exchanged for a successor in the normal way.</summary>
    Rotated = 0,

    /// <summary>The user signed out.</summary>
    SignedOut = 1,

    /// <summary>Presented after it had already been exchanged — a replay or a theft.</summary>
    Reused = 2,

    /// <summary>A credential changed, or the user was deactivated.</summary>
    CredentialChanged = 3,

    /// <summary>Revoked by an administrator.</summary>
    Administrative = 4,
}
