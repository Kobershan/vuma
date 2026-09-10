using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Loyalty;

/// <summary>Verifies the Stage 20 loyalty migration is reversible on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LoyaltyMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage20_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910040430_Stage20_Loyalty").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'loyalty'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().BeEquivalentTo(
            ["members", "rewards", "settings", "tiers", "transactions"],
            options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260910035414_Stage19_Crm").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'loyalty'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
