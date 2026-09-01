using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Persistence;

/// <summary>
/// The <c>CLAUDE.md</c> §7 rule 3 columns, checked against <c>information_schema</c> rather than
/// against the EF model.
/// </summary>
/// <remarks>
/// Asserting on the model would only prove that the configuration says what the configuration says.
/// The question worth answering is what PostgreSQL ended up with, because that is what a migration
/// ships to a store and what a hand-written report query will meet.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class BaseEntityMappingTests(PostgresFixture fixture)
{
    /// <summary>Every table an entity maps to. Extended as stages add modules.</summary>
    public static TheoryData<string, string> AllTables => new()
    {
        { "platform", "tenants" },
        { "platform", "stores" },
        { "platform", "audit_entries" },
        { "identity", "users" },
        { "identity", "roles" },
        { "identity", "role_permissions" },
        { "identity", "user_role_assignments" },
        { "identity", "terminals" },
        { "identity", "refresh_tokens" },

        // Stage 10. Stages 06 through 09 did not extend this list, so `catalog`, `partners`,
        // `finance`, `inventory` and `pos` are still absent from it — a real gap, recorded in
        // docs/PROGRESS.md rather than closed here, because adding twenty-two tables at once to a
        // suite this stage did not write is a change whose failures would belong to other stages.
        { "sales", "price_lists" },
        { "sales", "price_list_lines" },
        { "sales", "promotions" },
        { "sales", "promotion_lines" },
        { "sales", "sales_returns" },
        { "sales", "sales_return_lines" },
        { "sales", "price_override_logs" },
    };

    [Theory]
    [MemberData(nameof(AllTables))]
    public async Task Every_table_carries_the_mandatory_columns(string schema, string table)
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        Dictionary<string, ColumnShape> columns = await ReadColumnsAsync(connectionString, schema, table);

        // CLAUDE.md §7 rule 3 plus the two soft-delete columns rule 8 adds.
        columns.Should().ContainKey("id").WhoseValue.DataType.Should().Be("uuid");
        columns.Should().ContainKey("tenant_id").WhoseValue.Should().Match<ColumnShape>(c => c.DataType == "uuid" && !c.IsNullable);
        columns.Should().ContainKey("store_id").WhoseValue.Should().Match<ColumnShape>(c => c.DataType == "uuid" && c.IsNullable);
        columns.Should().ContainKey("created_by");
        columns.Should().ContainKey("updated_by");
        columns.Should().ContainKey("row_version").WhoseValue.DataType.Should().Be("bytea");
        columns.Should().ContainKey("sync_state");
        columns.Should().ContainKey("deleted_at");
        columns.Should().ContainKey("deleted_by");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public async Task Every_timestamp_is_timestamptz(string schema, string table)
    {
        // §7 rule 9. `timestamp without time zone` is the PostgreSQL default and is the single
        // easiest way to lose an hour twice a year in a country that changes its clocks.
        string connectionString = await fixture.CreateDatabaseAsync();

        Dictionary<string, ColumnShape> columns = await ReadColumnsAsync(connectionString, schema, table);

        columns.Values
            .Where(column => column.DataType.StartsWith("timestamp", StringComparison.Ordinal))
            .Should()
            .OnlyContain(column => column.DataType == "timestamp with time zone");

        columns["created_at"].DataType.Should().Be("timestamp with time zone");
        columns["updated_at"].DataType.Should().Be("timestamp with time zone");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public async Task Every_column_name_is_snake_case(string schema, string table)
    {
        // CONVENTIONS.md §2. PostgreSQL folds unquoted identifiers to lower case, so a single
        // PascalCase column means every hand-written query has to quote it forever.
        string connectionString = await fixture.CreateDatabaseAsync();

        Dictionary<string, ColumnShape> columns = await ReadColumnsAsync(connectionString, schema, table);

        columns.Keys.Should().OnlyContain(name => name == name.ToLowerInvariant());
        columns.Keys.Should().OnlyContain(name => !name.Contains(' ', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Money_and_quantity_keep_their_scale_and_their_companion_column()
    {
        // §7 rules 4 and 5. Scale 4 for money so a calculation does not round mid-way, and the
        // currency stored beside it so nobody can add rands to dollars.
        string connectionString = await fixture.CreateModelDatabaseAsync();

        Dictionary<string, ColumnShape> columns =
            await ReadColumnsAsync(connectionString, "platform", "mapping_probes");

        columns["price_amount"].DataType.Should().Be("numeric");
        columns["price_amount"].NumericPrecision.Should().Be(18);
        columns["price_amount"].NumericScale.Should().Be(4);
        columns["price_currency"].DataType.Should().Be("character");

        columns["counted_value"].DataType.Should().Be("numeric");
        columns["counted_value"].NumericPrecision.Should().Be(18);
        columns["counted_value"].NumericScale.Should().Be(6);
        columns["counted_uom"].DataType.Should().Be("character varying");
    }

    [Fact]
    public async Task Money_and_quantity_survive_a_round_trip_through_the_database()
    {
        string connectionString = await fixture.CreateModelDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        Money price = new(1234.5678m, "ZAR");
        Quantity counted = new(2.123456m, "KG");

        await using (ProbeDbContext writer = ProbeDbContext.ForProbe(connectionString))
        {
            writer.Add(MappingProbe.Create(tenantId, "round-trip", price, counted));
            await writer.SaveChangesAsync();
        }

        await using ProbeDbContext reader = ProbeDbContext.ForProbe(connectionString);
        MappingProbe probe = await reader.Set<MappingProbe>().SingleAsync();

        probe.Price.Should().Be(price);
        probe.Price.Currency.Should().Be("ZAR");
        probe.Counted.Should().Be(counted);
        probe.Counted.UnitOfMeasure.Should().Be("KG");
    }

    [Fact]
    public async Task Enums_are_stored_as_text_not_as_an_ordinal()
    {
        // An enum persisted by ordinal turns a reordered enum member into silently relabelled
        // history — and sync_state and the audit action are exactly the columns an investigation
        // reads years later.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        await using (VumaRetailDbContext writer = TestDbContextFactory.For(connectionString))
        {
            writer.Add(Store.Create(tenantId, "JHB01", "Johannesburg"));
            await writer.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sync_state FROM platform.stores LIMIT 1";

        object? value = await command.ExecuteScalarAsync();

        value.Should().Be(nameof(SyncState.Local));
    }

    private static async Task<Dictionary<string, ColumnShape>> ReadColumnsAsync(
        string connectionString,
        string schema,
        string table)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type, is_nullable, numeric_precision, numeric_scale
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        Dictionary<string, ColumnShape> columns = new(StringComparer.Ordinal);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = new ColumnShape(
                reader.GetString(1),
                reader.GetString(2) == "YES",
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4));
        }

        columns.Should().NotBeEmpty($"{schema}.{table} should exist");

        return columns;
    }

    private sealed record ColumnShape(string DataType, bool IsNullable, int? NumericPrecision, int? NumericScale);
}
