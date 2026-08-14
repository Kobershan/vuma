using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.Web.Identity;

/// <summary>
/// Sets <see cref="ITenantContext"/> at the request edge, so every query below is tenant-filtered.
/// </summary>
/// <remarks>
/// <para>
/// Two sources, in order. An authenticated request carries its tenant in the access token, which is
/// the only source that can be trusted — a header or a route value would let a caller pick a tenant.
/// An <em>unauthenticated</em> request on a store server falls back to the tenant that server was
/// activated for, because the sign-in endpoint has to look a user up before there is a token to read
/// a tenant from, and an in-store server serves exactly one tenant.
/// </para>
/// <para>
/// There is no fallback in the cloud, on purpose. An unresolved tenant matches nothing rather than
/// everything (<c>docs/DATA_MODEL.md</c> §1), so the failure mode of forgetting to configure one is
/// an empty result set, not somebody else's trading data.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="logger">Records a request whose tenant could not be resolved.</param>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request.</param>
    /// <param name="tenantContext">The ambient tenant to set.</param>
    /// <param name="options">The host's tenant configuration.</param>
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, HostTenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);

        Guid tenantId = ReadClaim(context.User, VumaClaims.TenantId) ?? options.TenantId;
        Guid? storeId = ReadClaim(context.User, VumaClaims.StoreId) ?? options.StoreId;

        if (tenantId == Guid.Empty)
        {
            // Warning, not an error response. Some endpoints legitimately run before a tenant exists —
            // activation, health checks — and the query filter already makes an unresolved tenant safe.
            logger.LogWarning("No tenant resolved for {Path}; queries in this request will match nothing.", context.Request.Path);
        }

        tenantContext.SetTenant(tenantId, storeId);

        await next(context).ConfigureAwait(false);
    }

    private static Guid? ReadClaim(ClaimsPrincipal? user, string claimType)
        => user?.Identity is { IsAuthenticated: true }
            && user.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}

/// <summary>
/// The tenant and store a host serves when a request carries none.
/// </summary>
/// <remarks>
/// Set on a store server from its activation record; left empty in the cloud, where every request
/// must carry its own tenant. Stage 04b replaces the configuration value with the activated licence's
/// tenant, which is why this is a small options object rather than a raw configuration read.
/// </remarks>
public sealed class HostTenantOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Vuma:Host";

    /// <summary>The tenant this host serves, or <see cref="Guid.Empty"/> in the cloud.</summary>
    public Guid TenantId { get; set; }

    /// <summary>The store this host serves, where it serves one.</summary>
    public Guid? StoreId { get; set; }
}
