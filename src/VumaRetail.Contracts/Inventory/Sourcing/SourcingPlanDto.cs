namespace VumaRetail.Contracts.Inventory.Sourcing;

public sealed class SourcingPlanDto
{
    public SourcingPlanDto(
        Guid orderLineId,
        decimal demandedQuantity,
        decimal plannedQuantity,
        decimal backorderedQuantity,
        List<SourcingAllocationDto> allocations)
    {
        OrderLineId = orderLineId;
        DemandedQuantity = demandedQuantity;
        PlannedQuantity = plannedQuantity;
        BackorderedQuantity = backorderedQuantity;
        Allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
    }

    public Guid OrderLineId { get; }
    public decimal DemandedQuantity { get; }
    public decimal PlannedQuantity { get; }
    public decimal BackorderedQuantity { get; }
    public List<SourcingAllocationDto> Allocations { get; }
}

public sealed class SourcingAllocationDto
{
    public SourcingAllocationDto(
        Guid companyId,
        string companyCode,
        Guid locationId,
        decimal plannedQuantity,
        decimal backorderedQuantity)
    {
        CompanyId = companyId;
        CompanyCode = companyCode ?? throw new ArgumentNullException(nameof(companyCode));
        LocationId = locationId;
        PlannedQuantity = plannedQuantity;
        BackorderedQuantity = backorderedQuantity;
    }

    public Guid CompanyId { get; }
    public string CompanyCode { get; }
    public Guid LocationId { get; }
    public decimal PlannedQuantity { get; }
    public decimal BackorderedQuantity { get; }
}
