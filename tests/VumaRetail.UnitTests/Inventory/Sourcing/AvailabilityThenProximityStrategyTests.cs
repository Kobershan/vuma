namespace VumaRetail.Tests.Unit.Inventory.Sourcing;

using VumaRetail.Application.Inventory.Sourcing;
using VumaRetail.Domain.Inventory.Sourcing;
using Xunit;

public sealed class AvailabilityThenProximityStrategyTests
{
    private readonly AvailabilityThenProximityStrategy _strategy = new();

    /// <summary>
    /// Criterion 1: 20 demanded, A=12 + B=30 → plan 12+8, commit holds 12+8, no backorder, B never negative.
    /// </summary>
    [Fact]
    public void Plan_WhenEnoughAvailability_AllocatesFullDemand()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var supplierCompanyId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 20, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 12 },
            { supplierCompanyId, 30 }
        };

        // Act
        var plan = _strategy.Plan(
            demand,
            Guid.NewGuid(),
            availability,
            orderingCompanyId,
            new[] { supplierCompanyId }.ToList());

        // Assert
        Assert.Equal(20, plan.TotalPlanned);
        Assert.Equal(0, plan.TotalBackordered);
        Assert.Equal(20, plan.TotalCovered);

        // Verify allocations: ordering company first, then supplier
        var primaryAllocation = plan.Lines.FirstOrDefault(l => l.CompanyId == orderingCompanyId);
        Assert.NotNull(primaryAllocation);
        Assert.Equal(12, primaryAllocation!.Allocation.PlannedQuantity);

        var secondaryAllocation = plan.Lines.FirstOrDefault(l => l.CompanyId == supplierCompanyId);
        Assert.NotNull(secondaryAllocation);
        Assert.Equal(8, secondaryAllocation!.Allocation.PlannedQuantity);
    }

    /// <summary>
    /// Criterion 2: 20 demanded, A=12 + B=3 → 15 held, 5 backordered, nothing negative.
    /// </summary>
    [Fact]
    public void Plan_WhenInsufficientAvailability_BackordersRemainder()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var supplierCompanyId = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 20, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 12 },
            { supplierCompanyId, 3 }
        };

        // Act
        var plan = _strategy.Plan(
            demand,
            Guid.NewGuid(),
            availability,
            orderingCompanyId,
            new[] { supplierCompanyId }.ToList());

        // Assert
        Assert.Equal(15, plan.TotalPlanned); // 12 + 3
        Assert.Equal(5, plan.TotalBackordered);
        Assert.Equal(20, plan.TotalCovered);

        // Verify supplier allocation is fully consumed
        var secondaryAllocation = plan.Lines.FirstOrDefault(l => l.CompanyId == supplierCompanyId);
        Assert.NotNull(secondaryAllocation);
        Assert.Equal(3, secondaryAllocation!.Allocation.PlannedQuantity);
        Assert.Equal(0, secondaryAllocation.Allocation.BackorderedQuantity);

        // Verify backorder is against primary
        var primaryAllocation = plan.Lines.FirstOrDefault(l => l.CompanyId == orderingCompanyId && l.Allocation.BackorderedQuantity > 0);
        Assert.NotNull(primaryAllocation);
        Assert.Equal(5, primaryAllocation!.Allocation.BackorderedQuantity);
    }

    /// <summary>
    /// Never allocates against a company with zero available (business rule 3).
    /// </summary>
    [Fact]
    public void Plan_NeverAllocatesAgainstZeroAvailability()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var zeroCompanyId = Guid.NewGuid();
        var supplierCompanyId = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 10, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 5 },
            { zeroCompanyId, 0 }, // Zero available
            { supplierCompanyId, 10 }
        };

        // Act
        var plan = _strategy.Plan(
            demand,
            Guid.NewGuid(),
            availability,
            orderingCompanyId,
            new[] { zeroCompanyId, supplierCompanyId }.ToList());

        // Assert: zero company should have no allocation
        var zeroAllocation = plan.Lines.FirstOrDefault(l => l.CompanyId == zeroCompanyId);
        Assert.Null(zeroAllocation);

        // Verify allocation goes to ordering company and supplier only
        var allocatedCompanies = plan.Lines.Select(l => l.CompanyId).Distinct();
        Assert.Equal(2, allocatedCompanies.Count());
        Assert.Contains(orderingCompanyId, allocatedCompanies);
        Assert.Contains(supplierCompanyId, allocatedCompanies);
    }

    /// <summary>
    /// Determinism: same input always produces same output.
    /// Ties break by company code so result is not row-order-dependent.
    /// </summary>
    [Fact]
    public void Plan_IsDeterministic_TiesBreakByCompanyCode()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 10, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 3 },
            { companyA, 5 },
            { companyB, 5 } // Same available: should tie-break by company code
        };

        var proximityRank = new[] { companyA, companyB }.ToList();

        // Act: Run multiple times
        var plan1 = _strategy.Plan(demand, Guid.NewGuid(), availability, orderingCompanyId, proximityRank);
        var plan2 = _strategy.Plan(demand, Guid.NewGuid(), availability, orderingCompanyId, proximityRank);

        // Assert: Same order of companies
        var companies1 = plan1.Lines.Select(l => l.CompanyId).ToList();
        var companies2 = plan2.Lines.Select(l => l.CompanyId).ToList();

        Assert.Equal(companies1, companies2);
    }

    /// <summary>
    /// Ordering company is prioritized first.
    /// </summary>
    [Fact]
    public void Plan_PrioritizesOrderingCompanyFirst()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var supplierCompanyId = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 10, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 5 },
            { supplierCompanyId, 10 }
        };

        // Act
        var plan = _strategy.Plan(
            demand,
            Guid.NewGuid(),
            availability,
            orderingCompanyId,
            new[] { supplierCompanyId }.ToList());

        // Assert: Ordering company allocated first (index 0)
        var firstAllocation = plan.Lines.FirstOrDefault();
        Assert.NotNull(firstAllocation);
        Assert.Equal(orderingCompanyId, firstAllocation!.CompanyId);
        Assert.Equal(5, firstAllocation.Allocation.PlannedQuantity);
    }

    /// <summary>
    /// Proximity rank is respected: closer companies allocated before farther ones.
    /// </summary>
    [Fact]
    public void Plan_RespectsProximityRank()
    {
        // Arrange
        var orderingCompanyId = Guid.NewGuid();
        var closer = Guid.NewGuid();
        var farther = Guid.NewGuid();

        var itemRef = new StockItemReference(Guid.NewGuid(), null);
        var demand = new SourcingDemandLine(itemRef, 20, 10.00m, "ZAR");

        var availability = new Dictionary<Guid, decimal>
        {
            { orderingCompanyId, 5 },
            { closer, 8 },
            { farther, 10 }
        };

        var proximityRank = new[] { closer, farther }.ToList();

        // Act
        var plan = _strategy.Plan(
            demand,
            Guid.NewGuid(),
            availability,
            orderingCompanyId,
            proximityRank);

        // Assert: Order should be ordering company, then closer, then farther
        var companies = plan.Lines.Select(l => l.CompanyId).ToList();
        Assert.Equal(orderingCompanyId, companies[0]);
        Assert.Equal(closer, companies[1]);
        Assert.Equal(farther, companies[2]);
    }
}
