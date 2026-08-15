using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Contracts.Licensing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Permissions;
using VumaRetail.Licensing.Queries;
using VumaRetail.Web.Api;
using VumaRetail.Web.Identity;

namespace VumaRetail.Web.Licensing;

/// <summary>
/// The licence screen, the activation wizard and the support-access consent flow, as API.
/// </summary>
/// <remarks>
/// <para>
/// R3: no feature exists in the desktop UI that is not reachable through the versioned REST API. The
/// stage brief describes screens — an activation wizard, a licence status screen with a "retry now"
/// button, a support-access approval — and the WPF shell that renders them is Stage 09's. What is here
/// is everything those screens will call, which is the half that can be built and tested today.
/// </para>
/// <para>
/// <b>Every route on this group works while the tenant is read-only.</b> The reads are queries, which
/// the guard never touches, and the writes carry the ADR-028 payment carve-out. A licence screen that
/// stopped working during a lapse would be a licence screen nobody could use to end one.
/// </para>
/// </remarks>
public static class LicensingEndpoints
{
    /// <summary>Maps the licensing endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaLicensing(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder licence = endpoints.MapVumaApi().MapGroup("/licence").WithTags("Licensing");

        licence.MapGet("/", (IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new GetLicenceStatusQuery(), cancellationToken))
            .RequirePermission(LicensingPermissions.LicenceView)
            .Produces<LicenceStatusResponse>()
            .WithSummary("The licence screen: plan, entitlements, limits against usage, and what is owed.")
            .WithDescription(
                "Works in every enforcement state, including read-only — this is the screen a "
                + "restricted tenant uses to end the restriction.");

        licence.MapPost("/activations", ActivateAsync)
            .RequirePermission(LicensingPermissions.LicenceActivate)
            .Produces<ActivationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Binds this installation to a licence key and this machine.")
            .WithDescription(
                "The key's checksum is verified locally first, so a typo fails before any network "
                + "call (docs/LICENSING.md §2).");

        licence.MapPost("/activations/rebind", RebindAsync)
            .RequirePermission(LicensingPermissions.LicenceActivate)
            .Produces<ActivationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Moves the binding to different hardware.")
            .WithDescription(
                "A DisasterRecovery rebind is auto-approved without exception: a rebuilt store must "
                + "never meet an activation error (R4, docs/LICENSING.md §3).");

        licence.MapPost("/refresh", RefreshAsync)
            .RequirePermission(LicensingPermissions.LicenceRefresh)
            .Produces<LeaseRefreshResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Fetches a lease now — the licence screen's \"retry now\".")
            .WithDescription(
                "An unreachable licence service answers 200 with reached=false and changes nothing. "
                + "A vendor-side outage never restricts anyone (ADR-028).");

        licence.MapPost("/emergency-code", RedeemAsync)
            .RequirePermission(LicensingPermissions.LicenceRedeem)
            .Produces<EmergencyCodeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Redeems a vendor emergency access code.")
            .WithDescription(
                "Verified against the pinned public key, so it works with no internet at all. "
                + "Single-use, single-tenant and expiring on its own signed schedule.");

        licence.MapGet("/sub-lease", SubLeaseAsync)
            .RequireAuthorization()
            .Produces<SubLeaseResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("The sub-lease a till runs on, issued over the LAN.")
            .WithDescription(
                "A terminal never needs internet of its own: it asks its store server on an "
                + "already certificate-authenticated connection and gets the store's entitlements, "
                + "capped by the store's own lease (docs/LICENSING.md §2).");

        licence.MapGet("/metering", (IDispatcher dispatcher, int? limit, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListMeteringQuery(limit ?? 30), cancellationToken))
            .RequirePermission(LicensingPermissions.MeteringView)
            .Produces<IReadOnlyList<MeteringRecordResponse>>()
            .WithSummary("Exactly what has been reported to the vendor, verbatim.")
            .WithDescription(
                "Counts and health only (R10). The payload is returned as sent, so the privacy "
                + "claim in docs/LICENSING.md §9 can be checked rather than believed.");

        RouteGroupBuilder support = licence.MapGroup("/support-access");

        support.MapGet("/", (IDispatcher dispatcher, int? limit, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListSupportGrantsQuery(limit ?? 50), cancellationToken))
            .RequirePermission(LicensingPermissions.SupportView)
            .Produces<IReadOnlyList<SupportGrantResponse>>()
            .WithSummary("Who at Vuma has asked to see this business's data, and when.")
            .WithDescription("An active grant is what drives the banner in the tenant's own UI.");

        support.MapPost("/{grantId:guid}/approve", ApproveAsync)
            .RequirePermission(LicensingPermissions.SupportDecide)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Lets Vuma support see this business's data, for a bounded time.");

        support.MapPost("/{grantId:guid}/decline", DeclineAsync)
            .RequirePermission(LicensingPermissions.SupportDecide)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Refuses a vendor support request.");

        support.MapPost("/{grantId:guid}/revoke", RevokeAsync)
            .RequirePermission(LicensingPermissions.SupportDecide)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Ends a vendor's access immediately.")
            .WithDescription(
                "Works even while the tenant is read-only. Withdrawal of consent is not a business "
                + "write and must not be held hostage by billing (ADR-052).");

        return endpoints;
    }

    /// <summary>
    /// Requires the tenant's plan to include a module (<c>LICENSING.md</c> §6).
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="module">The module, from its manifest.</param>
    /// <returns>The endpoint, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The single gate every module stage from 06 uses. It goes through
    /// <see cref="IEntitlementService"/> and nothing else — an architecture test fails the build on a
    /// module that reads a licence or an enforcement level for itself, because a second implementation
    /// of "are we allowed to" is a second set of edge cases and only one of them gets fixed.
    /// </para>
    /// <para>
    /// The refusal carries an upgrade reference, so the UI can offer to sell the module rather than
    /// showing a dead end.
    /// </para>
    /// </remarks>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string module)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        builder.AddEndpointFilter(async (context, next) =>
        {
            IEntitlementService entitlements = context.HttpContext.RequestServices
                .GetRequiredService<IEntitlementService>();

            if (!await entitlements.IsModuleEnabledAsync(module, context.HttpContext.RequestAborted))
            {
                throw new ModuleNotEnabledException(module);
            }

            return await next(context);
        });

        return builder;
    }

    private static async Task<Created<ActivationResponse>> ActivateAsync(
        ActivateRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ActivationResult result = await dispatcher.SendAsync(
            new ActivateInstallationCommand(request.LicenceKey, request.StoreName, request.ContactEmail),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created("/api/v1/licence", ToResponse(result));
    }

    private static async Task<Ok<ActivationResponse>> RebindAsync(
        RebindRequestBody request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ActivationResult result = await dispatcher.SendAsync(
            new RebindActivationCommand(ParseReason(request.Reason), request.Evidence),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(result));
    }

    private static async Task<Ok<LeaseRefreshResponse>> RefreshAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        LeaseRefreshResult result = await dispatcher
            .SendAsync(new RefreshLeaseCommand(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new LeaseRefreshResponse(
            result.Reached,
            result.Level.ToString(),
            result.LeaseExpiresAt));
    }

    private static async Task<Ok<EmergencyCodeResponse>> RedeemAsync(
        RedeemEmergencyCodeRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset expiresAt = await dispatcher
            .SendAsync(new RedeemEmergencyCodeCommand(request.Code), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new EmergencyCodeResponse(expiresAt));
    }

    private static async Task<Results<Ok<SubLeaseResponse>, UnauthorizedHttpResult>> SubLeaseAsync(
        IDispatcher dispatcher,
        IPrincipalAccessor principal,
        CancellationToken cancellationToken)
    {
        // A sub-lease belongs to a terminal, so the caller has to be one. The certificate scheme
        // (Stage 02) is what put the terminal id on the principal; a bearer token from a person does
        // not carry one, and a person has the licence screen instead.
        if (principal.TerminalId is not { } terminalId)
        {
            return TypedResults.Unauthorized();
        }

        SubLeaseResponse response = await dispatcher
            .QueryAsync(new GetSubLeaseQuery(terminalId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(response);
    }

    private static async Task<NoContent> ApproveAsync(
        Guid grantId,
        ApproveSupportAccessRequest? request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        TimeSpan? duration = request?.DurationHours is { } hours
            ? TimeSpan.FromHours(hours)
            : null;

        await dispatcher
            .SendAsync(new ApproveSupportAccessCommand(grantId, duration), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeclineAsync(
        Guid grantId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DeclineSupportAccessCommand(grantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RevokeAsync(
        Guid grantId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new RevokeSupportAccessCommand(grantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static ActivationResponse ToResponse(ActivationResult result)
        => new(
            result.ActivationId,
            result.TenantId,
            result.PlanCode,
            result.LicenceExpiresAt,
            result.Level.ToString());

    /// <summary>Parses a rebind reason, refusing anything unrecognised.</summary>
    /// <remarks>
    /// Never a fallback. A reason that quietly defaulted to <c>DisasterRecovery</c> would auto-approve
    /// every rebind, which is the one thing the vendor's rebind allowance exists to stop.
    /// </remarks>
    private static RebindReason ParseReason(string value)
        => Enum.TryParse(value, ignoreCase: true, out RebindReason reason) && Enum.IsDefined(reason)
            ? reason
            : throw new MalformedLicensingFieldException(nameof(RebindRequestBody.Reason), value);
}

/// <summary>A field on a licensing request is not one of the values it may be.</summary>
/// <param name="field">The field.</param>
/// <param name="value">What was sent.</param>
public sealed class MalformedLicensingFieldException(string field, string value)
    : DomainException(
        "LICENCE_FIELD_MALFORMED",
        $"'{value}' is not a valid {field}.",
        DomainProblemKind.Malformed);
