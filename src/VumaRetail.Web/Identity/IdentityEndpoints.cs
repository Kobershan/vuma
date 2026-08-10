using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Identity.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Identity;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Identity;

/// <summary>
/// The identity endpoints: sign in, refresh, sign out, terminal activation and "what may I do".
/// </summary>
/// <remarks>
/// ADR-008 — no capability exists in a UI before it exists in the API. Stage 02 mapped these plain;
/// Stage 03 hung them on the versioned group, gave them <c>ProblemDetails</c> bodies with stable
/// codes, and routed the one command among them through the dispatcher.
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>Maps the identity endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaIdentity(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();
        RouteGroupBuilder auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/token", SignInAsync)
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Signs in with a user name and password.");

        auth.MapPost("/pin", SignInWithPinAsync)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(TerminalCertificateOptions.Scheme)
                .RequireAuthenticatedUser())
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Signs a POS operator in on an already terminal-authenticated session.");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Exchanges a refresh token for a new pair, rotating it.");

        auth.MapPost("/sign-out", SignOutAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Revokes every refresh token the caller holds.");

        auth.MapPost("/terminal/activate", ActivateTerminalAsync)
            .AllowAnonymous()
            .Produces<TerminalActivationResponse>()
            .WithSummary("Activates an enrolled terminal, pinning its certificate.");

        RouteGroupBuilder me = api.MapGroup("/me").WithTags("Authentication");

        me.MapGet("/permissions", MyPermissionsAsync)
            .RequireAuthorization()
            .Produces<PermissionsResponse>()
            .WithSummary("What the caller may do in the store they are acting in.");

        api.MapGet("/permissions", (IPermissionCatalogue catalogue) => TypedResults.Ok(
                catalogue.All.Select(descriptor => new PermissionDescriptorResponse(
                    descriptor.Key.Value,
                    descriptor.Key.Module,
                    descriptor.Description,
                    descriptor.IsHighRisk))))
            .RequirePermission(IdentityPermissions.RoleView)
            .WithTags("Authentication")
            .WithSummary("Every permission this installation understands (ADR-013).");

        return endpoints;
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> SignInAsync(
        SignInRequest request,
        AuthenticationService authentication,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .SignInWithPasswordAsync(request.UserName, request.Password, request.StoreId, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result, correlation);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> SignInWithPinAsync(
        PinSignInRequest request,
        AuthenticationService authentication,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .SignInWithPinAsync(request.TerminalId, request.Pin, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result, correlation);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request,
        AuthenticationService authentication,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
    {
        AuthenticationResult result = await authentication
            .RefreshAsync(request.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result, correlation);
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
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Guid terminalId = await dispatcher
            .SendAsync(
                new ActivateTerminalCommand(
                    request.StoreId,
                    request.EnrolmentCode,
                    request.CertificateThumbprint,
                    request.DeviceFingerprint),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new TerminalActivationResponse(terminalId));
    }

    private static async Task<Results<Ok<PermissionsResponse>, ProblemHttpResult>> MyPermissionsAsync(
        ClaimsPrincipal caller,
        IDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
    {
        if (ReadGuid(caller, "sub") is not { } userId)
        {
            return Unauthenticated(correlation, "The access token carries no subject.");
        }

        Guid? storeId = ReadGuid(caller, VumaClaims.StoreId);

        IReadOnlyCollection<string> permissions = await dispatcher
            .QueryAsync(new GetEffectivePermissionsQuery(userId, storeId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PermissionsResponse(userId, storeId, permissions));
    }

    private static Results<Ok<TokenResponse>, ProblemHttpResult> Respond(
        AuthenticationResult result,
        ICorrelationContext correlation)
    {
        // One answer for every failure. Distinguishing "no such user" from "wrong password" from
        // "locked out" turns the sign-in endpoint into an oracle for all three (docs/SECURITY.md §1),
        // and a ProblemDetails body is a tempting place to reintroduce the distinction with a helpful
        // code. AuthenticationFailure carries the reason internally; none of it is serialised.
        if (!result.Succeeded || result.AccessToken is not { } access || result.RefreshToken is not { } refresh)
        {
            return Unauthenticated(correlation, "Those credentials were not accepted.");
        }

        return TypedResults.Ok(new TokenResponse(
            access.Value,
            access.ExpiresAt,
            refresh,
            result.UserId,
            result.DisplayName ?? string.Empty));
    }

    private static ProblemHttpResult Unauthenticated(ICorrelationContext correlation, string detail)
        => TypedResults.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Not signed in",
            Detail = detail,
            Extensions =
            {
                ["code"] = ApiErrorCodes.InvalidCredentials,
                ["correlationId"] = correlation.CorrelationId,
            },
        });

    private static Guid? ReadGuid(ClaimsPrincipal user, string claimType)
        => user.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}
