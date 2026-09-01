using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>
/// Applies the company identity retrofit to databases created by the complete migration chain.
/// </summary>
/// <remarks>
/// The original retrofit was authored without EF's migration metadata, so it was not discovered by
/// EF Core. This repair is deliberately idempotent and runs after every business table exists,
/// including the audit table that is written on the first save. The migration runner supplies
/// <c>vuma.company_id</c> for a provisioned database; the tenant id is the deterministic fallback
/// for a legacy one-company database and for database tooling that has no company context.
/// </remarks>
[Migration("20260828230000_CompanyIdentityRepair")]
[DbContext(typeof(VumaRetailDbContext))]
public partial class CompanyIdentityRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN
                SELECT t.table_schema, t.table_name
                FROM information_schema.tables t
                WHERE t.table_schema NOT IN ('pg_catalog', 'information_schema', 'registry')
                  AND t.table_type = 'BASE TABLE'
                  AND EXISTS (
                    SELECT 1 FROM information_schema.columns c
                    WHERE c.table_schema = t.table_schema
                      AND c.table_name = t.table_name
                      AND c.column_name = 'tenant_id')
              LOOP
                EXECUTE format(
                  'ALTER TABLE %I.%I ADD COLUMN IF NOT EXISTS company_id uuid',
                  r.table_schema, r.table_name);
                EXECUTE format(
                  'UPDATE %I.%I SET company_id = COALESCE(NULLIF(current_setting(''vuma.company_id'', true), '''')::uuid, tenant_id) WHERE company_id IS NULL',
                  r.table_schema, r.table_name);
                EXECUTE format(
                  'ALTER TABLE %I.%I ALTER COLUMN company_id SET NOT NULL',
                  r.table_schema, r.table_name);
                EXECUTE format(
                  'CREATE INDEX IF NOT EXISTS %I ON %I.%I (tenant_id, company_id)',
                  'ix_' || r.table_name || '_tenant_id_company_id', r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);

        // Keep the retrofit migration safe for databases whose earlier module migration was
        // recorded after its invariant DDL was only partially applied.
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
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN
                SELECT c.table_schema, c.table_name
                FROM information_schema.columns c
                JOIN information_schema.columns t
                  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                WHERE c.column_name = 'company_id'
                  AND t.column_name = 'tenant_id'
                  AND c.table_schema NOT IN ('pg_catalog', 'information_schema', 'registry')
              LOOP
                EXECUTE format('DROP INDEX IF EXISTS %I.%I', r.table_schema,
                  'ix_' || r.table_name || '_tenant_id_company_id');
                EXECUTE format('ALTER TABLE %I.%I ALTER COLUMN company_id DROP NOT NULL',
                  r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }
}
