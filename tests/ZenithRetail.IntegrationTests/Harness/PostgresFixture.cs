using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ZenithRetail.IntegrationTests.Harness;

/// <summary>
/// One PostgreSQL server for the whole test assembly, migrated once into a template database.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to get a server, in this order (ADR-036):
/// </para>
/// <list type="number">
/// <item><c>ZENITH_TEST_POSTGRES</c> — a connection string. Used by CI's service container and by
/// <c>scripts/pg-test.sh</c>, which starts a throwaway cluster from the locally installed PostgreSQL
/// binaries on a machine with no Docker.</item>
/// <item>Testcontainers, the path <c>docs/TESTING.md</c> §2 names, whenever Docker is available.</item>
/// </list>
/// <para>
/// If neither works the fixture throws with the command that fixes it. It deliberately does not skip:
/// <c>CLAUDE.md</c> §1 says a stage whose tests cannot run must not be marked DONE, and a suite that
/// quietly skips its only real database tests reports green while proving nothing.
/// </para>
/// <para>
/// Migrations run once, into <c>zenith_template</c>. Each test then clones that with
/// <c>CREATE DATABASE … TEMPLATE</c>, which is a file copy rather than a re-run of the migration
/// chain — full isolation between tests without paying for it per test.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "zenith_template";

    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;
    private int _databaseCounter;

    /// <summary>Connection string for the server's maintenance database.</summary>
    public string AdminConnectionString => _adminConnectionString;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _adminConnectionString = await ResolveServerAsync().ConfigureAwait(false);

        await CreateTemplateDatabaseAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Clones the migrated template into a fresh database and returns its connection string.
    /// </summary>
    /// <returns>A connection string to a database nothing else is using.</returns>
    public async Task<string> CreateDatabaseAsync()
    {
        string name = DatabaseName($"zenith_test_{Interlocked.Increment(ref _databaseCounter)}");

        await ExecuteOnServerAsync($"""CREATE DATABASE "{name}" TEMPLATE "{TemplateDatabase}" """)
            .ConfigureAwait(false);

        return ConnectionStringFor(name);
    }

    /// <summary>Creates an empty database with no schema at all, for the migration tests.</summary>
    /// <returns>A connection string to a database nothing else is using.</returns>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        string name = DatabaseName("zenith_migrate");

        await ExecuteOnServerAsync($"""CREATE DATABASE "{name}" """).ConfigureAwait(false);

        return ConnectionStringFor(name);
    }

    /// <summary>
    /// A unique database name inside PostgreSQL's 63-byte identifier limit — which truncates
    /// silently rather than erroring, so two "unique" names could collide.
    /// </summary>
    private static string DatabaseName(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using NpgsqlConnection connection = new(_adminConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a database built from the EF model rather than from the migration chain, so the
    /// mapping probe's table exists.
    /// </summary>
    /// <returns>A connection string to a database nothing else is using.</returns>
    /// <remarks>
    /// Deliberately not the same path as <see cref="CreateDatabaseAsync"/>. Migrations are what ships
    /// to a store and are tested as such; this one answers the different question of what the mapping
    /// conventions produce in PostgreSQL, for a type no migration covers yet.
    /// </remarks>
    public async Task<string> CreateModelDatabaseAsync()
    {
        string name = DatabaseName("zenith_model");

        await ExecuteOnServerAsync($"""CREATE DATABASE "{name}" """).ConfigureAwait(false);

        string connectionString = ConnectionStringFor(name);

        await using ProbeDbContext context = ProbeDbContext.ForProbe(connectionString);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return connectionString;
    }

    /// <summary>Builds a connection string for a named database on the fixture's server.</summary>
    /// <param name="database">The database name.</param>
    public string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = database }.ConnectionString;

    private async Task<string> ResolveServerAsync()
    {
        string? external = Environment.GetEnvironmentVariable("ZENITH_TEST_POSTGRES");

        if (!string.IsNullOrWhiteSpace(external) && await CanConnectAsync(external).ConfigureAwait(false))
        {
            return external;
        }

        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("postgres")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            return _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                """
                No PostgreSQL available for the integration tests, and they are not optional.

                Either start Docker (Testcontainers will take it from there), or run:

                    scripts/pg-test.sh start

                which starts a throwaway cluster from the locally installed PostgreSQL binaries and
                prints the ZENITH_TEST_POSTGRES export to use.
                """,
                exception);
        }
    }

    private static async Task<bool> CanConnectAsync(string connectionString)
    {
        try
        {
            await using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task CreateTemplateDatabaseAsync()
    {
        await using (NpgsqlConnection connection = new(_adminConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"""
                DROP DATABASE IF EXISTS "{TemplateDatabase}" WITH (FORCE);
                CREATE DATABASE "{TemplateDatabase}";
                """;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Pooling off for this one connection. `CREATE DATABASE … TEMPLATE` refuses to run while
        // anything is connected to the source, and a pooled connection stays open long after the
        // migration finishes — so every clone afterwards would fail with 55006.
        string templateConnection = new NpgsqlConnectionStringBuilder(ConnectionStringFor(TemplateDatabase))
        {
            Pooling = false,
        }.ConnectionString;

        // The real migration chain, not EnsureCreated. EnsureCreated builds the schema from the model
        // and would happily pass while the migrations that ship to a store are broken.
        await using (ZenithRetailDbContext context = TestDbContextFactory.For(templateConnection))
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }

        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>Binds <see cref="PostgresFixture"/> to every test class in the assembly.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "postgres";
}
