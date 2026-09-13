using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Service;

/// <summary>Verifies the Stage 23, 27 and 28 migrations can be applied and removed on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ServiceMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage23_stage27_and_stage28_migrations_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913182228_Stage28Projects").ConfigureAwait(false);

        IReadOnlyList<string> assetTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'assets'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);
        assetTables.Should().BeEquivalentTo(["asset_books", "depreciation_runs", "fixed_assets"], options => options.WithStrictOrdering());

        IReadOnlyList<string> projectTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'projects'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);
        projectTables.Should().BeEquivalentTo(["billing_milestones", "contract_variations", "project_budgets", "project_contracts", "projects"], options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260913180901_Stage27DepreciationRuns").ConfigureAwait(false);
        IReadOnlyList<string> revertedProjectTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'projects'
            """).ToListAsync().ConfigureAwait(false);
        revertedProjectTables.Should().BeEmpty();

        await context.Database.MigrateAsync("20260913174706_Stage27AssetBooks").ConfigureAwait(false);
        IReadOnlyList<string> remainingAssetTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'assets'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);
        remainingAssetTables.Should().BeEquivalentTo(["asset_books", "fixed_assets"], options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260913171313_Stage23_ServiceCustodyEvents").ConfigureAwait(false);
        IReadOnlyList<string> revertedAssetTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'assets'
            """).ToListAsync().ConfigureAwait(false);
        revertedAssetTables.Should().BeEmpty();

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'service'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);
        tables.Should().BeEquivalentTo(
            ["repair_jobs", "service_custody_events", "service_part_usages", "service_slas", "service_tickets", "warranty_claims"],
            options => options.WithStrictOrdering());

        IReadOnlyList<string> ticketColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'service' AND table_name = 'service_tickets'
            """).ToListAsync().ConfigureAwait(false);
        ticketColumns.Should().Contain("operation_id");

        await context.Database.MigrateAsync("20260913164736_Stage23_ServiceTicketOperationId").ConfigureAwait(false);
        IReadOnlyList<string> custodyColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'service' AND table_name = 'service_custody_events'
            """).ToListAsync().ConfigureAwait(false);
        custodyColumns.Should().BeEmpty();

        await context.Database.MigrateAsync("20260913163837_Stage23_ServiceManagement").ConfigureAwait(false);

        IReadOnlyList<string> revertedTicketColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'service' AND table_name = 'service_tickets'
            """).ToListAsync().ConfigureAwait(false);
        revertedTicketColumns.Should().NotContain("operation_id");

        await context.Database.MigrateAsync("20260913134232_Stage21PaymentAttempts").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'service'
            """).ToListAsync().ConfigureAwait(false);
        remainingTables.Should().BeEmpty();
    }
}
