using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VumaRetail.Web.Api;

/// <summary>
/// URL-segment API versioning: every route lives under <c>/api/v{n}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled, for the same reason the dispatcher is (ADR-009, ADR-043). The alternative is
/// <c>Asp.Versioning.Http</c>, which brings media-type and query-string versioning, deprecation
/// policies and a version-aware OpenAPI provider — none of which Vuma has decided it wants, and all
/// of which would then be the shape of the API for the next thirty stages. URL-segment versioning is
/// the version scheme <c>docs/API_STANDARDS.md</c> specifies, and this is fifty lines of it.
/// </para>
/// <para>
/// Two things a caller can rely on: the segment in the path, and <see cref="VersionHeader"/> on the
/// response. The header exists so that a client behind a proxy that rewrites paths — which is every
/// store with a reverse proxy in front of the server — can still tell what answered it.
/// </para>
/// </remarks>
public static class VumaApi
{
    /// <summary>The current API version. Bumping this is a breaking-change decision, not a chore.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The response header naming the version that answered.</summary>
    public const string VersionHeader = "X-Vuma-Api-Version";

    /// <summary>
    /// Routes that legitimately sit outside <c>/api/v{n}</c>: infrastructure, not API surface.
    /// </summary>
    /// <remarks>
    /// A closed list, asserted at runtime against the endpoint table. A new un-versioned route has to
    /// be added here on purpose, which is the point — the failure mode being prevented is an endpoint
    /// that quietly becomes un-versionable once a customer integration depends on it.
    /// </remarks>
    public static readonly string[] UnversionedRoutes = ["health", "openapi"];

    /// <summary>Maps a route group for the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The group, rooted at <c>/api/v1</c>.</returns>
    public static RouteGroupBuilder MapVumaApi(this IEndpointRouteBuilder endpoints)
        => endpoints.MapVumaApi(CurrentVersion);

    /// <summary>Maps a route group for a specific API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="version">The major version.</param>
    /// <returns>The group, rooted at <c>/api/v{version}</c>.</returns>
    public static RouteGroupBuilder MapVumaApi(this IEndpointRouteBuilder endpoints, int version)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        string label = VersionLabel(version);

        RouteGroupBuilder group = endpoints.MapGroup($"/api/{label}");

        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers[VersionHeader] = label;

            return await next(context).ConfigureAwait(false);
        });

        return group;
    }

    /// <summary>Renders a version number the way it appears in a path: <c>v1</c>.</summary>
    /// <param name="version">The major version.</param>
    /// <returns>The label.</returns>
    public static string VersionLabel(int version)
        => string.Create(CultureInfo.InvariantCulture, $"v{version}");

    /// <summary>Whether a route pattern is one of the permitted un-versioned infrastructure routes.</summary>
    /// <param name="routePattern">The raw route pattern, without a leading slash.</param>
    /// <returns><c>true</c> when the route is allowed to sit outside the versioned tree.</returns>
    public static bool IsUnversionedRoute(string routePattern)
    {
        ArgumentNullException.ThrowIfNull(routePattern);

        string trimmed = routePattern.TrimStart('/');

        return Array.Exists(
            UnversionedRoutes,
            allowed => trimmed.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(allowed + "/", StringComparison.OrdinalIgnoreCase));
    }
}
