namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Contracts.Inventory.Sourcing;

/// <summary>
/// Query to plan a sourcing allocation (dry run).
/// No side effects, no writes.
/// </summary>
public sealed class PlanSourcingQuery
{
    public PlanSourcingQuery(
        Guid orderLineId,
        SourcingDemandDto demand,
        IReadOnlyDictionary<Guid, decimal> groupAvailability,
        Guid orderingCompanyId,
        IReadOnlyList<Guid> proximityRank)
    {
        OrderLineId = orderLineId;
        Demand = demand ?? throw new ArgumentNullException(nameof(demand));
        GroupAvailability = groupAvailability ?? throw new ArgumentNullException(nameof(groupAvailability));
        OrderingCompanyId = orderingCompanyId;
        ProximityRank = proximityRank ?? throw new ArgumentNullException(nameof(proximityRank));
    }

    public Guid OrderLineId { get; }
    public SourcingDemandDto Demand { get; }
    public IReadOnlyDictionary<Guid, decimal> GroupAvailability { get; }
    public Guid OrderingCompanyId { get; }
    public IReadOnlyList<Guid> ProximityRank { get; }
}
