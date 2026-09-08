using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory.Commands;

/// <summary>Plans sourcing for an order without committing anything — the dry run.</summary>
/// <param name="OrderingCompanyId">The company the order was captured against.</param>
/// <param name="Demands">The lines asking for stock, with captured economics.</param>
/// <param name="ProximityLocations">Location ids nearest-first, when the caller knows geography.</param>
public sealed record PlanSourcingQuery(
    Guid OrderingCompanyId,
    IReadOnlyList<SourcingDemandLine> Demands,
    IReadOnlyList<Guid> ProximityLocations) : IQuery<SourcingPlan>;

/// <summary>Plans from the group projection. No side effects: a plan is a projection until committed.</summary>
/// <param name="planner">The single planning semantics commit shares.</param>
public sealed class PlanSourcingQueryHandler(ISourcingPlanner planner)
    : IQueryHandler<PlanSourcingQuery, SourcingPlan>
{
    /// <inheritdoc />
    public Task<SourcingPlan> HandleAsync(PlanSourcingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return planner.PlanAsync(query.Demands, query.OrderingCompanyId, query.ProximityLocations, cancellationToken);
    }
}

/// <summary>Commits a sourcing plan as a saga: plan from the group view, commit per company.</summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="OrderingCompanyId">The company the order was captured against.</param>
/// <param name="Source">The source order and its lines.</param>
/// <param name="IdempotencyKey">Stable across retries of the same commit.</param>
/// <param name="ProximityLocations">Location ids nearest-first, when the caller knows geography.</param>
/// <param name="InitiatedBy">Who asked, in audit-principal form.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CommitSourcingPlanCommand(
    Guid TenantId,
    Guid OrderingCompanyId,
    SourcingSourceOrder Source,
    string IdempotencyKey,
    IReadOnlyList<Guid> ProximityLocations,
    string InitiatedBy) : ICommand<CommittedSourcingPlan>;

/// <summary>Rejects a malformed commit command before it reaches the handler.</summary>
public sealed class CommitSourcingPlanCommandValidator : AbstractValidator<CommitSourcingPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public CommitSourcingPlanCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.OrderingCompanyId).NotEmpty();
        RuleFor(command => command.Source).NotNull();
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(256);
        RuleFor(command => command.InitiatedBy).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Commits through <see cref="ISourcingCommitService"/>.</summary>
/// <remarks>
/// Thin by design, like the reservation handlers: the service owns the saga, and the pipeline
/// transaction around this handler stays empty — one leg, one company, one transaction each.
/// </remarks>
/// <param name="commit">Commits the plan.</param>
public sealed class CommitSourcingPlanCommandHandler(ISourcingCommitService commit)
    : ICommandHandler<CommitSourcingPlanCommand, CommittedSourcingPlan>
{
    /// <inheritdoc />
    public Task<CommittedSourcingPlan> HandleAsync(CommitSourcingPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return commit.CommitAsync(
            new SourcingCommitRequest(
                command.TenantId,
                command.OrderingCompanyId,
                command.Source,
                command.IdempotencyKey,
                command.ProximityLocations,
                command.InitiatedBy),
            cancellationToken);
    }
}

/// <summary>Expires due holds in one company. The expiry job's unit of work.</summary>
/// <param name="CompanyId">The company whose holds expire. Informational: the acting company comes from the scope.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ExpireCompanyReservationsCommand(Guid CompanyId) : ICommand<int>;

/// <summary>Rejects a malformed expiry command before it reaches the handler.</summary>
public sealed class ExpireCompanyReservationsCommandValidator : AbstractValidator<ExpireCompanyReservationsCommand>
{
    /// <summary>Builds the rules.</summary>
    public ExpireCompanyReservationsCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
    }
}

/// <summary>Expires due holds through <see cref="IReservationService"/>.</summary>
/// <param name="reservations">Expires the holds.</param>
public sealed class ExpireCompanyReservationsCommandHandler(IReservationService reservations)
    : ICommandHandler<ExpireCompanyReservationsCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(ExpireCompanyReservationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return reservations.ExpireDueAsync(cancellationToken);
    }
}
