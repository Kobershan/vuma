using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations.Registry
{
    /// <inheritdoc />
    public partial class Stage06e_TradingGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "suspended_reason",
                schema: "registry",
                table: "company_links",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "registry",
                table: "company_links",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "revoked_reason",
                schema: "registry",
                table: "company_links",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "operator_name",
                schema: "registry",
                table: "company_links",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "operators",
                schema: "registry",
                columns: table => new
                {
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    licence_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operators", x => x.operator_id);
                });

            migrationBuilder.CreateTable(
                name: "premises",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    geo_location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    trading_hours = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_premises", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "premises_bin_layouts",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    premises_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bin_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_premises_bin_layouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "premises_occupancies",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    premises_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occupies_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    occupies_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_premises_occupancies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registry_users",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    contact_details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registry_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminals",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    premises_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_cert_thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    company_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_company_access",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    roles = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    granted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_company_access", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_links_tenant_id_operator_id",
                schema: "registry",
                table: "company_links",
                columns: new[] { "tenant_id", "operator_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operators_tenant_id_is_active",
                schema: "registry",
                table: "operators",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_tenant_id_code",
                schema: "registry",
                table: "premises",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_premises_tenant_id_is_active",
                schema: "registry",
                table: "premises",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_bin_layouts_premises_id_zone_code_bin_code",
                schema: "registry",
                table: "premises_bin_layouts",
                columns: new[] { "premises_id", "zone_code", "bin_code" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_occupancies_premises_id_company_id",
                schema: "registry",
                table: "premises_occupancies",
                columns: new[] { "premises_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registry_users_tenant_id_login",
                schema: "registry",
                table: "registry_users",
                columns: new[] { "tenant_id", "login" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registry_users_tenant_id_operator_id",
                schema: "registry",
                table: "registry_users",
                columns: new[] { "tenant_id", "operator_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terminals_tenant_id_premises_id",
                schema: "registry",
                table: "terminals",
                columns: new[] { "tenant_id", "premises_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terminals_tenant_id_terminal_id",
                schema: "registry",
                table: "terminals",
                columns: new[] { "tenant_id", "terminal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_company_access_registry_user_id_company_id",
                schema: "registry",
                table: "user_company_access",
                columns: new[] { "registry_user_id", "company_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operators",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "premises",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "premises_bin_layouts",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "premises_occupancies",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "registry_users",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "terminals",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "user_company_access",
                schema: "registry");

            migrationBuilder.DropIndex(
                name: "ix_company_links_tenant_id_operator_id",
                schema: "registry",
                table: "company_links");

            migrationBuilder.AlterColumn<string>(
                name: "suspended_reason",
                schema: "registry",
                table: "company_links",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "registry",
                table: "company_links",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "revoked_reason",
                schema: "registry",
                table: "company_links",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "operator_name",
                schema: "registry",
                table: "company_links",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
