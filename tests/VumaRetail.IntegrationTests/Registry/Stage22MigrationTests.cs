using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

[Collection(PostgresCollection.Name)]
public sealed class Stage22MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage22_transfer_receipt_metadata_migration_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260911203305_Stage22TransferReceiptMetadata").ConfigureAwait(false);

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_lines'
            ORDER BY ordinal_position
            """).ToListAsync().ConfigureAwait(false);

        columns.Should().Contain(["receiver_location_id", "unit_cost_at_transfer_amount", "unit_cost_at_transfer_currency"]);

        await context.Database.MigrateAsync("20260911190234_Stage22TransferLines").ConfigureAwait(false);

        IReadOnlyList<string> remainingColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_lines'
            """).ToListAsync().ConfigureAwait(false);

        remainingColumns.Should().NotContain(["receiver_location_id", "unit_cost_at_transfer_amount", "unit_cost_at_transfer_currency"]);
    }
}
