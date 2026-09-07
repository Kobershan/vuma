using VumaRetail.Domain.Inventory;

namespace VumaRetail.Application.Inventory;

/// <summary>Plans sourcing from the group projection — the single planning semantics.</summary>
/// <remarks>
/// Shared by the dry-run query and the commit service so plan and commit can never disagree
/// about what "the plan" means: both read the same projection through
/// <c>IAvailabilityService</c> and run the same <c>ICompanySourcingStrategy</c>. Planning reads;
/// only <c>ISourcingCommitService</c>, inside each owning company's database, commits.
/// </remarks>
public interface ISourcingPlanner
{
    /// <summary>Plans sourcing for every demand line from the group projection.</summary>
    Task<SourcingPlan> PlanAsync(
        IReadOnlyList<SourcingDemandLine> demands,
        Guid orderingCompanyId,
        IReadOnlyList<Guid> proximityLocations,
        CancellationToken cancellationToken = default);
}
