using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class SyncRegistryModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_routing_index",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    as_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_retired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_routing_index", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_links",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scopes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_a = table.Column<bool>(type: "boolean", nullable: false),
                    accepted_by_b = table.Column<bool>(type: "boolean", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_reason = table.Column<string>(type: "text", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "text", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_exposure_entries",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_exposure_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_groups",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    limit = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_holds",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_holds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_group_members",
                schema: "registry",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_limit = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_group_members", x => new { x.tenant_id, x.credit_group_id, x.company_id });
                    table.ForeignKey(
                        name: "fk_credit_group_members_credit_groups_credit_group_id",
                        column: x => x.credit_group_id,
                        principalSchema: "registry",
                        principalTable: "credit_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "company_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "ix_company_links_tenant_id_company_a_id_company_b_id",
                schema: "registry",
                table: "company_links",
                columns: new[] { "tenant_id", "company_a_id", "company_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_links_tenant_id_status",
                schema: "registry",
                table: "company_links",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_exposure_entries_tenant_id_credit_group_id_company_id",
                schema: "registry",
                table: "credit_exposure_entries",
                columns: new[] { "tenant_id", "credit_group_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_group_members_credit_group_id",
                schema: "registry",
                table: "credit_group_members",
                column: "credit_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_holds_tenant_id_credit_group_id_state",
                schema: "registry",
                table: "credit_holds",
                columns: new[] { "tenant_id", "credit_group_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_routing_index",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "company_links",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "credit_exposure_entries",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "credit_group_members",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "credit_holds",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "credit_groups",
                schema: "registry");
        }
    }
}
