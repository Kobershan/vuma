using Microsoft.EntityFrameworkCore;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.CustomerAccounts;

/// <summary>
/// Stage 10b stokvels against real PostgreSQL: the seven tables migrate, rows round-trip with
/// their company identity, and the offline-replay unique index refuses a duplicate receipt.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StokvelPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stokvel_rows_round_trip_with_their_company()
    {
        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        var clock = new TestClock(Now);
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:stokvel");

        Guid tenantId = UuidV7.NewGuid();
        Guid storeId = UuidV7.NewGuid();
        Guid companyId = UuidV7.NewGuid();
        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        await using var context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        var groups = new StokvelGroupRepository(context);
        var ledger = new StokvelContributionRepository(context);
        var payouts = new StokvelPayoutRepository(context);

        var group = StokvelGroup.Create(
            tenantId, storeId, "STK-000001", "Grocery", StokvelType.GroceryHamper,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            storeId, companyId);
        groups.AddGroup(group);
        var member = StokvelMember.Join(
            tenantId, storeId, group.Id, UuidV7.NewGuid(),
            MemberRole.Treasurer, new Money(500m, "ZAR"), Now);
        groups.AddMember(member);
        var basket = HamperBasket.Create(
            tenantId, storeId, group.Id, "December hamper", new Money(450m, "ZAR"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24),
            UuidV7.NewGuid(), companyId);
        basket.AddLine(HamperBasketLine.Create(
            tenantId, storeId, basket.Id, UuidV7.NewGuid(), null, 2m, "EA"));
        groups.AddBasket(basket);
        ledger.AddContribution(StokvelContribution.Record(
            tenantId, storeId, group.Id, member.Id, new Money(500m, "ZAR"),
            "RCPT-1", Now, "Till"));
        ledger.AddBenefit(StokvelBenefitAllocation.Allocate(
            tenantId, storeId, group.Id, member.Id, new Money(30m, "ZAR"),
            "time-weighted 2026 cycle, weight 1/1", Now));
        var payout = StokvelPayout.Request(
            tenantId, storeId, group.Id, member.Id, StokvelPayoutKind.Cash,
            new Money(100m, "ZAR"), null, Now);
        payouts.Add(payout);
        await context.CommitAsync().ConfigureAwait(false);

        StokvelGroup? reloaded = await groups.FindByNumberAsync("STK-000001");
        reloaded.Should().NotBeNull();
        reloaded!.CompanyId.Should().Be(companyId);
        (await groups.ListMembersAsync(group.Id)).Should().ContainSingle();
        (await groups.ListBasketsAsync(group.Id)).Should().ContainSingle();
        (await ledger.ListForMemberAsync(member.Id)).Should().ContainSingle();
        (await ledger.ListBenefitsForMemberAsync(member.Id)).Should().ContainSingle();
        (await payouts.ListForMemberAsync(member.Id)).Should().ContainSingle();

        // The offline-replay guard at the storage layer: the same receipt twice is one row.
        ledger.AddContribution(StokvelContribution.Record(
            tenantId, storeId, group.Id, member.Id, new Money(500m, "ZAR"),
            "RCPT-1", Now, "Till"));
        Func<Task> committing = () => context.CommitAsync();
        await committing.Should().ThrowAsync<DbUpdateException>();
    }
}
