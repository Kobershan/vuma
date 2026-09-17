using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Marketing;

/// <summary>Verifies the durable marketing journey migration is reversible on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class MarketingMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage22_marketing_journeys_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260914161856_Stage22MarketingJourneys").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'marketing'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().Contain(["attribution_events", "journey_definitions", "journey_enrollments"]);

        await context.Database.MigrateAsync("20260914041306_Stage22MarketingDeliveryMetadata").ConfigureAwait(false);

        IReadOnlyList<string> revertedTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'marketing'
            """).ToListAsync().ConfigureAwait(false);

        revertedTables.Should().NotContain("attribution_events");
        revertedTables.Should().NotContain("journey_definitions");
        revertedTables.Should().NotContain("journey_enrollments");
    }
}
