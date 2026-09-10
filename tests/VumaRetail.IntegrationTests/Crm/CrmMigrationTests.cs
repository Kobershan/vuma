using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Crm;

/// <summary>Verifies the Stage 19 CRM migration is reversible on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class CrmMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage19_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910035414_Stage19_Crm").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'crm'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().BeEquivalentTo(
            ["activities", "consents", "leads", "opportunities", "segment_members", "segments"],
            options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260909131616_Stage15_Planning").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'crm'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
