using Microsoft.EntityFrameworkCore;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Conversations;

/// <summary>Verifies the registry-backed account/company scope migration is reversible.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationScopeMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Conversation_scope_migration_up_and_down_are_reversible()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync().ConfigureAwait(false);
        await using var context = TestDbContextFactory.ForRegistry(connectionString);

        await context.Database.MigrateAsync("20260910180948_Stage22b_ConversationAccountScopes").ConfigureAwait(false);

        IReadOnlyList<string> tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'registry'
            ORDER BY table_name
            """).ToListAsync().ConfigureAwait(false);

        tables.Should().Contain("conversation_account_scopes");

        await context.Database.MigrateAsync("20260910164254_Stage22b_RegistryModelAlignment").ConfigureAwait(false);

        IReadOnlyList<string> remainingTables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'registry' AND table_name = 'conversation_account_scopes'
            """).ToListAsync().ConfigureAwait(false);

        remainingTables.Should().BeEmpty();
    }
}
