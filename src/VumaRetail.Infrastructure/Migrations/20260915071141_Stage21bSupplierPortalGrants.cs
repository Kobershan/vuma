using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage21bSupplierPortalGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_portal_grants",
                schema: "connect",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    retailer_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_portal_grants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_portal_grants_sync_state",
                schema: "connect",
                table: "supplier_portal_grants",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_portal_grants_tenant_id",
                schema: "connect",
                table: "supplier_portal_grants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_portal_grants_tenant_id_company_id",
                schema: "connect",
                table: "supplier_portal_grants",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_portal_grants_tenant_id_connection_id_contact_id",
                schema: "connect",
                table: "supplier_portal_grants",
                columns: new[] { "tenant_id", "connection_id", "contact_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_portal_grants_tenant_id_store_id",
                schema: "connect",
                table: "supplier_portal_grants",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_portal_grants",
                schema: "connect");
        }
    }
}
