using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Infrastructure.Security.Identity;

namespace ZenithRetail.Web.Identity;

/// <summary>
/// The real <see cref="IPrincipalAccessor"/>: who is acting, from the authenticated request.
/// </summary>
/// <remarks>
/// <para>
/// This is what closes the gap Stage 01 left. Until now every row's <c>created_by</c> read
/// <c>system:host</c>, which makes R6's audit trail a log of the process rather than of a person.
/// Registering this before <c>AddZenithPersistence</c> is all it takes — that registration is
/// try-add precisely so this is a registration rather than an edit to Stage 01's code.
/// </para>
/// <para>
/// Falls back to a named system principal outside a request — a background job, a migration, the
/// seeder. Not to an empty string and not to <c>"unknown"</c>: a row nobody can be held to is a hole
/// in the audit trail that looks like data.
/// </para>
/// </remarks>
/// <param name="accessor">The current request, if there is one.</param>
/// <param name="component">What to report when there is no request.</param>
public sealed class HttpContextPrincipalAccessor(IHttpContextAccessor accessor, string component = "host")
    : IPrincipalAccessor
{
    private readonly string _systemPrincipal = $"system:{component}";

    /// <inheritdoc />
    public string Principal
    {
        get
        {
            ClaimsPrincipal? user = accessor.HttpContext?.User;

            if (user?.Identity is not { IsAuthenticated: true })
            {
                return _systemPrincipal;
            }

            // A stable identifier, never a display name — display names are edited, and an audit
            // trail that changes retrospectively is not one (docs/DATA_MODEL.md §1).
            string? subject = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (subject is not null)
            {
                return $"user:{subject}";
            }

            return user.FindFirstValue(ZenithClaims.TerminalId) is { } terminal
                ? $"terminal:{terminal}"
                : _systemPrincipal;
        }
    }

    /// <inheritdoc />
    public Guid? TerminalId => ReadGuid(ZenithClaims.TerminalId);

    /// <inheritdoc />
    public bool IsSystem => accessor.HttpContext?.User.Identity is not { IsAuthenticated: true };

    private Guid? ReadGuid(string claimType)
        => accessor.HttpContext?.User.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}
