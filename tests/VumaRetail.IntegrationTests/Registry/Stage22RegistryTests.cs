using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>Exercises the Stage 22 registry boundary against the real PostgreSQL schema.</summary>
[Trait("Category", "Integration")]
[Trait("Stage", "22")]
[Collection(PostgresCollection.Name)]
public sealed class Stage22RegistryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Business_hierarchy_and_low_value_transfer_complete_without_cross_tenant_leakage()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid otherTenantId = UuidV7.NewGuid();
        Guid businessId = UuidV7.NewGuid();

        await using (VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            connectionString, TestTenantContext.For(tenantId)))
        {
            Company holding = Company.Create(tenantId, "HOLD", "Holding", "Holding", "ZAR", "en-ZA", "HO-");
            Company store = Company.Create(tenantId, "STORE", "Store", "Store", "ZAR", "en-ZA", "ST-");
            Guid holdingCompanyId = holding.Id;
            Guid storeCompanyId = store.Id;
            registry.Companies.AddRange(holding, store);
            await registry.SaveChangesAsync();

            var service = new Stage22RegistryService(registry, TestTenantContext.For(tenantId), new TestClock());
            await service.CreateBusinessAsync(businessId, "Retail Group", BusinessType.GroupBusiness, 10_000m,
                TransferCostingMethod.SenderCost, DiscrepancyOwner.Sender, "ST-");
            await service.AddCompanyAsync(businessId, holdingCompanyId);
            await service.AddCompanyAsync(businessId, storeCompanyId);
            GroupHierarchyNode holdingNode = await service.AddHierarchyNodeAsync(
                businessId, holdingCompanyId, HierarchyNodeType.HeadOffice, OwnershipType.Owned, null, null, true);
            await service.AddHierarchyNodeAsync(
                businessId, storeCompanyId, HierarchyNodeType.Store, OwnershipType.Owned, holdingNode.Id, "ST-01", true);

            StockTransferRequest transfer = await service.CreateTransferAsync(
                holdingCompanyId, holdingCompanyId, storeCompanyId, holdingCompanyId, 100m, false);
            transfer.Status.Should().Be(TransferStatus.Checked);

            string[] actions = ["accept", "reserve", "pick", "ship"];
            foreach (string action in actions)
            {
                transfer = await service.TransitionTransferAsync(transfer.Id, action);
            }

            transfer.Status.Should().Be(TransferStatus.InTransit);
            transfer = await service.TransitionTransferAsync(transfer.Id, "receive", quantity: 8m);
            transfer = await service.TransitionTransferAsync(transfer.Id, "reconcile", requestedQuantity: 10m, reason: "Two units damaged in transit.");
            transfer.Status.Should().Be(TransferStatus.Reconciled);
            transfer.DiscrepancyQuantity.Should().Be(-2m);

            (await registry.BusinessRegistrations.CountAsync()).Should().Be(1);
            (await registry.StockTransferRequests.CountAsync()).Should().Be(1);
        }

        await using (VumaRegistryDbContext otherTenantRegistry = TestDbContextFactory.ForRegistry(
            connectionString, TestTenantContext.For(otherTenantId)))
        {
            Company foreignCompany = Company.Create(otherTenantId, "FOREIGN", "Foreign", "Foreign", "ZAR", "en-ZA", "FR-");
            otherTenantRegistry.Companies.Add(foreignCompany);
            await otherTenantRegistry.SaveChangesAsync();
        }

        await using VumaRegistryDbContext isolatedRegistry = TestDbContextFactory.ForRegistry(
            connectionString, TestTenantContext.For(tenantId));
        IStage22RegistryService isolatedService = new Stage22RegistryService(
            isolatedRegistry, TestTenantContext.For(tenantId), new TestClock());

        await FluentActions.Invoking(() => isolatedService.AddCompanyAsync(businessId, UuidV7.NewGuid()))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Threshold_and_ownership_rules_are_enforced_before_transfer_creation()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid businessId = UuidV7.NewGuid();

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            connectionString, TestTenantContext.For(tenantId));
        Company holding = Company.Create(tenantId, "HOLD", "Holding", "Holding", "ZAR", "en-ZA", "HO-");
        Company receiver = Company.Create(tenantId, "STORE", "Store", "Store", "ZAR", "en-ZA", "ST-");
        Guid holdingCompanyId = holding.Id;
        Guid receiverCompanyId = receiver.Id;
        registry.Companies.AddRange(holding, receiver);
        await registry.SaveChangesAsync();

        var service = new Stage22RegistryService(registry, TestTenantContext.For(tenantId), new TestClock());
        await service.CreateBusinessAsync(businessId, "Retail Group", BusinessType.GroupBusiness, 100m,
            TransferCostingMethod.GroupStandardCost, DiscrepancyOwner.HeldForReview, "ST-");
        await service.AddCompanyAsync(businessId, holdingCompanyId);
        await service.AddCompanyAsync(businessId, receiverCompanyId);
        GroupHierarchyNode holdingNode = await service.AddHierarchyNodeAsync(
            businessId, holdingCompanyId, HierarchyNodeType.HeadOffice, OwnershipType.Owned, null, null, true);
        await service.AddHierarchyNodeAsync(
            businessId, receiverCompanyId, HierarchyNodeType.Store, OwnershipType.Owned, holdingNode.Id, "ST-01", true);

        StockTransferRequest transfer = await service.CreateTransferAsync(
            holdingCompanyId, holdingCompanyId, receiverCompanyId, holdingCompanyId, 100m, false);
        transfer.Status.Should().Be(TransferStatus.RegionalApprovalPending);

        await FluentActions.Invoking(() => service.TransitionTransferAsync(transfer.Id, "accept"))
            .Should().ThrowAsync<InvalidOperationException>();

        await service.TransitionTransferAsync(transfer.Id, "approve");
        await service.TransitionTransferAsync(transfer.Id, "accept");

        Guid franchiseCompanyId = UuidV7.NewGuid();
        Company franchise = Company.Create(tenantId, "FRAN", "Franchise", "Franchise", "ZAR", "en-ZA", "FR-");
        franchiseCompanyId = franchise.Id;
        registry.Companies.Add(franchise);
        await registry.SaveChangesAsync();
        await service.AddCompanyAsync(businessId, franchiseCompanyId);
        await service.AddHierarchyNodeAsync(
            businessId, franchiseCompanyId, HierarchyNodeType.Store, OwnershipType.Franchised, holdingNode.Id, "ST-02", true);

        await FluentActions.Invoking(() => service.CreateTransferAsync(
                holdingCompanyId, holdingCompanyId, franchiseCompanyId, holdingCompanyId, 1m, false))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
