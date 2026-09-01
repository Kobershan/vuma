using Microsoft.EntityFrameworkCore.Migrations;

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Adds the redundant company identity to every business table.</summary>
public partial class CompanyIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN SELECT table_schema, table_name FROM information_schema.tables
                WHERE table_schema NOT IN ('pg_catalog','information_schema','registry') AND table_type='BASE TABLE'
              LOOP
                EXECUTE format('ALTER TABLE %I.%I ADD COLUMN IF NOT EXISTS company_id uuid', r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE r record; BEGIN
              FOR r IN SELECT table_schema, table_name FROM information_schema.columns
                WHERE column_name='company_id' AND table_schema NOT IN ('pg_catalog','information_schema','registry')
              LOOP
                EXECUTE format('ALTER TABLE %I.%I DROP COLUMN IF EXISTS company_id', r.table_schema, r.table_name);
              END LOOP;
            END $$;
            """);
    }
}
