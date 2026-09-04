using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;

namespace VumaRetail.Infrastructure.Security.Identity;

/// <summary>
/// The claim names Vuma puts in an access token, in one place so the issuer and the reader cannot
/// drift apart.
/// </summary>
public static class VumaClaims
{
    /// <summary>The tenant the session belongs to. Sets <c>ITenantContext</c> at the request edge.</summary>
    public const string TenantId = "vuma:tenant";

    /// <summary>The store being acted in, for a store-scoped session.</summary>
    public const string StoreId = "vuma:store";

    /// <summary>The terminal, for a POS session. Becomes the <c>terminal_id</c> on the audit entry.</summary>
    public const string TerminalId = "vuma:terminal";

    /// <summary>The user's security stamp, so a credential change retires the token.</summary>
    public const string SecurityStamp = "vuma:stamp";

    /// <summary>The vendor-issued Operator ID, when the login is in the registry directory (ADR-127).</summary>
    public const string OperatorId = "vuma:operator";

    /// <summary>
    /// The companies the login may act in, as compact JSON (<c>[{"c":"guid","r":"roles"}]</c>).
    /// A request naming a company absent from this claim is 403 (ADR-127).
    /// </summary>
    public const string Companies = "vuma:companies";
}

/// <summary>How access and refresh tokens are signed and how long they live.</summary>
/// <remarks>
/// The lifetimes are <c>CLAUDE.md</c> §4's and are configuration rather than constants so a tenant
/// with a stricter policy can shorten them. Lengthening the access token past the default is what a
/// review should question: 15 minutes is what bounds the damage of a leaked bearer token.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>The configuration section these bind from.</summary>
    public const string SectionName = "Vuma:Jwt";

    /// <summary>
    /// The value shipped in <c>appsettings.json</c>, which the host refuses to start on outside
    /// Development. A shipped default signing key is a master key for every installation that used it.
    /// </summary>
    public const string DevelopmentPlaceholderKey = "development-only-signing-key-replace-me-0000000000";

    /// <summary>Who issued the token.</summary>
    public string Issuer { get; set; } = "vuma-store-server";

    /// <summary>Who the token is for.</summary>
    public string Audience { get; set; } = "vuma";

    /// <summary>The HMAC-SHA256 signing key. At least 32 bytes.</summary>
    public string SigningKey { get; set; } = DevelopmentPlaceholderKey;

    /// <summary>How long an access token lasts. 15 minutes (<c>CLAUDE.md</c> §4).</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a refresh token lasts. 30 days, rotating (<c>CLAUDE.md</c> §4).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>True when the signing key is still the shipped placeholder.</summary>
    public bool UsesPlaceholderKey => string.Equals(SigningKey, DevelopmentPlaceholderKey, StringComparison.Ordinal);
}

/// <summary>Mints signed JWT access tokens.</summary>
/// <remarks>
/// <para>
/// The token carries identity and nothing else. Permissions are resolved per request from the
/// catalogue, because a permission list baked into a 15-minute token is a 15-minute window in which a
/// permission you just revoked still works — and a token that grows with every module shipped.
/// </para>
/// <para>
/// Signed with HMAC-SHA256 from a shared key. Store server and cloud validate each other's tokens
/// through their own configured key; asymmetric signing becomes worth its key-distribution cost when
/// a third party has to validate a Vuma token, which is Stage 20's problem and not this one's.
/// </para>
/// </remarks>
/// <param name="options">Issuer, audience, key and lifetimes.</param>
/// <param name="clock">The only source of time — so a token's expiry is testable.</param>
public sealed class JwtTokenIssuer(JwtOptions options, IClock clock) : ITokenIssuer
{
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    /// <inheritdoc />
    public TimeSpan RefreshTokenLifetime => options.RefreshTokenLifetime;

    /// <inheritdoc />
    public AccessToken Issue(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        DateTimeOffset issuedAt = clock.UtcNow;
        DateTimeOffset expiresAt = issuedAt + options.AccessTokenLifetime;

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Domain.Primitives.UuidV7.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, subject.DisplayName),
            new(VumaClaims.TenantId, subject.TenantId.ToString()),
            new(VumaClaims.SecurityStamp, subject.SecurityStamp),
        ];

        if (subject.StoreId is { } storeId)
        {
            claims.Add(new Claim(VumaClaims.StoreId, storeId.ToString()));
        }

        if (subject.TerminalId is { } terminalId)
        {
            claims.Add(new Claim(VumaClaims.TerminalId, terminalId.ToString()));
        }

        if (subject.OperatorId is { } operatorId && operatorId != Guid.Empty)
        {
            claims.Add(new Claim(VumaClaims.OperatorId, operatorId.ToString()));
        }

        if (subject.Companies is { Count: > 0 } companies)
        {
            claims.Add(new Claim(VumaClaims.Companies, SerialiseCompanies(companies)));
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    private static string SerialiseCompanies(IReadOnlyList<CompanyMembership> companies)
        => VumaCompanyClaims.Serialise(companies);
}

/// <summary>
/// Reads and writes the <c>vuma:companies</c> claim in one place so the issuer, the request
/// edge and the company-selection guard cannot drift apart (ADR-127).
/// </summary>
public static class VumaCompanyClaims
{
    /// <summary>Serialises company memberships to the compact claim form.</summary>
    public static string Serialise(IReadOnlyList<CompanyMembership> companies)
    {
        ArgumentNullException.ThrowIfNull(companies);
        return JsonSerializer.Serialize(companies.Select(m => new Entry(m.CompanyId, m.Roles)).ToArray());
    }

    /// <summary>
    /// Parses the claim back. Returns <c>null</c> when the token carries no companies claim at
    /// all (a pre-registry login or a system principal) — which is not the same as an empty set.
    /// </summary>
    public static IReadOnlyList<CompanyMembership>? Parse(ClaimsPrincipal? user)
    {
        string? raw = user?.FindFirst(VumaClaims.Companies)?.Value;

        if (raw is null)
        {
            return null;
        }

        try
        {
            Entry[] entries = JsonSerializer.Deserialize<Entry[]>(raw) ?? [];
            return entries.Select(e => new CompanyMembership(e.C, e.R)).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The access token carries an unreadable companies claim.", exception);
        }
    }

    private sealed record Entry(Guid C, string R);

    /// <summary>
    /// Refuses a company absent from the token's company set (ADR-127: 403, not 404, and not a
    /// filtered empty result). A token carrying no companies claim at all — pre-registry login,
    /// system principal — is unrestricted: the claim's absence predates the rule.
    /// </summary>
    public static void RequireCompany(System.Security.Claims.ClaimsPrincipal? user, Guid companyId)
    {
        IReadOnlyList<CompanyMembership>? companies = Parse(user);

        if (companies is null || companies.Count == 0)
        {
            return;
        }

        if (!companies.Any(m => m.CompanyId == companyId))
        {
            throw new Domain.Registry.ForbiddenException(companyId);
        }
    }
}
