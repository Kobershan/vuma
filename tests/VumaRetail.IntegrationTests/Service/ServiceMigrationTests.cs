using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Service;

/// <summary>Verifies the Stage 23 service schema can be applied and removed on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ServiceMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage23_service_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260913163837_Stage23_ServiceManagement").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'service'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);
        tables.Should().BeEquivalentTo(
            ["repair_jobs", "service_part_usages", "service_slas", "service_tickets", "warranty_claims"],
            options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260913134232_Stage21PaymentAttempts").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'service'
            """).ToListAsync().ConfigureAwait(false);
        remainingTables.Should().BeEmpty();
    }
}
