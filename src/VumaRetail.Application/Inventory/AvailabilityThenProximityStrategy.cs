using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>
/// The default sourcing strategy: the ordering company first, then the nearest store, then the
/// most available — behind <c>ICompanySourcingStrategy</c> so Stage 15 can replace it.
/// </summary>
/// <remarks>
/// Pure function of its inputs: no database, no clock. Ordering within a tier is total
/// (proximity rank, then available descending, then company code, then location id), so the same
/// inputs always produce the same plan and the stage's acceptance numbers are unit tests.
/// Stale candidates are planned from, not excluded: staleness is the projection's nature
/// (ADR-119), and excluding a quiet-but-stocked company would refuse a plan the commit could
/// have filled. The commit re-checks every leg in the owning company's database, so a stale
/// figure can cause a shortfall (re-sourced, then backordered) but never a negative (ADR-102).
/// A candidate stating no availability is never allocated against (business rule 3).
/// </remarks>
public sealed class AvailabilityThenProximityStrategy : ICompanySourcingStrategy
{
    /// <inheritdoc />
    public SourcingPlan Plan(SourcingPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OrderingCompanyId == Guid.Empty)
        {
            throw new ArgumentException("A sourcing plan needs its ordering company.", nameof(request));
        }

        List<SourcingPlanLine> lines = new(request.Demands.Count);

        foreach (SourcingDemandLine demand in request.Demands)
        {
            lines.Add(PlanLine(demand, request));
        }

        return new SourcingPlan(request.OrderingCompanyId, lines);
    }

    private static SourcingPlanLine PlanLine(SourcingDemandLine demand, SourcingPlanRequest request)
    {
        ValidateDemand(demand);

        List<SourcingCandidate> candidates = request.Candidates
            .Where(candidate => Matches(candidate, demand) && candidate.Available.Value > 0m)
            .OrderBy(candidate => candidate.CompanyId == request.OrderingCompanyId ? 0 : 1)
            .ThenBy(candidate => ProximityRank(candidate.LocationId, request.ProximityLocations))
            .ThenByDescending(candidate => candidate.Available.Value)
            .ThenBy(candidate => candidate.CompanyCode, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.LocationId)
            .ToList();

        List<SourcingAllocation> allocations = [];
        decimal remaining = demand.Demanded.Value;

        foreach (SourcingCandidate candidate in candidates)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, candidate.Available.Value);
            if (take <= 0m)
            {
                continue;
            }

            allocations.Add(new SourcingAllocation(
                candidate.CompanyId,
                candidate.LocationId,
                new Quantity(take, demand.Demanded.UnitOfMeasure)));
            remaining -= take;
        }

        var backorder = new Quantity(remaining, demand.Demanded.UnitOfMeasure);
        var planned = new SourcingPlanLine(demand.LineId, allocations, backorder);

        AssertBalanced(demand, planned);

        return planned;
    }

    private static bool Matches(SourcingCandidate candidate, SourcingDemandLine demand)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        bool skuMatches = (demand.ItemId is not null && candidate.ItemId == demand.ItemId)
            || (demand.ItemVariantId is not null && candidate.ItemVariantId == demand.ItemVariantId);

        return skuMatches
            && string.Equals(candidate.Available.UnitOfMeasure, demand.Demanded.UnitOfMeasure, StringComparison.Ordinal);
    }

    private static int ProximityRank(Guid locationId, IReadOnlyList<Guid> proximity)
    {
        for (int index = 0; index < proximity.Count; index++)
        {
            if (proximity[index] == locationId)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static void ValidateDemand(SourcingDemandLine demand)
    {
        bool hasItem = demand.ItemId is not null && demand.ItemId != Guid.Empty;
        bool hasVariant = demand.ItemVariantId is not null && demand.ItemVariantId != Guid.Empty;
        if (hasItem == hasVariant)
        {
            throw InventoryRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (demand.Demanded.IsNegative || demand.Demanded.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }
    }

    private static void AssertBalanced(SourcingDemandLine demand, SourcingPlanLine planned)
    {
        decimal accounted = planned.Allocations.Sum(allocation => allocation.Quantity.Value)
            + planned.Backorder.Value;

        if (accounted != demand.Demanded.Value)
        {
            throw InventoryRuleException.SourcingPlanUnbalanced(
                demand.LineId,
                demand.Demanded,
                new Quantity(accounted, demand.Demanded.UnitOfMeasure));
        }
    }
}
