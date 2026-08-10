using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Identity.Queries;
using VumaRetail.Contracts.Identity;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.Web.Identity;

/// <summary>
/// The identity endpoints: sign in, refresh, sign out, terminal activation and "what may I do".
/// </summary>
/// <remarks>
/// ADR-008 — no capability exists in a UI before it exists in the API, so these ship in Stage 02
/// rather than waiting for a login screen. Versioning, <c>ProblemDetails</c> shaping and OpenAPI
/// examples are Stage 03's deliverable; these are deliberately plain until then.
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>Maps the identity endpoints under <c>/api/v1</c>.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaIdentity(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder auth = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");

        auth.MapPost("/token", SignInAsync)
            .AllowAnonymous()
            .WithSummary("Signs in with a user name and password.");

        auth.MapPost("/pin", SignInWithPinAsync)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(TerminalCertificateOptions.Scheme)
                .RequireAuthenticatedUser())
            .WithSummary("Signs a POS operator in on an already terminal-authenticated session.");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithSummary("Exchanges a refresh token for a new pair, rotating it.");

        auth.MapPost("/sign-out", SignOutAsync)
            .RequireAuthorization()
            .WithSummary("Revokes every refresh token the caller holds.");

        auth.MapPost("/terminal/activate", ActivateTerminalAsync)
            .AllowAnonymous()
            .WithSummary("Activates an enrolled terminal, pinning its certificate.");

        endpoints.MapGet("/api/v1/me/permissions", MyPermissionsAsync)
            .RequireAuthorization()
            .WithTags("Authentication")
            .WithSummary("What the caller may do in the store they are acting in.");

        endpoints.MapGet("/api/v1/permissions", (IPermissionCatalogue catalogue) => Results.Ok(
                catalogue.All.Select(descriptor => new
                {
                    permission = descriptor.Key.Value,
                    module = descriptor.Key.Module,
                    descriptor.Description,
                    descriptor.IsHighRisk,
                })))
            .RequirePermission(IdentityPermissions.RoleView)
            .WithTags("Authentication")
            .WithSummary("Every permission this installation understands (ADR-013).");

        return endpoints;
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> SignInAsync(
        SignInRequest request,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .SignInWithPasswordAsync(request.UserName, request.Password, request.StoreId, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> SignInWithPinAsync(
        PinSignInRequest request,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .SignInWithPinAsync(request.TerminalId, request.Pin, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .RefreshAsync(request.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result);
    }

    private static async Task<NoContent> SignOutAsync(
        ClaimsPrincipal caller,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        if (ReadGuid(caller, "sub") is { } userId)
        {
            await authentication.SignOutAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<TerminalActivationResponse>> ActivateTerminalAsync(
        ActivateTerminalRequest request,
        ICommandHandler<ActivateTerminalCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        Guid terminalId = await handler
            .HandleAsync(
                new ActivateTerminalCommand(
                    request.StoreId,
                    request.EnrolmentCode,
                    request.CertificateThumbprint,
                    request.DeviceFingerprint),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new TerminalActivationResponse(terminalId));
    }

    private static async Task<Results<Ok<PermissionsResponse>, UnauthorizedHttpResult>> MyPermissionsAsync(
        ClaimsPrincipal caller,
        IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>> handler,
        CancellationToken cancellationToken)
    {
        if (ReadGuid(caller, "sub") is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        Guid? storeId = ReadGuid(caller, VumaClaims.StoreId);

        IReadOnlyCollection<string> permissions = await handler
            .HandleAsync(new GetEffectivePermissionsQuery(userId, storeId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PermissionsResponse(userId, storeId, permissions));
    }

    private static Results<Ok<TokenResponse>, UnauthorizedHttpResult> Respond(AuthenticationResult result)
    {
        // One answer for every failure. Distinguishing "no such user" from "wrong password" from
        // "locked out" turns the sign-in endpoint into an oracle for all three
        // (docs/SECURITY.md §1). Stage 03 gives this a ProblemDetails body with a stable code.
        if (!result.Succeeded || result.AccessToken is not { } access || result.RefreshToken is not { } refresh)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new TokenResponse(
            access.Value,
            access.ExpiresAt,
            refresh,
            result.UserId,
            result.DisplayName ?? string.Empty));
    }

    private static Guid? ReadGuid(ClaimsPrincipal user, string claimType)
        => user.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}
