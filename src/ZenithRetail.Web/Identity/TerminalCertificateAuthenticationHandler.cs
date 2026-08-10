using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZenithRetail.Application.Identity;
using ZenithRetail.Domain.Identity;
using ZenithRetail.Infrastructure.Security.Identity;

// ASP.NET Core has its own AuthenticationService in the namespace this file already needs for
// AuthenticationHandler. Aliased rather than renaming ours, which is named for what it does.
using AuthenticationService = ZenithRetail.Application.Identity.AuthenticationService;

namespace ZenithRetail.Web.Identity;

/// <summary>Options for the terminal certificate scheme.</summary>
public sealed class TerminalCertificateOptions : AuthenticationSchemeOptions
{
    /// <summary>The scheme name terminals authenticate under.</summary>
    public const string Scheme = "ZenithTerminalCertificate";
}

/// <summary>
/// Authenticates a till by the client certificate it presented on the connection.
/// </summary>
/// <remarks>
/// <para>
/// Trust on first enrolment, pinned thereafter (<c>docs/SECURITY.md</c> §1). The certificate is not
/// validated against a chain, because there is no certificate authority in this design and a
/// self-signed certificate is exactly what a till presents. What is checked is that its SHA-256
/// thumbprint is the one bound to an active terminal — which is a stronger statement than chain
/// validation, since it names one device rather than one issuer.
/// </para>
/// <para>
/// Which Kestrel port demands a client certificate, and how one survives a reverse proxy, is
/// deployment configuration owned by Stage 31's installer. This handler only needs the certificate
/// to have reached <c>HttpContext.Connection</c>.
/// </para>
/// </remarks>
/// <param name="options">Scheme options.</param>
/// <param name="logger">Logger factory.</param>
/// <param name="encoder">URL encoder.</param>
/// <param name="authentication">Resolves a thumbprint into a terminal.</param>
public sealed class TerminalCertificateAuthenticationHandler(
    IOptionsMonitor<TerminalCertificateOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthenticationService authentication) : AuthenticationHandler<TerminalCertificateOptions>(options, logger, encoder)
{
    /// <summary>The header a terminal reports its device fingerprint in.</summary>
    public const string FingerprintHeader = "X-Zenith-Device-Fingerprint";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        X509Certificate2? certificate = await Context.Connection.GetClientCertificateAsync().ConfigureAwait(false);

        if (certificate is null)
        {
            return AuthenticateResult.NoResult();
        }

        // GetCertHashString(SHA256) rather than the Thumbprint property: Thumbprint is SHA-1, which
        // has practical collisions, and pinning is the whole security story here.
        string thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);

        TerminalAuthenticationResult result = await authentication
            .AuthenticateTerminalAsync(
                thumbprint,
                Context.Request.Headers[FingerprintHeader].FirstOrDefault(),
                Context.RequestAborted)
            .ConfigureAwait(false);

        if (!result.Succeeded || result.Terminal is not { } terminal)
        {
            return AuthenticateResult.Fail("No active terminal is bound to that certificate.");
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, terminal.Id.ToString()),
                new Claim(ZenithClaims.TerminalId, terminal.Id.ToString()),
                new Claim(ZenithClaims.TenantId, terminal.TenantId.ToString()),
                .. Store(terminal),
            ],
            TerminalCertificateOptions.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), TerminalCertificateOptions.Scheme));
    }

    private static IEnumerable<Claim> Store(Terminal terminal)
        => terminal.StoreId is { } storeId ? [new Claim(ZenithClaims.StoreId, storeId.ToString())] : [];
}
