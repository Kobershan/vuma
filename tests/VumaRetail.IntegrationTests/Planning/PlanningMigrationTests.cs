using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Planning;

/// <summary>Verifies the Stage 15 migration creates and reverses its planning schema on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PlanningMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage15_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260909131616_Stage15_Planning").ConfigureAwait(false);

        IReadOnlyList<string> createdTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'planning'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        createdTables.Should().Contain("demand_forecasts");
        createdTables.Should().Contain("replenishment_suggestions");
        createdTables.Should().HaveCount(9);

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'planning'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
