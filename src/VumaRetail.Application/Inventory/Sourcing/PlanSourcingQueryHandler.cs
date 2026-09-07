namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Contracts.Inventory.Sourcing;

/// <summary>
/// Handler for PlanSourcingQuery: dry-run sourcing without side effects.
/// </summary>
public sealed class PlanSourcingQueryHandler : IQueryHandler<PlanSourcingQuery, SourcingPlanDto>
{
    private readonly ICompanySourcingStrategy _strategy;
    private readonly IItemRepository _itemRepository;

    public PlanSourcingQueryHandler(
        ICompanySourcingStrategy strategy,
        IItemRepository itemRepository)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _itemRepository = itemRepository ?? throw new ArgumentNullException(nameof(itemRepository));
    }

    public async Task<SourcingPlanDto> HandleAsync(PlanSourcingQuery query, CancellationToken ct)
    {
        // Resolve item reference
        var item = await _itemRepository.FindActiveByCodeAsync(query.Demand.ItemCode, ct)
            ?? throw new InventoryRuleException(InventoryRuleCode.ITEM_NOT_FOUND, $"Item {query.Demand.ItemCode} not found.");

        var itemReference = new StockItemReference(item.Id, query.Demand.VariantCode);

        // Build demand line
        var demandLine = new SourcingDemandLine(
            itemReference,
            query.Demand.Quantity,
            query.Demand.UnitPrice,
            query.Demand.CurrencyCode);

        // Plan sourcing
        var plan = _strategy.Plan(
            demandLine,
            query.OrderLineId,
            query.GroupAvailability,
            query.OrderingCompanyId,
            query.ProximityRank);

        // Convert to DTO
        var allocations = plan.Lines
            .Select(line => new SourcingAllocationDto(
                line.CompanyId,
                line.CompanyCode,
                line.Allocation.LocationId,
                line.Allocation.PlannedQuantity,
                line.Allocation.BackorderedQuantity))
            .ToList();

        return new SourcingPlanDto(
            plan.OrderLineId,
            plan.Demand.Quantity,
            plan.TotalPlanned,
            plan.TotalBackordered,
            allocations);
    }
}
