using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Application.Inventory.Queries;
using VumaRetail.Application.Registry;
using VumaRetail.Contracts.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Inventory;

/// <summary>
/// The Stage 08c endpoints: available-to-promise (company-local and group) and the internal
/// reservation operations.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/v1/availability</c> answers both of the stage's questions from one path: without
/// <c>groupScope</c> it reads inside the acting company (authoritative,
/// <c>inventory.availability.view</c>); with <c>groupScope=true</c> it reads the registry
/// projection (planning only, always stamped <c>AsAt</c>) and additionally requires
/// <c>registry.availability.view</c>. The route gate carries the first permission; the second is
/// enforced in the handler through <c>IPermissionChecker</c> because the endpoint cannot know
/// which permission applies until it reads the flag — the narrow escape hatch ADR-137 built for
/// exactly this shape.
/// </para>
/// </remarks>
public static class AvailabilityEndpoints
{
    /// <summary>Maps the availability and reservation endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaAvailability(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        api.MapGet("/availability", GetAvailabilityAsync)
            .RequirePermission(InventoryPermissions.AvailabilityView)
            .Produces<LocalAvailabilityResponse>()
            .Produces<GroupAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Available-to-promise for one stock-keeping unit, local or group-wide.")
            .WithDescription(
                "Without groupScope: authoritative, read inside the acting company. With "
                + "groupScope=true: planning only, from the registry projection, always stamped "
                + "AsAt with stale contributors named.");

        RouteGroupBuilder reservations = api.MapGroup("/inventory/reservations").WithTags("Inventory");

        reservations.MapPost("/reserve", ReserveStockAsync)
            .RequirePermission(InventoryPermissions.ReservationManage)
            .Produces<ReserveStockResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Holds stock for a document, reporting what could not be covered as shortfall.");

        reservations.MapPost("/consume", ConsumeReservationAsync)
            .RequirePermission(InventoryPermissions.ReservationManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Consumes a live hold — the held quantity shipped or issued.");

        reservations.MapPost("/release", ReleaseReservationAsync)
            .RequirePermission(InventoryPermissions.ReservationManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Releases a live hold — available is restored by a new ledger row.");

        return endpoints;
    }

    private static async Task<IResult> GetAvailabilityAsync(
        IDispatcher dispatcher,
        IPrincipalAccessor principal,
        Application.Abstractions.ITenantContext tenant,
        IPermissionChecker permissions,
        CancellationToken cancellationToken,
        Guid? itemId = null,
        Guid? itemVariantId = null,
        Guid? locationId = null,
        bool groupScope = false)
    {
        if (groupScope)
        {
            Guid userId = ParseUserId(principal.Principal);
            bool allowed = await permissions.HasPermissionAsync(
                    userId, tenant.StoreId, RegistryPermissions.GroupAvailabilityView, cancellationToken)
                .ConfigureAwait(false);

            if (!allowed)
            {
                throw InventoryForbiddenException.GroupAvailabilityNotPermitted();
            }

            GroupAvailabilityView view = await dispatcher
                .QueryAsync(new GetGroupAvailabilityQuery(itemId, itemVariantId), cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Ok(ToResponse(view));
        }

        if (locationId is null)
        {
            throw new ValidationFailedException(
                nameof(GetLocalAvailabilityQuery),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(locationId)] = ["A location is required for company-local availability."],
                });
        }

        LocalAvailability local = await dispatcher
            .QueryAsync(new GetLocalAvailabilityQuery(locationId.Value, itemId, itemVariantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(local));
    }

    private static async Task<IResult> ReserveStockAsync(
        ReserveStockRequest request,
        IDispatcher dispatcher,
        VumaRetail.Application.Abstractions.Registry.ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        ReservationSource source = ParseEnum<ReservationSource>(
            request.Source, nameof(request.Source), nameof(ReserveStockCommand));

        ReserveOutcome outcome = await dispatcher
            .SendAsync(
                new ReserveStockCommand(
                    request.LocationId,
                    request.ItemId,
                    request.ItemVariantId,
                    new Quantity(request.Quantity, request.UnitOfMeasure),
                    source,
                    request.SourceDocumentId,
                    request.GroupDocumentRef,
                    request.ExpiresAt,
                    request.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            outcome.ReservationId is null
                ? "/api/v1/inventory/reservations"
                : $"/api/v1/inventory/reservations/{outcome.ReservationId}",
            new ReserveStockResponse(
                outcome.ReservationId,
                outcome.Held.Value,
                outcome.Shortfall.Value,
                outcome.AvailableAfter.Value,
                outcome.Held.UnitOfMeasure,
                outcome.AsAt));
    }

    private static async Task<IResult> ConsumeReservationAsync(
        ConsumeReservationRequest request,
        IDispatcher dispatcher,
        VumaRetail.Application.Abstractions.Registry.ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        await dispatcher
            .SendAsync(new ConsumeReservationCommand(request.ReservationId, request.ConsumedByReferenceId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReleaseReservationAsync(
        ReleaseReservationRequest request,
        IDispatcher dispatcher,
        VumaRetail.Application.Abstractions.Registry.ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        await dispatcher
            .SendAsync(new ReleaseReservationCommand(request.ReservationId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Binds the acting company for this request scope when the caller named one explicitly.
    /// </summary>
    /// <remarks>
    /// The interim selection mechanism until per-request company middleware lands (the Stage 06c
    /// follow-up): <c>/api/v1/companies/select</c> binds for its own scope only, so an endpoint
    /// that needs a company today must accept one. A request that names a different company than
    /// the scope already holds is refused loudly rather than silently switching ledgers mid-call.
    /// </remarks>
    private static void BindCompany(
        VumaRetail.Application.Abstractions.Registry.ICompanyContext company,
        Guid? companyId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (companyId is null || companyId == Guid.Empty)
        {
            return;
        }

        if (company.CompanyId is { } bound && bound != companyId)
        {
            throw new ValidationFailedException(
                nameof(ReserveStockCommand),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["The request names a different company than the scope already holds."],
                });
        }

        if (company.CompanyId is null)
        {
            company.SetCompany(companyId.Value);
        }
    }

    private static LocalAvailabilityResponse ToResponse(LocalAvailability local) => new(
        local.LocationId,
        local.ItemId,
        local.ItemVariantId,
        ToResponse(local.Promise));

    private static GroupAvailabilityResponse ToResponse(GroupAvailabilityView view) => new(
        view.ItemId,
        view.ItemVariantId,
        [.. view.Contributions.Select(contribution => new GroupAvailabilityContributionResponse(
            contribution.CompanyId,
            contribution.CompanyCode,
            ToResponse(contribution.Promise),
            contribution.AsAt,
            contribution.IsStale))],
        view.TotalFreshAvailable,
        view.StaleContributorCodes,
        view.AsAt);

    private static AvailableToPromiseResponse ToResponse(AvailableToPromise promise) => new(
        promise.OnHand.Value,
        promise.Reserved.Value,
        promise.InStaging.Value,
        promise.Incoming.Value,
        promise.Available.Value,
        promise.OnHand.UnitOfMeasure,
        promise.AsAt);

    private static Guid ParseUserId(string principal)
    {
        const string prefix = "user:";
        if (principal.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(principal[prefix.Length..], out Guid userId)
            && userId != Guid.Empty)
        {
            return userId;
        }

        throw InventoryForbiddenException.GroupAvailabilityNotPermitted();
    }

    /// <summary>Parses an enum from a request field, refusing with a 400 rather than a 500 on a typo.</summary>
    private static TEnum ParseEnum<TEnum>(string value, string propertyName, string messageName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            messageName,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [propertyName] = [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}."],
            });
    }
}
