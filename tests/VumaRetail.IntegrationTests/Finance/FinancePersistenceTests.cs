using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Finance;

/// <summary>
/// Stage 07's schema against a real PostgreSQL: the migration, the money mapping, and the
/// immutability guard.
/// </summary>
/// <remarks>
/// These are the assertions that cannot be made without a database. The unit tests prove a journal
/// balances; only this proves the balanced journal survives a round trip, that a posted one cannot
/// then be edited, and that the <c>finance</c> schema actually reverses — the whole Finance model was
/// unconstructible at one point in this stage's history while the solution still compiled and every
/// unit test passed (ADR-067).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class FinancePersistenceTests(PostgresFixture fixture)
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    private static Money Rand(decimal amount) => new(amount, "ZAR");

    private static Journal BalancedJournal(Guid periodId, Guid debitAccount, Guid creditAccount)
        => Journal.Post(
            TenantId, null, periodId, "JNL-000001", DateTimeOffset.UtcNow, "user:test",
            "manual", "manual", "TEST-1",
            [
                JournalLineDraft.DebitLine(debitAccount, Rand(115m), "debit side"),
                JournalLineDraft.CreditLine(creditAccount, Rand(115m), "credit side"),
            ],
            "A round-trip test posting");

    [Fact]
    public async Task The_finance_schema_applies_reverses_and_re_applies()
    {
        // The stage's own "up → down → up on an empty database" acceptance criterion. The generic
        // MigrationTests walk the whole chain; this one names the finance tables specifically, so a
        // Down that quietly leaves the finance schema behind is a failure here rather than a
        // surprise on the next tenant's upgrade.
        string connectionString = await fixture.CreateEmptyDatabaseAsync();

        await using VumaRetailDbContext context = TestDbContextFactory.For(connectionString);
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        (await FinanceTableNamesAsync(connectionString)).Should().Contain(
            ["accounts", "accounting_periods", "journals", "journal_lines", "posting_rules",
             "ar_invoices", "ap_invoices", "bank_accounts", "bank_statement_lines", "tax_rules"]);

        await migrator.MigrateAsync("0");
        (await FinanceTableNamesAsync(connectionString)).Should().BeEmpty();

        await migrator.MigrateAsync();
        (await FinanceTableNamesAsync(connectionString)).Should().Contain(["journals", "journal_lines"]);
    }

    [Fact]
    public async Task A_posted_journal_round_trips_with_its_debits_and_credits_intact()
    {
        // JournalLine stores four plain columns and computes Money? back from them (ADR-067). This
        // is the test that would have caught the original mapping: it could not even build a model.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid periodId = UuidV7.NewGuid();
        Guid debitAccount = UuidV7.NewGuid();
        Guid creditAccount = UuidV7.NewGuid();
        Guid journalId;

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            Journal journal = BalancedJournal(periodId, debitAccount, creditAccount);
            journalId = journal.Id;
            seed.Add(journal);
            await seed.SaveChangesAsync();
        }

        await using VumaRetailDbContext read =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId));

        Journal stored = await read.Journals
            .Include(journal => journal.Lines)
            .SingleAsync(journal => journal.Id == journalId);

        stored.Lines.Should().HaveCount(2);

        JournalLine debit = stored.Lines.Single(line => line.AccountId == debitAccount);
        debit.Debit.Should().Be(Rand(115m));
        debit.Credit.Should().BeNull();
        debit.CreditAmount.Should().BeNull();
        debit.CreditCurrency.Should().BeNull();

        JournalLine credit = stored.Lines.Single(line => line.AccountId == creditAccount);
        credit.Credit.Should().Be(Rand(115m));
        credit.Debit.Should().BeNull();

        stored.Lines.Sum(line => line.SignedAmount).Should().Be(0m,
            "a journal that balanced when posted must still balance when read back");
    }

    [Fact]
    public async Task A_journal_line_carrying_both_a_debit_and_a_credit_is_refused_by_the_database_itself()
    {
        // The domain refuses it, and so does a check constraint — because the domain is not the only
        // thing that can write this table. A restore, a sync apply or a hand-run SQL fix all reach
        // the table directly.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid journalId;

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            Journal journal = BalancedJournal(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
            journalId = journal.Id;
            seed.Add(journal);
            await seed.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO finance.journal_lines
                (id, tenant_id, journal_id, line_number, account_id, description,
                 debit_amount, debit_currency, credit_amount, credit_currency,
                 created_at, created_by, updated_at, updated_by, row_version, sync_state, sync_stamp)
            VALUES
                (gen_random_uuid(), @tenantId, @journalId, 99, gen_random_uuid(), 'both sides',
                 100, 'ZAR', 100, 'ZAR',
                 now(), 'test', now(), 'test', '\x00'::bytea, 'Pending', 'test-stamp')
            """;
        command.Parameters.AddWithValue("tenantId", TenantId);
        command.Parameters.AddWithValue("journalId", journalId);

        Func<Task> inserting = () => command.ExecuteNonQueryAsync();

        (await inserting.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_journal_lines_exactly_one_side");
    }

    [Fact]
    public async Task A_journal_line_carrying_neither_a_debit_nor_a_credit_is_refused_by_the_database_itself()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid journalId;

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            Journal journal = BalancedJournal(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
            journalId = journal.Id;
            seed.Add(journal);
            await seed.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO finance.journal_lines
                (id, tenant_id, journal_id, line_number, account_id, description,
                 debit_amount, debit_currency, credit_amount, credit_currency,
                 created_at, created_by, updated_at, updated_by, row_version, sync_state, sync_stamp)
            VALUES
                (gen_random_uuid(), @tenantId, @journalId, 98, gen_random_uuid(), 'neither side',
                 NULL, NULL, NULL, NULL,
                 now(), 'test', now(), 'test', '\x00'::bytea, 'Pending', 'test-stamp')
            """;
        command.Parameters.AddWithValue("tenantId", TenantId);
        command.Parameters.AddWithValue("journalId", journalId);

        Func<Task> inserting = () => command.ExecuteNonQueryAsync();

        (await inserting.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_journal_lines_exactly_one_side");
    }

    [Fact]
    public async Task A_posted_journal_cannot_be_modified()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid journalId;

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            Journal journal = BalancedJournal(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
            journalId = journal.Id;
            seed.Add(journal);
            await seed.SaveChangesAsync();
        }

        await using VumaRetailDbContext amend =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId));

        Journal stored = await amend.Journals.SingleAsync(journal => journal.Id == journalId);
        amend.Entry(stored).Property(journal => journal.Narration).CurrentValue = "Quietly reworded";

        Func<Task> saving = () => amend.SaveChangesAsync();

        await saving.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable record*");
    }

    [Fact]
    public async Task A_posted_journal_cannot_be_deleted()
    {
        // Rule 8 rewrites an ordinary delete as a soft delete; rule 7 outranks it for an immutable
        // record, which must refuse outright rather than quietly acquiring a deleted_at.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid journalId;

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            Journal journal = BalancedJournal(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
            journalId = journal.Id;
            seed.Add(journal);
            await seed.SaveChangesAsync();
        }

        await using VumaRetailDbContext remove =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId));

        Journal stored = await remove.Journals.SingleAsync(journal => journal.Id == journalId);
        remove.Remove(stored);

        Func<Task> saving = () => remove.SaveChangesAsync();

        await saving.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable record*");
    }

    [Fact]
    public async Task Money_is_stored_at_four_decimal_places_with_its_currency_beside_it()
    {
        // §7 rule 4, checked against the column types rather than the model, because the model is
        // what the migration was generated from and this is what a customer's database actually has.
        string connectionString = await fixture.CreateDatabaseAsync();

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type, numeric_precision, numeric_scale, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'finance' AND table_name = 'journal_lines'
              AND column_name IN ('debit_amount', 'debit_currency', 'credit_amount', 'credit_currency')
            ORDER BY column_name
            """;

        Dictionary<string, (string DataType, int? Precision, int? Scale, int? Length)> columns = [];

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4));
            }
        }

        columns["debit_amount"].DataType.Should().Be("numeric");
        columns["debit_amount"].Precision.Should().Be(18);
        columns["debit_amount"].Scale.Should().Be(4);
        columns["credit_amount"].Scale.Should().Be(4);
        columns["debit_currency"].Length.Should().Be(3);
        columns["credit_currency"].Length.Should().Be(3);
    }

    [Fact]
    public async Task A_tax_rule_round_trips_so_a_rate_change_is_a_row_not_a_deployment()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using (VumaRetailDbContext seed =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId)))
        {
            seed.Add(TaxRule.Define(
                TenantId, "STANDARD", "Standard rate", 0.15m, TaxTreatment.Inclusive, new DateOnly(2026, 4, 1)));
            seed.Add(TaxRule.Define(
                TenantId, "ZERO", "Zero rated", 0m, TaxTreatment.Exclusive, new DateOnly(2026, 4, 1)));
            await seed.SaveChangesAsync();
        }

        await using VumaRetailDbContext read =
            TestDbContextFactory.For(connectionString, tenant: TestTenantContext.For(TenantId));

        List<TaxRule> rules = await read.TaxRules.OrderBy(rule => rule.Code).ToListAsync();

        rules.Should().HaveCount(2);
        rules[0].Code.Should().Be("STANDARD");
        rules[0].Rate.Should().Be(0.15m);
        rules[0].Treatment.Should().Be(TaxTreatment.Inclusive);
        rules[1].Code.Should().Be("ZERO");
        rules[1].Rate.Should().Be(0m);
    }

    private static async Task<IReadOnlyCollection<string>> FinanceTableNamesAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name FROM information_schema.tables WHERE table_schema = 'finance'
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
