using Microsoft.EntityFrameworkCore;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Domain.Primitives;
using ZenithRetail.IntegrationTests.Harness;

namespace ZenithRetail.IntegrationTests.Persistence;

/// <summary>
/// Optimistic concurrency on the <c>row_version</c> token (ADR-035).
/// </summary>
/// <remarks>
/// This matters more here than in an ordinary application. Sync replays writes that were captured on
/// a terminal minutes or days earlier (ADR-006), so "the row changed under me" is a normal event
/// rather than an unlucky race, and it has to surface as a conflict rather than as a lost update.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task The_second_writer_to_a_changed_row_is_refused()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid storeId;

        await using (ZenithRetailDbContext seed = TestDbContextFactory.For(connectionString))
        {
            Store store = Store.Create(tenantId, "JHB01", "Johannesburg");
            seed.Add(store);
            await seed.SaveChangesAsync();
            storeId = store.Id;
        }

        await using ZenithRetailDbContext first = TestDbContextFactory.For(connectionString);
        await using ZenithRetailDbContext second = TestDbContextFactory.For(connectionString);

        Store firstCopy = await first.Stores.SingleAsync(s => s.Id == storeId);
        Store secondCopy = await second.Stores.SingleAsync(s => s.Id == storeId);

        firstCopy.SetDetails("Renamed by the first writer", null);
        await first.SaveChangesAsync();

        secondCopy.SetDetails("Renamed by the second writer", null);

        await second.Invoking(context => context.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Every_write_replaces_the_concurrency_token()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        Store store = Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        byte[] afterInsert = store.RowVersion;

        store.SetDetails("Johannesburg Central", null);
        await context.SaveChangesAsync();

        store.RowVersion.Should().NotEqual(afterInsert);
    }
}
