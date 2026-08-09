using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ZenithRetail.IntegrationTests.Harness;

namespace ZenithRetail.IntegrationTests.Persistence;

/// <summary>
/// The migration chain — applies, reverses, and re-applies.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §8 requires every migration to be reversible and <c>docs/TESTING.md</c> §6 makes
/// it a CI gate. A <c>Down</c> that was never run is a <c>Down</c> that does not work, and the moment
/// it is needed is the moment an upgrade has gone wrong in a shop on a Saturday.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Every_migration_applies_to_an_empty_database()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);
        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await TableNamesAsync(connectionString)).Should().Contain(["tenants", "stores", "audit_entries"]);
    }

    [Fact]
    public async Task The_model_and_the_migrations_agree()
    {
        // A model change without a migration builds, tests green, and then fails on a customer's
        // database during an upgrade. This is the cheapest place to catch it.
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);
        await context.Database.MigrateAsync();

        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        IDesignTimeModel designTimeModel = context.GetService<IDesignTimeModel>();

        var differences = differ.GetDifferences(
            migrations.ModelSnapshot!.Model.GetRelationalModel(),
            designTimeModel.Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "the migration snapshot is out of date — run `dotnet ef migrations add <name>`");
    }

    [Fact]
    public async Task Down_reverses_the_initial_migration()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        (await TableNamesAsync(connectionString)).Should().Contain("stores");

        // "0" is EF's name for the empty state, before the first migration.
        await migrator.MigrateAsync("0");

        IReadOnlyCollection<string> afterDown = await TableNamesAsync(connectionString);
        afterDown.Should().NotContain("tenants");
        afterDown.Should().NotContain("stores");
        afterDown.Should().NotContain("audit_entries");

        // The migrations history table survives Down on purpose: it is EF's own bookkeeping, not
        // part of the schema the migration created.
        await migrator.MigrateAsync();
        (await TableNamesAsync(connectionString)).Should().Contain(["tenants", "stores", "audit_entries"]);
    }

    private static async Task<IReadOnlyCollection<string>> TableNamesAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform'
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
