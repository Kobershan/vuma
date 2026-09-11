using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Conversations;

/// <summary>Verifies the Stage 22b conversation migration is reversible on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stage22b_durable_idempotency_migration_is_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260911203729_Stage22bDurableConversationIdempotency").ConfigureAwait(false);

        IReadOnlyList<string> columns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'conversations' AND table_name = 'conversation_turns'
            """).ToListAsync().ConfigureAwait(false);
        columns.Should().Contain("idempotency_key");

        await context.Database.MigrateAsync("20260910161332_Stage22b_Conversations").ConfigureAwait(false);

        IReadOnlyList<string> remainingColumns = await context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'conversations' AND table_name = 'conversation_turns'
            """).ToListAsync().ConfigureAwait(false);
        remainingColumns.Should().NotContain("idempotency_key");
    }

    [Fact]
    public async Task Stage22b_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.For(connectionString);

        await context.Database.MigrateAsync("20260910161332_Stage22b_Conversations").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'conversations'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().BeEquivalentTo(
            ["conversation_turns", "conversations", "document_delivery_tokens"],
            options => options.WithStrictOrdering());

        await context.Database.MigrateAsync("20260910042748_Stage14_Revision4Rework").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'conversations'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
