using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Domain.Primitives;
using ZenithRetail.IntegrationTests.Harness;

namespace ZenithRetail.IntegrationTests.Persistence;

/// <summary>
/// The two global query filters: soft delete (§7 rule 8) and tenant isolation (§7 rule 3, R8).
/// </summary>
/// <remarks>
/// Both are applied by convention to every entity rather than declared per configuration. The failure
/// mode of "somebody forgot one" is a tenant reading another tenant's trading data, which is not a
/// bug anyone wants to find in production, so these are asserted against a real database with two
/// tenants' rows genuinely present in the same table.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class QueryFilterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_query_returns_only_the_current_tenants_rows()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantA = UuidV7.NewGuid();
        Guid tenantB = UuidV7.NewGuid();

        await using (ZenithRetailDbContext seed = TestDbContextFactory.For(connectionString))
        {
            seed.Add(Store.Create(tenantA, "AAA", "A's store"));
            seed.Add(Store.Create(tenantB, "BBB", "B's store"));
            await seed.SaveChangesAsync();
        }

        await using ZenithRetailDbContext scoped =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(tenantA));

        List<Store> visible = await scoped.Stores.ToListAsync();

        visible.Should().ContainSingle();
        visible[0].Code.Should().Be("AAA");
    }

    [Fact]
    public async Task An_unresolved_tenant_sees_nothing_rather_than_everything()
    {
        // Before login and during activation there is no tenant. Returning every tenant's rows
        // because the filter value happens to be Guid.Empty would be the worst possible default.
        string connectionString = await fixture.CreateDatabaseAsync();

        await using (ZenithRetailDbContext seed = TestDbContextFactory.For(connectionString))
        {
            seed.Add(Store.Create(UuidV7.NewGuid(), "AAA", "A's store"));
            await seed.SaveChangesAsync();
        }

        await using ZenithRetailDbContext unresolved =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(Guid.Empty));

        (await unresolved.Stores.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_explicit_bypass_crosses_the_tenant_boundary()
    {
        // The sync receiver at the cloud tier and the backup job legitimately need this. It has to
        // work, and it has to be the only way across.
        string connectionString = await fixture.CreateDatabaseAsync();

        await using (ZenithRetailDbContext seed = TestDbContextFactory.For(connectionString))
        {
            seed.Add(Store.Create(UuidV7.NewGuid(), "AAA", "A's store"));
            seed.Add(Store.Create(UuidV7.NewGuid(), "BBB", "B's store"));
            await seed.SaveChangesAsync();
        }

        TestTenantContext tenantContext = TestTenantContext.For(UuidV7.NewGuid());
        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString, tenant: tenantContext);

        (await context.Stores.ToListAsync()).Should().BeEmpty();

        using (tenantContext.BypassTenantFilter("sync receiver"))
        {
            (await context.Stores.ToListAsync()).Should().HaveCount(2);
        }

        (await context.Stores.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_soft_deleted_row_disappears_from_queries_but_not_from_the_table()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        await using (ZenithRetailDbContext writer = TestDbContextFactory.For(connectionString))
        {
            Store store = Store.Create(tenantId, "JHB01", "Johannesburg");
            writer.Add(store);
            await writer.SaveChangesAsync();

            writer.Remove(store);
            await writer.SaveChangesAsync();
        }

        await using ZenithRetailDbContext reader = TestDbContextFactory.For(connectionString);

        (await reader.Stores.ToListAsync()).Should().BeEmpty();
        (await reader.Stores.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle();

        // The physical row is the point of rule 8. A query filter that hid a DELETE would be theatre.
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*), count(deleted_at) FROM platform.stores";

        await using NpgsqlDataReader dbReader = await command.ExecuteReaderAsync();
        await dbReader.ReadAsync();

        dbReader.GetInt64(0).Should().Be(1);
        dbReader.GetInt64(1).Should().Be(1);
    }

    [Fact]
    public async Task Remove_is_rewritten_as_a_soft_delete_rather_than_issuing_a_DELETE()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        TestClock clock = new();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString, clock);

        Store store = Store.Create(tenantId, "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        clock.Advance(TimeSpan.FromHours(3));
        context.Remove(store);
        await context.SaveChangesAsync();

        store.IsDeleted.Should().BeTrue();
        store.DeletedAt.Should().Be(clock.UtcNow);
        store.DeletedBy.Should().Be("user:test");
    }

    [Fact]
    public async Task A_soft_deleted_store_code_can_be_used_again()
    {
        // The unique index is filtered on deleted_at IS NULL. Without that filter, closing a store
        // would reserve its code forever, which is a surprising consequence of rule 8 for a user.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        Store original = Store.Create(tenantId, "JHB01", "Johannesburg");
        context.Add(original);
        await context.SaveChangesAsync();

        context.Remove(original);
        await context.SaveChangesAsync();

        context.Add(Store.Create(tenantId, "JHB01", "Johannesburg, relocated"));

        await context.Invoking(c => c.SaveChangesAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Two_live_stores_cannot_share_a_code_within_one_tenant()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        context.Add(Store.Create(tenantId, "JHB01", "Johannesburg"));
        await context.SaveChangesAsync();

        context.Add(Store.Create(tenantId, "JHB01", "Johannesburg again"));

        await context.Invoking(c => c.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();
    }
}
