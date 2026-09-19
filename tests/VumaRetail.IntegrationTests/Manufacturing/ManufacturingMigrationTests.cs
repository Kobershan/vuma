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
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913053229_Stage17_ProductionExecutionRecords");

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'production_orders'
            """).ToListAsync();
        columns.Should().Contain(["bill_of_materials_id", "material_issues", "output_receipts", "scrap_records"]);

        await context.Database.MigrateAsync("20260912215016_Stage17_ProductionOrderLifecycle");

        IReadOnlyList<string> afterExecutionDown = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'production_orders'
            """).ToListAsync();
        afterExecutionDown.Should().NotContain(["bill_of_materials_id", "material_issues", "output_receipts", "scrap_records"]);

        await context.Database.MigrateAsync("20260910170958_Stage16_RoutingSteps");
        IReadOnlyList<string> afterLifecycleDown = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync();
        afterLifecycleDown.Should().ContainSingle().Which.Should().Be("bills_of_materials");
    }

    [Fact]
    public async Task Stage18_quality_migrations_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913122618_Stage18RecallCases");

        IReadOnlyList<string> qualityTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'quality'
            """).ToListAsync();
        qualityTables.Should().Contain(["quality_holds", "inspection_plans", "inspection_results", "non_conformances", "quality_certificates", "recall_cases"]);

        await context.Database.MigrateAsync("20260913121916_Stage18QualityCertificates");

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'quality'
            """).ToListAsync();
        remainingTables.Should().NotContain("recall_cases");
        remainingTables.Should().Contain("quality_certificates");
    }

    [Fact]
    public async Task Stage16_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910170958_Stage16_RoutingSteps");

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync();

        tables.Should().ContainSingle().Which.Should().Be("bills_of_materials");

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'manufacturing' AND table_name = 'bills_of_materials'
            """).ToListAsync();
        columns.Should().Contain(["lines", "routing_steps"]);

        await context.Database.MigrateAsync("20260909032943_Stage13b_PickingWavesStaging");

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'manufacturing'
            """).ToListAsync();

        remainingTables.Should().BeEmpty();
    }
}
