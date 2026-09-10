using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage16_BomSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "manufacturing");

            migrationBuilder.CreateTable(
                name: "bills_of_materials",
                schema: "manufacturing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    finished_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finished_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_bills_of_materials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_sync_state",
                schema: "manufacturing",
                table: "bills_of_materials",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_tenant_finished_status",
                schema: "manufacturing",
                table: "bills_of_materials",
                columns: new[] { "tenant_id", "finished_item_id", "finished_variant_id", "status" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_tenant_id",
                schema: "manufacturing",
                table: "bills_of_materials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_tenant_id_company_id",
                schema: "manufacturing",
                table: "bills_of_materials",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_tenant_id_store_id",
                schema: "manufacturing",
                table: "bills_of_materials",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bills_of_materials_tenant_finished_version",
                schema: "manufacturing",
                table: "bills_of_materials",
                columns: new[] { "tenant_id", "finished_item_id", "finished_variant_id", "version" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bills_of_materials",
                schema: "manufacturing");
        }
    }
}
