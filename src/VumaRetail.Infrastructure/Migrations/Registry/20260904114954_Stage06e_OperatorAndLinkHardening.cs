using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations.Registry
{
    /// <inheritdoc />
    public partial class Stage06e_OperatorAndLinkHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-121's database half: a CHECK cannot reference another table, so the
            // operator-match invariant lives in a trigger. The aggregate and ProposeAsync
            // refuse first; this refuses a row that reaches the database anyway.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION registry.enforce_company_link_operator_match()
                RETURNS trigger AS $$
                DECLARE
                    v_operator_a uuid;
                    v_operator_b uuid;
                BEGIN
                    SELECT operator_id INTO v_operator_a FROM registry.companies WHERE id = NEW.company_a_id;
                    SELECT operator_id INTO v_operator_b FROM registry.companies WHERE id = NEW.company_b_id;

                    IF v_operator_a IS NULL OR v_operator_b IS NULL THEN
                        RAISE EXCEPTION 'REGISTRY_COMPANY_NOT_FOUND: a company link references an unknown company';
                    END IF;

                    IF v_operator_a <> v_operator_b OR NEW.operator_id <> v_operator_a THEN
                        RAISE EXCEPTION 'REGISTRY_DIFFERENT_OPERATORS: a company link must sit under both companies operator';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_company_links_operator_match ON registry.company_links;

                CREATE TRIGGER trg_company_links_operator_match
                BEFORE INSERT OR UPDATE OF company_a_id, company_b_id, operator_id ON registry.company_links
                FOR EACH ROW EXECUTE FUNCTION registry.enforce_company_link_operator_match();
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "registry",
                table: "user_company_access",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "registry",
                table: "premises_occupancies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "registry",
                table: "premises_bin_layouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_by_a_at",
                schema: "registry",
                table: "company_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_by_a_by",
                schema: "registry",
                table: "company_links",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_by_a_fingerprint",
                schema: "registry",
                table: "company_links",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_by_b_at",
                schema: "registry",
                table: "company_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_by_b_by",
                schema: "registry",
                table: "company_links",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_by_b_fingerprint",
                schema: "registry",
                table: "company_links",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operator_id",
                schema: "registry",
                table: "companies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_user_company_access_tenant_id_registry_user_id",
                schema: "registry",
                table: "user_company_access",
                columns: new[] { "tenant_id", "registry_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_occupancies_tenant_id_premises_id",
                schema: "registry",
                table: "premises_occupancies",
                columns: new[] { "tenant_id", "premises_id" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_bin_layouts_tenant_id_premises_id",
                schema: "registry",
                table: "premises_bin_layouts",
                columns: new[] { "tenant_id", "premises_id" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_operator_id",
                schema: "registry",
                table: "companies",
                columns: new[] { "tenant_id", "operator_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_company_access_tenant_id_registry_user_id",
                schema: "registry",
                table: "user_company_access");

            migrationBuilder.DropIndex(
                name: "ix_premises_occupancies_tenant_id_premises_id",
                schema: "registry",
                table: "premises_occupancies");

            migrationBuilder.DropIndex(
                name: "ix_premises_bin_layouts_tenant_id_premises_id",
                schema: "registry",
                table: "premises_bin_layouts");

            migrationBuilder.DropIndex(
                name: "ix_companies_tenant_id_operator_id",
                schema: "registry",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "registry",
                table: "user_company_access");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "registry",
                table: "premises_occupancies");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "registry",
                table: "premises_bin_layouts");

            migrationBuilder.DropColumn(
                name: "accepted_by_a_at",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "accepted_by_a_by",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "accepted_by_a_fingerprint",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "accepted_by_b_at",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "accepted_by_b_by",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "accepted_by_b_fingerprint",
                schema: "registry",
                table: "company_links");

            migrationBuilder.DropColumn(
                name: "operator_id",
                schema: "registry",
                table: "companies");

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_company_links_operator_match ON registry.company_links;
                DROP FUNCTION IF EXISTS registry.enforce_company_link_operator_match();
                """);
        }
    }
}
