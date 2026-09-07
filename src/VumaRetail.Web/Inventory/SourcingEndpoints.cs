using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Contracts.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Inventory;

/// <summary>The Stage 08c sourcing endpoints: dry-run planning, saga-backed commit, intent state.</summary>
/// <remarks>
/// Planning is a read shaped as a <c>POST</c> (the demand set belongs in the body, not in a query
/// string) gated on <c>inventory.availability.view</c>; committing writes through
/// <c>inventory.reservation.manage</c>. Every figure in every response carries its basis — the
/// committed plan's backorders say what the group could not cover, never a silent short-ship.
/// </remarks>
public static class SourcingEndpoints
{
    /// <summary>Maps the sourcing endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaSourcing(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder sourcing = endpoints.MapVumaApi().MapGroup("/sourcing").WithTags("Sourcing");

        sourcing.MapPost("/plan", PlanSourcingAsync)
            .RequirePermission(InventoryPermissions.AvailabilityView)
            .Produces<SourcingPlanResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Plans sourcing for an order without committing anything — the dry run.");

        sourcing.MapPost("/commit", CommitSourcingPlanAsync)
            .RequirePermission(InventoryPermissions.ReservationManage)
            .Produces<CommittedSourcingPlanResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Commits a sourcing plan as a saga: one reservation leg per company.");

        sourcing.MapGet("/intents/{intentId:guid}", GetSourcingIntentAsync)
            .RequirePermission(InventoryPermissions.ReservationManage)
            .Produces<SourcingIntentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads a sourcing saga intent and its legs for the in-flight report.");

        return endpoints;
    }

    private static async Task<IResult> PlanSourcingAsync(
        PlanSourcingRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        SourcingPlan plan = await dispatcher
            .QueryAsync(
                new PlanSourcingQuery(
                    request.OrderingCompanyId,
                    [.. request.Demands.Select(ToDemand)],
                    [.. request.ProximityLocationIds]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(plan));
    }

    private static async Task<IResult> CommitSourcingPlanAsync(
        CommitSourcingPlanRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        SalesChannel channel = ParseEnum<SalesChannel>(
            request.Source.Channel, nameof(request.Source.Channel), nameof(CommitSourcingPlanCommand));
        OrderFulfilmentType fulfilment = ParseEnum<OrderFulfilmentType>(
            request.Source.FulfilmentType, nameof(request.Source.FulfilmentType), nameof(CommitSourcingPlanCommand));

        CommittedSourcingPlan committed = await dispatcher
            .SendAsync(
                new CommitSourcingPlanCommand(
                    request.TenantId,
                    request.OrderingCompanyId,
                    new Application.Inventory.SourcingSourceOrder(
                        request.Source.OrderId,
                        request.Source.OrderNumber,
                        request.Source.PartnerId,
                        channel,
                        fulfilment,
                        DeliveryAddress: null,
                        request.Source.Currency,
                        [.. request.Source.Demands.Select(ToDemand)]),
                    request.IdempotencyKey,
                    [.. request.ProximityLocationIds],
                    request.InitiatedBy),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/sourcing/intents/{committed.IntentId}",
            ToResponse(committed));
    }

    private static async Task<IResult> GetSourcingIntentAsync(
        Guid intentId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Application.Inventory.SourcingIntentResult? intent = await dispatcher
            .QueryAsync(new Application.Inventory.Queries.GetSourcingIntentQuery(intentId), cancellationToken)
            .ConfigureAwait(false);

        return intent is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SourcingIntentResponse(
                intent.IntentId,
                intent.Type,
                intent.State,
                intent.CreatedAt,
                [.. intent.Legs.Select(leg => new SourcingIntentLegResponse(
                    leg.LegId, leg.CompanyId, leg.State, leg.Attempts, leg.LastError))]));
    }

    private static SourcingDemandLine ToDemand(SourcingDemandLineRequest request) => new(
        request.LineId,
        request.ItemId,
        request.ItemVariantId,
        new Quantity(request.Demanded, request.UnitOfMeasure),
        new Money(request.UnitPrice, request.Currency),
        new Money(request.LineNet, request.Currency),
        new Money(request.LineTax, request.Currency),
        new Money(request.LineGross, request.Currency));

    private static SourcingPlanResponse ToResponse(SourcingPlan plan) => new(
        plan.OrderingCompanyId,
        [.. plan.Lines.Select(line => new SourcingPlanLineResponse(
            line.LineId,
            [.. line.Allocations.Select(allocation => new SourcingAllocationResponse(
                allocation.CompanyId,
                allocation.LocationId,
                allocation.Quantity.Value,
                allocation.Quantity.UnitOfMeasure))],
            line.Backorder.Value,
            line.Backorder.UnitOfMeasure))],
        plan.IsFullyCovered);

    private static CommittedSourcingPlanResponse ToResponse(CommittedSourcingPlan committed) => new(
        committed.IntentId,
        ToResponse(committed.Plan),
        [.. committed.Legs.Select(leg => new SourcingLegOutcomeResponse(
            leg.CompanyId,
            leg.LegId,
            [.. leg.Held.Keys],
            leg.Shortfalls.ToDictionary(entry => entry.Key, entry => entry.Value.Value)))],
        committed.SplitOrderIds,
        committed.WasReplay);

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
