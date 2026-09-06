using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Restores database invariants missing from some previously migrated databases.</summary>
/// <remarks>
/// The invariants are already part of the intended model and the original module migrations. This
/// idempotent repair covers databases whose migration history was recorded after an incomplete
/// application of those operations. Down is intentionally a no-op: removing an invariant that may
/// have pre-dated this repair would make a rollback less safe than leaving it in place.
/// </remarks>
[Migration("20260829100000_PersistenceInvariantRepair")]
[DbContext(typeof(VumaRetailDbContext))]
public partial class PersistenceInvariantRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF to_regclass('procurement.rfq_responses') IS NOT NULL
                 AND NOT EXISTS (
                   SELECT 1 FROM pg_indexes
                   WHERE schemaname = 'procurement'
                     AND indexname = 'ux_rfq_responses_rfq_id_partner_id') THEN
                CREATE UNIQUE INDEX ux_rfq_responses_rfq_id_partner_id
                  ON procurement.rfq_responses (rfq_id, partner_id)
                  WHERE deleted_at IS NULL;
              END IF;

              IF to_regclass('finance.journal_lines') IS NOT NULL
                 AND NOT EXISTS (
                   SELECT 1 FROM pg_constraint
                   WHERE connamespace = 'finance'::regnamespace
                     AND conrelid = 'finance.journal_lines'::regclass
                     AND conname = 'ck_journal_lines_exactly_one_side') THEN
                ALTER TABLE finance.journal_lines
                  ADD CONSTRAINT ck_journal_lines_exactly_one_side
                  CHECK (((debit_amount IS NOT NULL)::int + (credit_amount IS NOT NULL)::int) = 1);
              END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // See the remarks: these are durable model invariants, not disposable repair artifacts.
    }
}
