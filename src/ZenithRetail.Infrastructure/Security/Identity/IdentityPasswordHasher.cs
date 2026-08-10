using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ZenithRetail.Application.Identity;
using ZenithRetail.Domain.Identity;

namespace ZenithRetail.Infrastructure.Security.Identity;

/// <summary>
/// Hashes passwords, POS PINs and terminal enrolment codes with ASP.NET Core Identity's hasher.
/// </summary>
/// <remarks>
/// <para>
/// PBKDF2-HMAC-SHA512, 100 000 iterations, a 128-bit per-secret salt, and a version byte in front of
/// the output so the parameters can be raised later and existing hashes rehashed on next sign-in.
/// This is the <c>CLAUDE.md</c> §4 "ASP.NET Core Identity" component that Zenith actually uses — see
/// ADR-038 for why <c>UserManager</c> and the EF stores are not.
/// </para>
/// <para>
/// One hasher for all three secret kinds on purpose. A PIN is weaker than a password and an enrolment
/// code is stronger than both, but the property each needs from storage is identical: salted, slow,
/// and constant-time to verify. A second, faster path for "just the PIN" is how the weakest secret in
/// the system ends up with the weakest storage.
/// </para>
/// </remarks>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <inheritdoc />
    public string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        // The hasher's user argument is only used by the v2 compatibility path, which is off. Passing
        // a throwaway keeps the domain entity out of a hashing call it has no business being in.
        return _hasher.HashPassword(Placeholder, secret);
    }

    /// <inheritdoc />
    public bool Verify(string hash, string secret)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        PasswordVerificationResult result = _hasher.VerifyHashedPassword(Placeholder, hash, secret);

        // SuccessRehashNeeded means the stored hash used older parameters. It is still a match; the
        // rehash is a separate concern the sign-in path handles when the parameters are raised.
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static User Placeholder { get; } = User.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"), "hasher", "hasher");
}

/// <summary>
/// Generates refresh tokens and reduces them to the digest stored against a session.
/// </summary>
/// <remarks>
/// SHA-256, unsalted and deterministic, which would be wrong for a password and is right here. The
/// token is 256 bits this system generated rather than something a person chose, so it has nothing to
/// guess and no dictionary to attack — and the refresh path has to <em>find</em> the row by digest,
/// which a per-row salt makes impossible without scanning every live token on every refresh.
/// </remarks>
public sealed class Sha256TokenHasher : ITokenHasher
{
    /// <summary>Bytes of entropy in a generated token. 256 bits, matching the digest.</summary>
    private const int TokenBytes = 32;

    /// <inheritdoc />
    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }

    /// <inheritdoc />
    public string Generate() => Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// URL-safe base64 without padding, so a token survives a query string, a header and a mobile
    /// client's keychain without anybody having to remember to escape it.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
