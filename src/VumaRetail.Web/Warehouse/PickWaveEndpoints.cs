using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Application.Warehouse.Permissions;
using VumaRetail.Contracts.Warehouse;
using VumaRetail.Domain.Warehouse;
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
            .Produces<VumaRetail.Contracts.Warehouse.PreviewConsolidatedWaveResponse>()
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
        Guid locationId,
        DateOnly periodFrom,
        DateOnly periodTo,
        string geographyLevel,
        string geographyValue,
        Guid? companyScopeId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var query = new VumaRetail.Application.Warehouse.Queries.PreviewConsolidatedWaveQuery(
            locationId, periodFrom, periodTo, geographyLevel, geographyValue, companyScopeId);

        var appResponse = await dispatcher
            .QueryAsync(query, cancellationToken)
            .ConfigureAwait(false);

        var contractResponse = new VumaRetail.Contracts.Warehouse.PreviewConsolidatedWaveResponse(
            appResponse.LocationId,
            appResponse.GeographyLevel,
            appResponse.GeographyValue,
            appResponse.PeriodFrom,
            appResponse.PeriodTo,
            appResponse.GroupedLines.Select(g => new VumaRetail.Contracts.Warehouse.GroupedLinePreview(
                g.ItemId, g.ItemVariantId, g.UnitOfMeasure, g.PackSize, g.TotalQuantity, g.OrderCount)).ToList(),
            appResponse.OrderBreakdowns.Select(b => new VumaRetail.Contracts.Warehouse.OrderBreakdownPreview(
                b.OrderId, b.OrderLineId, b.ItemId, b.Quantity)).ToList());

        return TypedResults.Ok(contractResponse);
    }

    private static async Task<IResult> GetBreakdownAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var appBreakdowns = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.GetBreakdownQuery(id), cancellationToken)
            .ConfigureAwait(false);

        var contractBreakdowns = appBreakdowns.Select(b => new VumaRetail.Contracts.Warehouse.OrderBreakdownResponse(
            b.OrderId, b.OrderLineId, b.Quantity)).ToList();

        return TypedResults.Ok(contractBreakdowns);
    }

    private static async Task<IResult> CreateCountScheduleAsync(
        CreateCountScheduleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CountCadence cadence = ParseEnum<CountCadence>(request.Cadence, nameof(request.Cadence), nameof(CreateCountScheduleCommand));

        CreateCountScheduleCommand command = new CreateCountScheduleCommand(
            request.Name, cadence, request.Scope,
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
        var appSummaries = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.ListCountSchedulesQuery(storeId, activeOnly), cancellationToken)
            .ConfigureAwait(false);

        var contractSummaries = appSummaries.Select(s => new CountScheduleResponse(
            s.Id, s.Name, s.Cadence, s.Scope, s.SlowMoverDays,
            s.RandomSampleSize, s.NextRunAt, s.IsActive)).ToList();

        return TypedResults.Ok(contractSummaries);
    }

    private static async Task<IResult> GetCountSheetAsync(
        Guid id, Guid locationId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var appSheet = await dispatcher
            .QueryAsync(new VumaRetail.Application.Warehouse.Queries.GetCountSheetQuery(id, locationId), cancellationToken)
            .ConfigureAwait(false);

        var contractSheet = new CountSheetResponse(
            appSheet.ScheduleId,
            appSheet.LocationId,
            appSheet.GeneratedAt,
            appSheet.Counts.Select(c => new CycleCountSummary(
                c.CycleCountId, c.Scope, c.Status, c.ScheduledAt)).ToList(),
            appSheet.InFlightWarnings.Select(w => new InFlightWarning(
                w.BinId, w.ItemId, w.ItemVariantId, w.InFlightQuantity, w.WaveReference)).ToList());

        return TypedResults.Ok(contractSheet);
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