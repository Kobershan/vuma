namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// A complete sourcing plan for a demand line: which companies/locations will supply it, in what quantities,
/// and how much is backordered. Built from the stale-by-construction group projection; meant to be planned
/// (dry run) before commit.
/// </summary>
public sealed class SourcingPlan
{
    private readonly List<SourcingPlanLine> _lines = [];

    public SourcingPlan(Guid orderLineId, SourcingDemandLine demand)
    {
        if (orderLineId == Guid.Empty) throw new ArgumentException("Order line ID is required.", nameof(orderLineId));
        OrderLineId = orderLineId;
        Demand = demand ?? throw new ArgumentNullException(nameof(demand));
    }

    /// <summary>
    /// The order line being sourced.
    /// </summary>
    public Guid OrderLineId { get; }

    /// <summary>
    /// The demand (quantity, price, currency).
    /// </summary>
    public SourcingDemandLine Demand { get; }

    /// <summary>
    /// Allocations per company/location.
    /// </summary>
    public IReadOnlyList<SourcingPlanLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Total quantity allocated (sum of PlannedQuantity across all lines).
    /// </summary>
    public decimal TotalPlanned => _lines.Sum(x => x.Allocation.PlannedQuantity);

    /// <summary>
    /// Total quantity backordered (sum of BackorderedQuantity across all lines).
    /// </summary>
    public decimal TotalBackordered => _lines.Sum(x => x.Allocation.BackorderedQuantity);

    /// <summary>
    /// Total covered: TotalPlanned + TotalBackordered.
    /// Should equal Demand.Quantity.
    /// </summary>
    public decimal TotalCovered => TotalPlanned + TotalBackordered;

    /// <summary>
    /// Add an allocation to the plan.
    /// </summary>
    public void AddAllocation(Guid companyId, Guid locationId, decimal plannedQuantity, decimal backorderedQuantity, string companyCode)
    {
        if (TotalCovered >= Demand.Quantity)
            throw new InvalidOperationException("Plan is already complete; cannot add more allocations.");

        var allocation = new SourcingAllocation(companyId, locationId, plannedQuantity, backorderedQuantity);
        _lines.Add(new SourcingPlanLine(companyId, allocation, companyCode));
    }

    /// <summary>
    /// Represents one line in the plan: company, allocation, company code for sorting.
    /// </summary>
    public sealed class SourcingPlanLine
    {
        public SourcingPlanLine(Guid companyId, SourcingAllocation allocation, string companyCode)
        {
            CompanyId = companyId;
            Allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
            CompanyCode = companyCode ?? throw new ArgumentNullException(nameof(companyCode));
        }

        public Guid CompanyId { get; }
        public SourcingAllocation Allocation { get; }
        public string CompanyCode { get; }
    }
}
