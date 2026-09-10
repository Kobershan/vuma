using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Manufacturing;

/// <summary>Verifies the Stage 16 manufacturing migration against real PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ManufacturingMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage16_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910170012_Stage16_BomSetup").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().ContainSingle().Which.Should().Be("bills_of_materials");

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
