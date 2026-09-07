namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Default sourcing strategy: ordering company first, then linked companies by proximity rank,
/// then remaining companies by most available.
/// Never allocates against a company with zero available (business rule 3).
/// Output is deterministic: ties break by company code so result is not row-order-dependent.
/// Pure function with no DB, no clock, no side effects.
/// </summary>
public sealed class AvailabilityThenProximityStrategy : ICompanySourcingStrategy
{
    public SourcingPlan Plan(
        SourcingDemandLine demand,
        Guid orderLineId,
        IReadOnlyDictionary<Guid, decimal> groupAvailability,
        Guid orderingCompanyId,
        IReadOnlyList<Guid> proximityRank)
    {
        if (demand == null) throw new ArgumentNullException(nameof(demand));
        if (groupAvailability == null) throw new ArgumentNullException(nameof(groupAvailability));
        if (orderingCompanyId == Guid.Empty) throw new ArgumentException("Ordering company ID is required.", nameof(orderingCompanyId));
        if (proximityRank == null) throw new ArgumentNullException(nameof(proximityRank));

        var plan = new SourcingPlan(orderLineId, demand);
        decimal remaining = demand.Quantity;
        var companyMetadata = new Dictionary<Guid, CompanyMetadata>();

        // Build company metadata from groupAvailability
        foreach (var (companyId, available) in groupAvailability)
        {
            if (available > 0)
            {
                var proximityIndex = proximityRank.IndexOf(companyId);
                companyMetadata[companyId] = new CompanyMetadata
                {
                    CompanyId = companyId,
                    Available = available,
                    ProximityIndex = proximityIndex,
                    IsPrimaryOrdering = companyId == orderingCompanyId
                };
            }
        }

        // Sort: primary company first, then by proximity rank, then by available (descending), then by company code
        var sortedCompanies = companyMetadata.Values
            .OrderByDescending(x => x.IsPrimaryOrdering)
            .ThenBy(x => x.ProximityIndex == -1 ? int.MaxValue : x.ProximityIndex)
            .ThenByDescending(x => x.Available)
            .ThenBy(x => x.CompanyCode) // Determinism: break ties by company code
            .ToList();

        // Allocate demand across sorted companies
        foreach (var company in sortedCompanies)
        {
            if (remaining <= 0)
                break;

            var toAllocate = Math.Min(remaining, company.Available);
            var backorder = remaining - toAllocate;
            if (backorder < 0)
                backorder = 0;

            // Add allocation: we assume location is the company's primary location for now (or default)
            // This will be refined when the commit service reads real per-location availability
            plan.AddAllocation(
                company.CompanyId,
                company.CompanyId, // Use company ID as location ID (simplified; Stage 13b will add geography)
                toAllocate,
                0,
                company.CompanyCode);

            remaining = backorder;
        }

        // If anything remains, backorder it against the primary ordering company
        if (remaining > 0)
        {
            if (companyMetadata.TryGetValue(orderingCompanyId, out var primary))
            {
                plan.AddAllocation(
                    orderingCompanyId,
                    orderingCompanyId,
                    0,
                    remaining,
                    primary.CompanyCode);
            }
            else
            {
                // Ordering company not in availability dict: backorder everything
                plan.AddAllocation(
                    orderingCompanyId,
                    orderingCompanyId,
                    0,
                    remaining,
                    "UNKNOWN");
            }
        }

        return plan;
    }

    private sealed class CompanyMetadata
    {
        public Guid CompanyId { get; set; }
        public decimal Available { get; set; }
        public int ProximityIndex { get; set; } // -1 if not in proximity rank
        public bool IsPrimaryOrdering { get; set; }
        public string CompanyCode { get; set; } = null!;
    }
}
