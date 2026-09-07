namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Strategy for allocating demand across multiple companies.
/// Pure function: takes only data, returns deterministic allocation.
/// Implementations must be stateless and reentrant.
/// </summary>
public interface ICompanySourcingStrategy
{
    /// <summary>
    /// Plan a sourcing allocation for a single demand line.
    /// </summary>
    /// <param name="demand">The demand line to allocate.</param>
    /// <param name="groupAvailability">Per-company available quantities (from stale group projection).</param>
    /// <param name="orderingCompanyId">The company placing the order.</param>
    /// <param name="proximityRank">Ordered list of company IDs by proximity (if available).</param>
    /// <returns>A sourcing plan allocating the demand across companies.</returns>
    SourcingPlan Plan(
        SourcingDemandLine demand,
        Guid orderLineId,
        IReadOnlyDictionary<Guid, decimal> groupAvailability,
        Guid orderingCompanyId,
        IReadOnlyList<Guid> proximityRank);
}
