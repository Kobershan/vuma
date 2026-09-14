using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Manufacturing;

/// <summary>Verifies the Stage 16 manufacturing migration against real PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ManufacturingMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage17_production_migrations_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913053229_Stage17_ProductionExecutionRecords").ConfigureAwait(false);

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'production_orders'
            """).ToListAsync().ConfigureAwait(false);
        columns.Should().Contain(["bill_of_materials_id", "material_issues", "output_receipts", "scrap_records"]);

        await context.Database.MigrateAsync("20260912215016_Stage17_ProductionOrderLifecycle").ConfigureAwait(false);

        IReadOnlyList<string> afterExecutionDown = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'production_orders'
            """).ToListAsync().ConfigureAwait(false);
        afterExecutionDown.Should().NotContain(["bill_of_materials_id", "material_issues", "output_receipts", "scrap_records"]);

        await context.Database.MigrateAsync("20260910170958_Stage16_RoutingSteps").ConfigureAwait(false);
        IReadOnlyList<string> afterLifecycleDown = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync().ConfigureAwait(false);
        afterLifecycleDown.Should().ContainSingle().Which.Should().Be("bills_of_materials");
    }

    [Fact]
    public async Task Stage18_quality_migrations_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913122618_Stage18RecallCases").ConfigureAwait(false);

        IReadOnlyList<string> qualityTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'quality'
            """).ToListAsync().ConfigureAwait(false);
        qualityTables.Should().Contain(["quality_holds", "inspection_plans", "inspection_results", "non_conformances", "quality_certificates", "recall_cases"]);

        await context.Database.MigrateAsync("20260913121916_Stage18QualityCertificates").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'quality'
            """).ToListAsync().ConfigureAwait(false);
        remainingTables.Should().NotContain("recall_cases");
        remainingTables.Should().Contain("quality_certificates");
    }

    [Fact]
    public async Task Stage16_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910170958_Stage16_RoutingSteps").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().ContainSingle().Which.Should().Be("bills_of_materials");

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'bills_of_materials'
            """).ToListAsync().ConfigureAwait(false);
        columns.Should().Contain(["lines", "routing_steps"]);

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
