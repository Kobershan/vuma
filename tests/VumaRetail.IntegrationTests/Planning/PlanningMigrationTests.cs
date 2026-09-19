using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Planning;

/// <summary>Verifies the Stage 15 migration creates and reverses its planning schema on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PlanningMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage13b_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging");

        IReadOnlyList<string> createdTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'warehouse'
            """).ToListAsync();
        createdTables.Should().Contain("count_schedules");
        createdTables.Should().Contain("pick_wave_line_breakdowns");

        await context.Database.MigrateAsync("20260817061003_Warehouse");

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'warehouse'
            """).ToListAsync();
        remainingTables.Should().NotContain("count_schedules");
        remainingTables.Should().NotContain("pick_wave_line_breakdowns");
    }

    [Fact]
    public async Task Stage15_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260909131616_Stage15_Planning");

        IReadOnlyList<string> createdTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'planning'
            ORDER BY table_name
            """).ToListAsync();

        createdTables.Should().Contain("demand_forecasts");
        createdTables.Should().Contain("replenishment_suggestions");
        createdTables.Should().HaveCount(9);

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging");

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'planning'
            """).ToListAsync();

        remainingTables.Should().BeEmpty();
    }
}
