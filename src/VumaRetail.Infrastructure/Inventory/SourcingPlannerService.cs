using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>Plans sourcing from the group projection — the single planning semantics.</summary>
/// <param name="availability">The group projection reader.</param>
/// <param name="strategy">The sourcing strategy.</param>
public sealed class SourcingPlanner(
    IAvailabilityService availability,
    ICompanySourcingStrategy strategy) : ISourcingPlanner
{
    /// <inheritdoc />
    public async Task<SourcingPlan> PlanAsync(
        IReadOnlyList<SourcingDemandLine> demands,
        Guid orderingCompanyId,
        IReadOnlyList<Guid> proximityLocations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(demands);

        List<SourcingCandidate> candidates = [];
        foreach (SourcingDemandLine demand in demands)
        {
            GroupAvailabilityView view = await availability
                .GetGroupAsync(demand.ItemId, demand.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            candidates.AddRange(view.Contributions.Select(contribution => new SourcingCandidate(
                contribution.CompanyId,
                contribution.CompanyCode,
                contribution.LocationId,
                demand.ItemId,
                demand.ItemVariantId,
                contribution.Promise.Available,
                contribution.AsAt,
                contribution.IsStale)));
        }

        return strategy.Plan(new SourcingPlanRequest(
            demands, candidates, orderingCompanyId, proximityLocations));
    }
}
