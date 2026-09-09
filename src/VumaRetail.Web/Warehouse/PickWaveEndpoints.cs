using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Contracts.Warehouse;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Warehouse;

/// <summary>
/// Stage 13b: consolidated pick waves, staging states and interval counts.
/// Every write goes through a command handler; the build does not persist until release.
/// </summary>
public static class PickWaveEndpoints
{
    /// <summary>Maps the consolidated pick wave endpoints under the current API version.</summary>
    public static IEndpointRouteBuilder MapPickWaves(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder waves = api.MapGroup("/pick-waves/consolidated")
            .WithTags("PickWaves")
            .RequireModule("warehouse");

        waves.MapPost("/build", BuildConsolidatedWaveAsync)
            .RequirePermission(WarehousePermissions.WaveBuild)
            .Produces<ConsolidatedWaveResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Builds a consolidated pick wave from open order lines.");

        waves.MapPost("/{id:guid}/release", ReleaseConsolidatedWaveAsync)
            .RequirePermission(WarehousePermissions.WaveRelease)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Releases a consolidated wave: allocates bins and starts picking.");

        waves.MapGet("/preview", PreviewConsolidatedWaveAsync)
            .RequirePermission(WarehousePermissions.WaveBuild)
            .Produces<PreviewConsolidatedWaveResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Previews a consolidated wave without committing it.");

        waves.MapGet("/{id:guid}/breakdown", GetBreakdownAsync)
            .RequirePermission(WarehousePermissions.WaveBuild)
            .Produces<IReadOnlyList<OrderBreakdownResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads the per-order breakdown for a grouped wave line.");

        waves.MapPost("/count-schedules", CreateCountScheduleAsync)
            .RequirePermission(WarehousePermissions.CountSchedule)
            .Produces<CountScheduleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a new count schedule.");

        waves.MapGet("/count-schedules", ListCountSchedulesAsync)
            .RequirePermission(WarehousePermissions.CountSchedule)
            .Produces<IReadOnlyList<CountScheduleResponse>>()
            .WithSummary("Lists count schedules.");

        waves.MapGet("/counts/{id:guid}/sheet", GetCountSheetAsync)
            .RequirePermission(WarehousePermissions.CountPerform)
            .Produces<CountSheetResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Generates a count sheet from a schedule, with in-flight warnings.");

        return endpoints;
    }

    private static async Task<IResult> BuildConsolidatedWaveAsync(
        BuildConsolidatedWaveRequest request,
        Guid? companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        PickWaveFilter filter = new PickWaveFilter(
            request.PeriodFrom, request.PeriodTo,
            request.GeographyLevel, request.GeographyValue,
            request.CompanyScopeId, request.LocationId, null);

        BuildConsolidatedWaveCommand command = new BuildConsolidatedWaveCommand(filter, []);

        Guid id = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/pick-waves/consolidated/{id}",
            new ConsolidatedWaveResponse(id, "Open", request.GeographyLevel, request.GeographyValue,
                request.PeriodFrom, request.PeriodTo, [], []));
    }

    private static async Task<IResult> ReleaseConsolidatedWaveAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReleaseConsolidatedWaveCommand(id), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PreviewConsolidatedWaveAsync(
        VumaRetail.Application.Warehouse.Queries.PreviewConsolidatedWaveQuery query, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PreviewConsolidatedWaveResponse response = await dispatcher
            .QueryAsync(query, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetBreakdownAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderBreakdownResponse> breakdowns = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.GetBreakdownQuery(id), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(breakdowns);
    }

    private static async Task<IResult> CreateCountScheduleAsync(
        CreateCountScheduleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CreateCountScheduleCommand command = new CreateCountScheduleCommand(
            request.Name, request.Cadence, request.Scope,
            request.SlowMoverDays, request.RandomSampleSize, request.NextRunAt);

        Guid id = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/pick-waves/consolidated/count-schedules/{id}",
            new CountScheduleResponse(id, request.Name, request.Cadence,
                request.Scope, request.SlowMoverDays, request.RandomSampleSize,
                request.NextRunAt, true));
    }

    private static async Task<IResult> ListCountSchedulesAsync(
        Guid? storeId, bool activeOnly, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<CountScheduleSummary> summaries = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.ListCountSchedulesQuery(storeId, activeOnly), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            summaries.Select(s => new CountScheduleResponse(
                s.Id, s.Name, s.Cadence, s.Scope, s.SlowMoverDays,
                s.RandomSampleSize, s.NextRunAt, s.IsActive)).ToList());
    }

    private static async Task<IResult> GetCountSheetAsync(
        Guid id, Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CountSheetResponse sheet = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.GetCountSheetQuery(id, locationId), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(sheet);
    }

    private static void BindCompany(ICompanyContext company, Guid? companyId)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (companyId is null || companyId == Guid.Empty)
        {
            return;
        }
        if (company.CompanyId is { } bound && bound != companyId)
        {
            throw new ValidationFailedException(
                nameof(companyId),
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
}