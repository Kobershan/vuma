using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>Stage 06d routing collision and projection-staleness checks over PostgreSQL.</summary>
[Trait("Category", "Integration")]
[Trait("Stage", "06D")]
[Collection(PostgresCollection.Name)]
public sealed class RoutingAndGroupReadTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Collision_is_persisted_and_returned_as_multiple_candidates()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid companyA = UuidV7.NewGuid();
        Guid companyB = UuidV7.NewGuid();
        DateTimeOffset now = TestClock.DefaultStart;

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        registry.CatalogRoutingIndex.AddRange(
            Routing(tenantId, companyA, "A", "COLLIDE", now),
            Routing(tenantId, companyB, "B", "COLLIDE", now));
        await registry.SaveChangesAsync();

        ICompanyContext companyContext = Substitute.For<ICompanyContext>();
        companyContext.CompanyId.Returns(companyA);
        ICompanyDbContextFactory companyDatabases = Substitute.For<ICompanyDbContextFactory>();
        var resolver = new BarcodeResolver(registry, companyContext, companyDatabases, new TestClock(now));

        BarcodeResolution result = await resolver.ResolveAsync("COLLIDE");

        result.IsMultiple.Should().BeTrue();
        result.IsLocalFallback.Should().BeFalse();
        result.Candidates.Select(candidate => candidate.CompanyId).Should().BeEquivalentTo([companyA, companyB]);
    }

    [Fact]
    public async Task Group_read_names_a_company_with_no_projection_as_stale()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        DateTimeOffset now = TestClock.DefaultStart;

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        Company published = Company.Create(tenantId, "PUB", "Published", "Published", "ZAR", "en-ZA", "PU-");
        Company missing = Company.Create(tenantId, "MIS", "Missing", "Missing", "ZAR", "en-ZA", "MI-");
        registry.Companies.AddRange(published, missing);
        registry.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
            tenantId, published.Id, published.Code, UuidV7.NewGuid(), UuidV7.NewGuid(), null,
            10m, 2m, 1m, "EA", now));
        await registry.SaveChangesAsync();

        var store = new GroupReadStore(registry, new TestClock(now), Options.Create(new GroupAvailabilityOptions
        {
            StaleAfter = TimeSpan.FromMinutes(15),
        }));
        GroupAvailability view = await store.GetAvailabilityAsync(tenantId, [published.Id, missing.Id]);

        view.TotalAvailable.Should().Be(7m);
        view.Companies.Single(company => company.CompanyId == published.Id).IsStale.Should().BeFalse();
        view.Companies.Single(company => company.CompanyId == missing.Id).IsStale.Should().BeTrue();
        view.StaleContributorCodes.Should().ContainSingle().Which.Should().Be("MIS");
    }

    private static CatalogRoutingIndexEntry Routing(Guid tenantId, Guid companyId, string companyCode, string barcode, DateTimeOffset now)
        => new()
        {
            Id = UuidV7.NewGuid(),
            TenantId = tenantId,
            CompanyId = companyId,
            CompanyCode = companyCode,
            Barcode = barcode,
            ItemId = UuidV7.NewGuid(),
            ItemCode = $"{companyCode}-ITEM",
            Description = $"{companyCode} item",
            AsAt = now,
        };
}
