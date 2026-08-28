using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Completes the company identity retrofit with its tenant/company indexes.</summary>
public partial class CompanyIdentityModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
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
                EXECUTE format(
                  'CREATE INDEX IF NOT EXISTS %I ON %I.%I (tenant_id, company_id)',
                  'ix_' || r.table_name || '_tenant_id_company_id', r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN
                SELECT schemaname, indexname
                FROM pg_indexes
                WHERE indexname LIKE 'ix~_%~_tenant~_id~_company~_id' ESCAPE '~'
                  AND schemaname NOT IN ('pg_catalog', 'information_schema', 'registry')
              LOOP
                EXECUTE format('DROP INDEX IF EXISTS %I.%I', r.schemaname, r.indexname);
              END LOOP;
            END $$;
            """);
    }
}
