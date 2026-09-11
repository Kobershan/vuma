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

    [Fact]
    public async Task Stage22_related_transfer_metadata_migration_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260911205614_Stage22RelatedTransfers").ConfigureAwait(false);
        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_requests'
            """).ToListAsync().ConfigureAwait(false);
        columns.Should().Contain(["related_transfer_id", "relation"]);

        await context.Database.MigrateAsync("20260911203305_Stage22TransferReceiptMetadata").ConfigureAwait(false);
        IReadOnlyList<string> remainingColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_requests'
            """).ToListAsync().ConfigureAwait(false);
        remainingColumns.Should().NotContain(["related_transfer_id", "relation"]);
    }

    [Fact]
    public async Task Stage22_related_transfer_idempotency_index_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260911211503_Stage22RelatedTransferIdempotency").ConfigureAwait(false);
        IReadOnlyList<string> indexes = await context.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'registry' AND tablename = 'stock_transfer_requests'
            """).ToListAsync().ConfigureAwait(false);
        indexes.Should().Contain("ux_stock_transfer_requests_related_relation");

        await context.Database.MigrateAsync("20260911205614_Stage22RelatedTransfers").ConfigureAwait(false);
        IReadOnlyList<string> remainingIndexes = await context.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'registry' AND tablename = 'stock_transfer_requests'
            """).ToListAsync().ConfigureAwait(false);
        remainingIndexes.Should().NotContain("ux_stock_transfer_requests_related_relation");
    }

    [Fact]
    public async Task Stage22_delivery_note_migration_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260911211737_Stage22TransferDeliveryNotes").ConfigureAwait(false);
        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'registry'
            """).ToListAsync().ConfigureAwait(false);
        tables.Should().Contain(["stock_transfer_delivery_notes", "stock_transfer_delivery_note_lines"]);

        await context.Database.MigrateAsync("20260911211503_Stage22RelatedTransferIdempotency").ConfigureAwait(false);
        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'registry'
            """).ToListAsync().ConfigureAwait(false);
        remainingTables.Should().NotContain(["stock_transfer_delivery_notes", "stock_transfer_delivery_note_lines"]);
    }

    [Fact]
    public async Task Stage22_transfer_tracking_migration_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260911214001_Stage22TransferTracking").ConfigureAwait(false);
        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_lines'
            """).ToListAsync().ConfigureAwait(false);
        columns.Should().Contain(["batch_reference", "expiry_date", "serial_number"]);

        await context.Database.MigrateAsync("20260911211737_Stage22TransferDeliveryNotes").ConfigureAwait(false);
        IReadOnlyList<string> remainingColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'registry' AND table_name = 'stock_transfer_lines'
            """).ToListAsync().ConfigureAwait(false);
        remainingColumns.Should().NotContain(["batch_reference", "expiry_date", "serial_number"]);
    }
}
