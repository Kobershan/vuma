using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VumaRetail.Infrastructure.Persistence.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>
/// The registry's own migration chain — applies, reverses, and re-applies. Mirrors
/// <c>Persistence.MigrationTests</c> for <see cref="VumaRegistryDbContext"/>.
/// </summary>
/// <remarks>
/// Exists because a real defect reached this branch without it: the registry's first migration was
/// regenerated with genuinely empty <c>Up</c>/<c>Down</c> bodies (a fully-populated
/// <c>Designer.cs</c> but no executable content), and nothing in the diff at the time actually ran
/// <c>Database.Migrate()</c> against a real PostgreSQL database for this context —
/// <c>RegistryRulesTests</c> builds its model straight from <c>OnModelCreating</c>, never from applying
/// the migration chain, so it could not see this class of bug. A second, separate defect (an
/// <c>Entity.CompanyId</c> the registry should never have mapped) was likewise only caught by inspecting
/// an actually-applied schema, not by reading migration source or a design-time model diff. Both are
/// exactly what these tests now assert on every run.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RegistryMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Every_migration_applies_to_an_empty_database()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using VumaRegistryDbContext context = TestRegistryDbContextFactory.For(connectionString);
        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        (await TableNamesAsync(connectionString)).Should().Contain(
            ["companies", "company_groups", "company_group_members", "saga_intents", "saga_legs", "outbox_messages"]);
    }

    [Fact]
    public async Task The_model_and_the_migrations_agree()
    {
        // A model change without a migration builds, tests green, and then fails on a customer's
        // database during an upgrade (the same reasoning as Persistence.MigrationTests). This is also
        // exactly the check that would have caught the empty-Up/Down defect this test class exists to
        // guard against: an empty migration and a fully-populated snapshot disagree.
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using VumaRegistryDbContext context = TestRegistryDbContextFactory.For(connectionString);
        await context.Database.MigrateAsync();

        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        IDesignTimeModel designTimeModel = context.GetService<IDesignTimeModel>();

        var differences = differ.GetDifferences(
            migrations.ModelSnapshot!.Model.GetRelationalModel(),
            designTimeModel.Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "the registry migration snapshot is out of date — run `dotnet ef migrations add <name> "
            + "--context VumaRegistryDbContext --output-dir Migrations/Registry`");
    }

    [Fact]
    public async Task No_registry_table_carries_a_company_id_column()
    {
        // The ground-truth check for ADR-140's registry exclusion, against an actually-applied schema
        // rather than the in-memory model RegistryRulesTests already covers.
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using VumaRegistryDbContext context = TestRegistryDbContextFactory.For(connectionString);
        await context.Database.MigrateAsync();

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = 'registry' AND column_name = 'company_id'
            """;

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeFalse(
            "no registry table should carry company_id — a registry row spans companies by design (ADR-099, ADR-140)");
    }

    [Fact]
    public async Task Down_reverses_the_initial_migration()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using VumaRegistryDbContext context = TestRegistryDbContextFactory.For(connectionString);
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        (await TableNamesAsync(connectionString)).Should().Contain("companies");

        // "0" is EF's name for the empty state, before the first migration.
        await migrator.MigrateAsync("0");

        IReadOnlyCollection<string> afterDown = await TableNamesAsync(connectionString);
        afterDown.Should().NotContain("companies");
        afterDown.Should().NotContain("saga_legs");

        // The migrations history table survives Down on purpose: it is EF's own bookkeeping, not part
        // of the schema the migration created.
        await migrator.MigrateAsync();
        (await TableNamesAsync(connectionString)).Should().Contain(["companies", "saga_intents", "saga_legs"]);
    }

    private static async Task<IReadOnlyCollection<string>> TableNamesAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name FROM information_schema.tables WHERE table_schema = 'registry'
            """;

        List<string> tables = [];

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
