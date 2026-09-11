using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>Real PostgreSQL checks for the serialisable Stage 06d credit ledger.</summary>
[Trait("Category", "Integration")]
[Trait("Stage", "06D")]
[Collection(PostgresCollection.Name)]
public sealed class GroupCreditServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Parallel_holds_cannot_both_spend_the_last_group_credit_and_confirmation_replays_once()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid companyA = UuidV7.NewGuid();
        Guid companyB = UuidV7.NewGuid();
        Guid groupId;

        await using (VumaRegistryDbContext seed = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId)))
        {
            var group = new CreditGroup(tenantId, "Shared credit", "Receivable", 5_000m, "ZAR");
            groupId = group.Id;
            seed.AddRange(group,
                new CreditGroupMember(group.Id, companyA, null, tenantId),
                new CreditGroupMember(group.Id, companyB, null, tenantId));
            await seed.SaveChangesAsync();
        }

        await using VumaRegistryDbContext registryA = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        await using VumaRegistryDbContext registryB = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        ICompanyLinkService links = Substitute.For<ICompanyLinkService>();
        links.RequireLink(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var clock = new TestClock();
        var serviceA = new GroupCreditService(registryA, clock, links);
        var serviceB = new GroupCreditService(registryB, clock, links);

        Task<HoldResult> first = serviceA.TryHoldAsync(tenantId, groupId, companyA, 4_000m, "ZAR", "DOC-A", TimeSpan.FromHours(1));
        Task<HoldResult> second = serviceB.TryHoldAsync(tenantId, groupId, companyB, 4_000m, "ZAR", "DOC-B", TimeSpan.FromHours(1));
        HoldResult[] results = await Task.WhenAll(first, second);

        results.Count(result => result.Success).Should().Be(1);
        HoldResult held = results.Single(result => result.Success);

        await serviceA.ConfirmHoldAsync(held.HoldId);
        await serviceA.ConfirmHoldAsync(held.HoldId);

        await using VumaRegistryDbContext verified = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        (await verified.CreditExposureEntries.CountAsync(entry => entry.CreditGroupId == groupId)).Should().Be(1);
        (await verified.CreditExposureEntries.SingleAsync(entry => entry.CreditGroupId == groupId)).Amount.Should().Be(4_000m);
        (await verified.CreditHolds.CountAsync(hold => hold.CreditGroupId == groupId)).Should().Be(1);
    }

    [Fact]
    public async Task Unconfirmed_hold_expires_and_returns_credit()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid companyId = UuidV7.NewGuid();
        var clock = new TestClock();

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(connectionString, TestTenantContext.For(tenantId));
        var group = new CreditGroup(tenantId, "Expiry", "Receivable", 5_000m, "ZAR");
        registry.AddRange(group, new CreditGroupMember(group.Id, companyId, null, tenantId));
        await registry.SaveChangesAsync();

        ICompanyLinkService links = Substitute.For<ICompanyLinkService>();
        var service = new GroupCreditService(registry, clock, links);
        HoldResult held = await service.TryHoldAsync(tenantId, group.Id, companyId, 4_000m, "ZAR", "DOC-EXP", TimeSpan.FromMinutes(10));
        (await service.GetPositionAsync(tenantId, group.Id)).Available.Should().Be(1_000m);

        clock.Advance(TimeSpan.FromMinutes(11));
        (await service.ExpireHoldsAsync(tenantId)).Should().Be(1);
        (await service.GetPositionAsync(tenantId, group.Id)).Available.Should().Be(5_000m);
        (await service.GetOutstandingHoldsAsync(tenantId)).Should().BeEmpty();
        held.Success.Should().BeTrue();
    }
}
