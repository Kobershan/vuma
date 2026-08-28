using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>
/// Applies the company identity retrofit to databases created by the complete migration chain.
/// </summary>
/// <remarks>
/// The original retrofit was authored without EF's migration metadata, so it was not discovered by
/// EF Core. This forward-only repair is deliberately idempotent and runs after every business table
/// exists, including the audit table that is written on the first save.
/// </remarks>
[Migration("20260828230000_CompanyIdentityRepair")]
[DbContext(typeof(VumaRetailDbContext))]
public partial class CompanyIdentityRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN SELECT table_schema, table_name FROM information_schema.tables
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema', 'registry')
                  AND table_type = 'BASE TABLE'
              LOOP
                EXECUTE format(
                  'ALTER TABLE %I.%I ADD COLUMN IF NOT EXISTS company_id uuid',
                  r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN SELECT table_schema, table_name FROM information_schema.columns
                WHERE column_name = 'company_id'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema', 'registry')
              LOOP
                EXECUTE format(
                  'ALTER TABLE %I.%I DROP COLUMN IF EXISTS company_id',
                  r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }
}
